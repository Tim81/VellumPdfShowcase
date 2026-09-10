using VellumPdf.Encryption;
using VellumPdf.Fonts;
using VellumPdf.Images;
using VellumPdf.Layout;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdf.Layout.Elements.Table;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Web.Generation;

/// <summary>
/// Renders a <see cref="DocumentSpec"/> into PDF bytes by calling the real
/// <c>VellumPdf.Layout</c> API. Every asset the document needs (embedded font
/// bytes, image bytes, an ICC profile) travels inside the <see cref="DocumentSpec"/>
/// itself, so this type fetches nothing and writes only into a
/// <see cref="MemoryStream"/>. It never touches the file system, which keeps it
/// exercisable from a test host with no browser present.
/// </summary>
/// <remarks>
/// Every element type below (<c>Heading</c>, <c>Paragraph</c>, <c>ListElement</c>,
/// <c>ListItem</c>, <c>TableElement</c>, <c>Cell</c>, <c>LayoutImage</c>,
/// <c>PieChart</c>, <c>LineSeparator</c>, <c>RunningBand</c>, <c>TextStyle</c>)
/// exposes its settable members as <see langword="init"/>, not <see langword="set"/>,
/// so every property this code assigns is set inside the single object-initializer
/// expression that constructs the instance; only <c>Document</c> and
/// <c>PdfDocumentInfo</c> use ordinary mutable properties. Fallback values used
/// for an unset optional spec field (<see cref="BuildCell"/>'s <c>Padding</c>,
/// <see cref="BuildTable"/>'s <c>BorderWidth</c> and <c>BorderColor</c>,
/// <see cref="BuildList"/>'s <c>Indent</c>, and every element's <c>Margins</c>)
/// are hardcoded literals, each checked against the library's own default by
/// hand rather than read off a default-constructed instance at run time. What
/// actually guards them against drifting out of step with the library is the
/// round-trip sample built with every optional field left unset: it fails the
/// moment a fallback here stops matching what the library itself defaults to.
/// </remarks>
public static class SpecRenderer
{
    /// <summary>Builds the document described by <paramref name="spec"/> and returns its PDF bytes.</summary>
    /// <remarks>
    /// Exception contract: every failure this method can produce, other than
    /// an <see cref="OutOfMemoryException"/>, surfaces as
    /// <see cref="ArgumentException"/>
    /// (including its <see cref="ArgumentOutOfRangeException"/> and
    /// <see cref="ArgumentNullException"/> subtypes, for a malformed <paramref name="spec"/>
    /// the model failed to reject) or <see cref="InvalidOperationException"/>
    /// (for everything the library itself refuses only once construction is
    /// under way: a malformed image or font, an inconsistent ICC profile, an
    /// object-streams-plus-encryption or PDF/A-plus-encryption combination,
    /// a page too small to lay one line out on, or a single element demanding
    /// more than the library's ceiling of 50,000 page continuations). A caller
    /// that wants one catch clause to be complete can therefore catch
    /// <see cref="ArgumentException"/>.
    /// Before this contract was made uniform, <c>Document.Encrypt</c> and
    /// <c>Document.Save</c> were called unwrapped: measured directly, a bad
    /// combination of settings could throw <see cref="NotSupportedException"/>,
    /// <see cref="InvalidOperationException"/>, <see cref="ArgumentOutOfRangeException"/>
    /// or <see cref="ArgumentException"/> depending on which rule it broke,
    /// while every wrapped path already normalised to <see cref="InvalidOperationException"/>
    /// alone, so a caller catching only that type let three others through.
    /// NOTE: this deliberately does not guard <see cref="DocumentSpec.UseObjectStreams"/>
    /// combined with <see cref="DocumentSpec.Encryption"/>, consistently with
    /// every other cross-feature incompatibility the library enforces at save
    /// time rather than at construction; see the remark on
    /// <see cref="DocumentSpec.UseObjectStreams"/>.
    /// <para>
    /// Cycle 7 review: this contract was FALSE until <see cref="TextRunSpec"/>
    /// validated its own members. <see cref="TextRunSpec"/> was, before that
    /// fix, this model's one bare positional record with neither member
    /// checked at all; a <see cref="TextRunSpec.Style"/> of
    /// <see langword="null"/> passed <see cref="ParagraphSpec"/>'s own
    /// construction untouched and reached <see cref="NullReferenceException"/>
    /// from several different dereferences downstream, none of them
    /// <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/>.
    /// See the remark on <see cref="TextRunSpec"/> for where each of those
    /// dereferences was.
    /// </para>
    /// <para>
    /// Round nine review: the SAME shape of gap survived in three further
    /// <see langword="required"/> members with no null check of their own,
    /// found by the identical reasoning applied to the rest of the model
    /// rather than assumed closed once <see cref="TextRunSpec"/> was fixed: a
    /// <see cref="RunningBandSpec"/> with a <see langword="null"/> <see cref="RunningBandSpec.Style"/>,
    /// a <see langword="null"/> <see cref="DocumentSpec.DefaultTextStyle"/>,
    /// and a <see cref="TextStyleSpec"/> with a <see langword="null"/>
    /// <see cref="TextStyleSpec.Font"/> each reached <see cref="NullReferenceException"/>
    /// the same way <see cref="TextRunSpec.Style"/> once did. All three are
    /// now validated at their own construction, the same way; the contract
    /// above is true again as of this fix.
    /// </para>
    /// </remarks>
    public static byte[] Render(DocumentSpec spec)
    {
        spec.ValidateEmbeddedFontReferences();

        using var document = new Document
        {
            PageSize = new VellumPdf.Document.PdfRectangle(0, 0, spec.Page.WidthPoints, spec.Page.HeightPoints),
            Margins = spec.Margins,
            Conformance = spec.Conformance,
            Tagged = spec.Tagged,
            UseObjectStreams = spec.UseObjectStreams,
        };

        if (spec.Language is not null)
        {
            document.Language = spec.Language;
        }

        var embeddedFonts = spec.EmbeddedFonts.Select(bytes => LoadEmbeddedFont(document, bytes)).ToArray();
        var context = new RenderContext(embeddedFonts, new Dictionary<TextStyleSpec, TextStyle>());

        // Consulted only by the one Document.Add(string, TextStyle?) overload,
        // which AddContentItem calls for a PlainTextSpec left unstyled: see
        // the remark on DocumentSpec.DefaultTextStyle. HeadingSpec and
        // ParagraphSpec resolve their own fallback directly below and never
        // read this value; ListItemSpec and TableCellSpec, when unstyled,
        // stay null here and are resolved later by their own container. Only
        // called when the specification actually contains an unstyled
        // PlainTextSpec, the identical condition SpecCodeEmitter checks
        // before emitting the matching document.SetDefaultFont line, so the
        // two sides cannot disagree about whether the call happens even
        // though it is provably inert either way for every other document.
        if (SpecCodeEmitter.HasUnstyledPlainText(spec))
        {
            document.SetDefaultFont(ToTextStyle(spec.DefaultTextStyle, context));
        }

        if (spec.Metadata is { } metadata)
        {
            ApplyMetadata(document.Info, metadata);
        }

        foreach (var item in spec.Content)
        {
            AddContentItem(document, item, context);
        }

        if (spec.Header is { } header)
        {
            document.Header = BuildRunningBand(header, context);
        }

        if (spec.Footer is { } footer)
        {
            document.Footer = BuildRunningBand(footer, context);
        }

        switch (spec.OutputIntent)
        {
            case PdfAOutputIntentSpec pdfA:
                try
                {
                    document.SetPdfAOutputIntent(pdfA.IccProfile, pdfA.ComponentCount, pdfA.OutputConditionIdentifier, pdfA.Info);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Could not embed the ICC output intent: {ex.Message}", ex);
                }

                break;
            case CmykOutputIntentSpec cmyk:
                document.UseCmykOutputIntent(cmyk.OutputConditionIdentifier);
                break;
            case null:
                break;
        }

        if (spec.Encryption is { } encryption)
        {
            try
            {
                document.Encrypt(new PdfEncryptionSettings
                {
                    UserPassword = encryption.UserPassword,
                    OwnerPassword = encryption.OwnerPassword,
                    Permissions = encryption.Permissions,
                    EncryptMetadata = encryption.EncryptMetadata,
                });
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException($"Could not encrypt the document: {ex.Message}", ex);
            }
        }

        try
        {
            using var stream = new MemoryStream();
            document.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not save the document: {ex.Message}", ex);
        }
    }

    private static void ApplyMetadata(VellumPdf.Document.PdfDocumentInfo info, DocumentMetadataSpec metadata)
    {
        if (metadata.Title is not null)
        {
            info.Title = metadata.Title;
        }

        if (metadata.Author is not null)
        {
            info.Author = metadata.Author;
        }

        if (metadata.Subject is not null)
        {
            info.Subject = metadata.Subject;
        }

        if (metadata.Keywords is not null)
        {
            info.Keywords = metadata.Keywords;
        }

        if (metadata.Creator is not null)
        {
            info.Creator = metadata.Creator;
        }

        if (metadata.Producer is not null)
        {
            info.Producer = metadata.Producer;
        }
    }

    /// <summary>
    /// No default arm: <see cref="DocumentSpec.Content"/>'s own
    /// construction-time validation already rejects any <see cref="ContentItemSpec"/>
    /// subtype other than the eight named below, and a null item, so this
    /// switch STATEMENT (unlike a switch expression) compiles without one and
    /// has no unreachable branch for the coverage gate to find. Before that
    /// validation existed, an unrecognised subtype silently fell through this
    /// switch as though it were absent, while <see cref="Generation.SpecCodeEmitter.EmitContentItem"/>
    /// threw for the identical input: exactly the divergence CLAUDE.md's
    /// round-trip invariant forbids. See the remark on <c>ContentItemSpec</c>.
    /// </summary>
    private static void AddContentItem(Document document, ContentItemSpec item, RenderContext context)
    {
        switch (item)
        {
            case PlainTextSpec plainText:
                document.Add(plainText.Text, ToTextStyleOrNull(plainText.Style, context));
                break;
            case HeadingSpec heading:
                document.Add(BuildHeading(heading, context));
                break;
            case ParagraphSpec paragraph:
                document.Add(BuildParagraph(paragraph, context));
                break;
            case ListSpec list:
                document.Add(BuildList(list, context));
                break;
            case TableSpec table:
                document.Add(BuildTable(table, context));
                break;
            case ImageSpec image:
                document.Add(BuildImage(image));
                break;
            case PieChartSpec pieChart:
                document.Add(BuildPieChart(pieChart));
                break;
            case LineSeparatorSpec lineSeparator:
                document.Add(BuildLineSeparator(lineSeparator));
                break;
        }
    }

    private static Heading BuildHeading(HeadingSpec spec, RenderContext context) =>
        new(spec.Text, ToTextStyleOrNull(spec.Style, context))
        {
            Level = spec.Level,
            Alignment = spec.Alignment,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            BookmarkTitle = spec.BookmarkTitle,
            Language = spec.Language,
        };

    private static Paragraph BuildParagraph(ParagraphSpec spec, RenderContext context)
    {
        var runs = spec.Runs.Select(run => new TextRun(run.Text, ToTextStyle(run.Style, context)));
        return new Paragraph(runs)
        {
            Alignment = spec.Alignment,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            Language = spec.Language,
        };
    }

    private static ListElement BuildList(ListSpec spec, RenderContext context)
    {
        var list = new ListElement(spec.Style)
        {
            Indent = spec.Indent ?? 20,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            DefaultStyle = ToTextStyleOrNull(spec.DefaultStyle, context),
        };

        foreach (var item in spec.Items)
        {
            list.Add(BuildListItem(item, context));
        }

        return list;
    }

    private static ListItem BuildListItem(ListItemSpec spec, RenderContext context)
    {
        var item = new ListItem(spec.Text, ToTextStyleOrNull(spec.Style, context))
        {
            Language = spec.Language,
        };

        foreach (var child in spec.Children)
        {
            item.AddChild(BuildListItem(child, context));
        }

        return item;
    }

    private static TableElement BuildTable(TableSpec spec, RenderContext context)
    {
        var table = new TableElement
        {
            DefaultCellStyle = ToTextStyleOrNull(spec.DefaultCellStyle, context),
            BorderWidth = spec.BorderWidth ?? 0.5,
            BorderColor = spec.BorderColor ?? ColorRgb.Black,
            Margins = spec.Margins ?? EdgeInsets.Zero,
        };

        if (spec.ColumnWidths is { Count: > 0 } widths)
        {
            table.SetColumnWidths([.. widths]);
        }

        foreach (var rowSpec in spec.Rows)
        {
            var row = rowSpec.IsHeader ? table.AddHeaderRow() : table.AddRow();

            foreach (var cellSpec in rowSpec.Cells)
            {
                row.AddCell(BuildCell(cellSpec, context));
            }
        }

        return table;
    }

    private static Cell BuildCell(TableCellSpec spec, RenderContext context) =>
        new(spec.Content)
        {
            ColSpan = spec.ColSpan,
            RowSpan = spec.RowSpan,
            Style = ToTextStyleOrNull(spec.Style, context),
            Padding = spec.Padding ?? new EdgeInsets(4, 6, 4, 6),
            Background = spec.Background,
            Alignment = spec.Alignment,
            Language = spec.Language,
        };

    /// <summary>
    /// The Kernel image loader for each <see cref="ImageFormat"/> this model
    /// recognises. A lookup table, not a <c>switch</c>, for the identical
    /// coverage reason documented on <see cref="Generation.SpecCodeEmitter.ImageLoaderName"/>:
    /// a <c>switch</c> over this public enumeration would need a <c>default</c>
    /// arm the compiler requires but the public API makes unreachable (see
    /// <see cref="BuildImage"/>'s own remark), and that arm's branch outcome
    /// is attributed to the ENCLOSING method regardless of where any thrown
    /// exception is constructed, so extracting only the throw does not, on
    /// its own, bring the branch-coverage gate to 100% the way eliminating
    /// the branch entirely does.
    /// </summary>
    private static readonly IReadOnlyDictionary<ImageFormat, Func<byte[], PdfImageXObject>> ImageLoaders =
        new Dictionary<ImageFormat, Func<byte[], PdfImageXObject>>
        {
            [ImageFormat.Png] = PngImageLoader.Load,
            [ImageFormat.Jpeg] = JpegImageLoader.Load,
            [ImageFormat.Bmp] = BmpImageLoader.Load,
            [ImageFormat.Gif] = GifImageLoader.Load,
            [ImageFormat.Tiff] = TiffImageLoader.Load,
        };

    /// <summary>
    /// Wraps the Kernel image loader for <see cref="ImageSpec.Format"/> in a
    /// try/catch, per plan section 5.4 control 5: <see cref="DocumentSpec.Content"/>
    /// already rejects bytes whose magic signature contradicts the declared
    /// format, but a well-signed file can still be malformed further in, and
    /// a raw exception from the least-exercised code in the dependency chain
    /// is not a legible message. <see cref="ImageLoaders"/> not containing
    /// <see cref="ImageSpec.Format"/> is unreachable from the public API:
    /// <see cref="DocumentSpec.Content"/> rejects any <see cref="ImageSpec"/>
    /// whose declared <see cref="ImageSpec.Format"/> does not match its own
    /// byte signature, and <see cref="ImageSignature.Matches"/>, which
    /// performs that check, itself throws on any <see cref="ImageFormat"/>
    /// value outside the five named members before a <see cref="DocumentSpec"/>
    /// carrying one can ever be constructed.
    /// </summary>
    private static LayoutImage BuildImage(ImageSpec spec)
    {
        try
        {
            var xObject = ImageLoaders[spec.Format](spec.Bytes);

            return new LayoutImage(xObject)
            {
                Width = spec.Width,
                Height = spec.Height,
                Alignment = spec.Alignment,
                Margins = spec.Margins ?? EdgeInsets.Zero,
                AltText = spec.AltText,
            };
        }
        catch (Exception ex)
        {
            // No `when` guard excluding a specific exception type here any
            // more: cycle 6 excluded ArgumentOutOfRangeException so the
            // switch's own now-removed unreachable-format throw could
            // propagate unwrapped instead of being reported as a decode
            // failure. ImageLoaders[spec.Format] no longer throws that type
            // for an unreachable format (it throws KeyNotFoundException,
            // itself unreachable for the same reason); if a loader ever
            // throws ArgumentOutOfRangeException for a genuinely malformed
            // but well-signed file, wrapping it as a decode failure, exactly
            // like any other exception a loader raises, is the correct
            // behaviour.
            throw new InvalidOperationException($"Could not decode the embedded {spec.Format} image: {ex.Message}", ex);
        }
    }

    /// <summary>Wraps <c>Document.UseTrueTypeFont</c>, per plan section 5.4 control 5, for the same reason as <see cref="BuildImage"/>.</summary>
    private static EmbeddedFontHandle LoadEmbeddedFont(Document document, byte[] bytes)
    {
        try
        {
            return document.UseTrueTypeFont(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not parse an embedded font: {ex.Message}", ex);
        }
    }

    private static PieChart BuildPieChart(PieChartSpec spec) => new()
    {
        Slices = spec.Slices,
        Diameter = spec.Diameter,
        Margins = spec.Margins ?? new EdgeInsets(6),
        StrokeColor = spec.StrokeColor,
        StrokeWidth = spec.StrokeWidth,
        Alignment = spec.Alignment,
        StartAngle = spec.StartAngle,
        Clockwise = spec.Clockwise,
        AltText = spec.AltText,
        Decorative = spec.Decorative,
    };

    private static LineSeparator BuildLineSeparator(LineSeparatorSpec spec) => new()
    {
        LineWidth = spec.LineWidth,
        Color = spec.Color,
        Margins = spec.Margins ?? new EdgeInsets(6, 0, 6, 0),
    };

    private static RunningBand BuildRunningBand(RunningBandSpec spec, RenderContext context)
    {
        var style = ToTextStyle(spec.Style, context);
        return spec.Height is { } height
            ? new RunningBand(spec.Template, style, spec.Alignment) { Height = height }
            : new RunningBand(spec.Template, style, spec.Alignment);
    }

    /// <summary>
    /// Builds the <see cref="TextStyle"/> for <paramref name="spec"/>, or
    /// returns the one already built for a value-equal <see cref="TextStyleSpec"/>
    /// earlier in the same <see cref="Render"/> call. <see cref="RenderContext.StyleCache"/>
    /// keys on <see cref="TextStyleSpec"/>'s own record value equality, so two
    /// runs sharing a value-equal style (whether or not they share the same
    /// instance) are always given the same <see cref="TextStyle"/> instance.
    /// This matters beyond simply avoiding redundant allocation: the library
    /// merges adjacent <c>TextRun</c>s whose <c>Style</c> is reference-identical,
    /// and <see cref="Generation.SpecCodeEmitter"/> hoists a style used more
    /// than once into one shared local by the same value-equality rule, so
    /// this cache is what keeps the two sides merging runs identically.
    /// </summary>
    private static TextStyle ToTextStyle(TextStyleSpec spec, RenderContext context)
    {
        if (context.StyleCache.TryGetValue(spec, out var cached))
        {
            return cached;
        }

        // A two-way comparison against FontKind.Embedded, not a switch over
        // both named members, for the identical coverage reason documented
        // on SpecRenderer.ImageLoaders and SpecCodeEmitter.ImageLoaderName: a
        // switch over this public enumeration would need a default arm the
        // compiler requires but FontSpec.Kind's own construction-time
        // validation (see its remark) makes unreachable, and that arm's
        // branch outcome would be attributed to this method regardless of
        // where any thrown exception is constructed. FontKind has exactly
        // two members; anything other than Embedded is Standard14, for any
        // FontSpec that exists. NOTE: an earlier version of this comment
        // claimed FontSpec only ever constructs a Kind through
        // FromStandard14 or FromEmbedded; that was false (a caller may
        // always write `new FontSpec { Kind = (FontKind)99 }` directly, and
        // this repository's own test suite once did), which is why the
        // guarantee here now rests on FontSpec.Kind's own validation, not on
        // the shape of its two factory methods.
        var fontRef = spec.Font.Kind == FontKind.Embedded
            ? new FontReference(context.Fonts[spec.Font.EmbeddedFontIndex])
            : new FontReference(spec.Font.Standard14Face);

        // NOTE: an unset spec.Leading becomes a literal 0 here, matching
        // TextStyle's own default and what SpecCodeEmitter emits when it omits
        // the Leading property entirely, so the two sides agree. See the
        // remark on SpecLimits.MaxLeadingPoints: the library treats 0 as a
        // request to compute its own line height from the font, which is NOT
        // bounded by SpecLimits.MaxLeadingPoints and can exceed it.
        var style = new TextStyle
        {
            FontRef = fontRef,
            FontSize = spec.FontSize,
            Leading = spec.Leading ?? 0,
            Color = spec.Color,
            LinkUri = spec.LinkUri,
        };

        context.StyleCache.Add(spec, style);
        return style;
    }

    private static TextStyle? ToTextStyleOrNull(TextStyleSpec? spec, RenderContext context) =>
        spec is null ? null : ToTextStyle(spec, context);

    /// <summary>
    /// The state threaded through one <see cref="Render"/> call: the embedded
    /// font handles already registered with <c>document</c>, in
    /// <see cref="DocumentSpec.EmbeddedFonts"/> order, and the cache
    /// <see cref="ToTextStyle"/> uses to give every value-equal
    /// <see cref="TextStyleSpec"/> the same <see cref="TextStyle"/> instance.
    /// </summary>
    private sealed record RenderContext(IReadOnlyList<EmbeddedFontHandle> Fonts, Dictionary<TextStyleSpec, TextStyle> StyleCache);
}
