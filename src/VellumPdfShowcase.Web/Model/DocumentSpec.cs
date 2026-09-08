using VellumPdf.Encryption;
using VellumPdf.Fonts;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using DocumentConformance = VellumPdf.Document.PdfConformance;

namespace VellumPdfShowcase.Web.Model;

/// <summary>
/// An immutable description of a document the visitor has selected in the
/// showcase. <see cref="Generation.SpecRenderer"/> turns a <see cref="DocumentSpec"/>
/// into PDF bytes by calling <c>VellumPdf.Layout</c> directly, and
/// <see cref="Generation.SpecCodeEmitter"/> turns the same instance into the C#
/// a developer would write to obtain the identical document. Neither type may
/// diverge from the other, because both read only this record.
/// </summary>
/// <remarks>
/// Scope is bounded deliberately: every member here corresponds to a capability
/// recorded as available in the library's 2.3.0 capability table. Nothing that
/// table lists as absent (explicit page breaks, nested tables, inline runs in
/// headings or list items, multi-string headers and footers, shapes beyond a
/// line separator, and so on) has a representation.
/// </remarks>
/// <remarks>
/// A collection the library requires to be non-empty rejects an empty value at
/// construction, with a message naming the actual problem, rather than letting
/// the caller build the spec and hear about it later from deep inside the
/// library: see <see cref="Content"/>, <see cref="PieChartSpec.Slices"/>,
/// <see cref="TableSpec.Rows"/> and <see cref="TableRowSpec.Cells"/>.
/// </remarks>
public sealed record DocumentSpec
{
    /// <summary>The page size, in PDF points.</summary>
    public required PageSizeSpec Page { get; init; }

    /// <summary>
    /// The page margins, applied on all four edges of every page. Defaults to
    /// 72 points (one inch) on every edge, matching <c>Document</c>'s own
    /// default exactly, for the same reason given on <see cref="PieChartSpec.StartAngle"/>.
    /// </summary>
    public EdgeInsets Margins { get; init; } = new(72);

    /// <summary>
    /// The style registered as the document's default through
    /// <c>Document.SetDefaultFont</c>. Per section 3.4.0 of the plan, that
    /// member is consulted only by the <c>Document.Add(string, TextStyle?)</c>
    /// overload, which a <see cref="PlainTextSpec"/> with no explicit
    /// <see cref="PlainTextSpec.Style"/> maps to.
    /// </summary>
    /// <remarks>
    /// NOTE: content items do not all behave alike here. <see cref="HeadingSpec"/>
    /// and <see cref="ParagraphSpec"/> resolve their own fallback style at
    /// construction and never read this value. <see cref="ListItemSpec"/> and
    /// <see cref="TableCellSpec"/> stay <see langword="null"/> when left
    /// unstyled and are resolved later by their container
    /// (<see cref="ListSpec.DefaultStyle"/> or <see cref="TableSpec.DefaultCellStyle"/>),
    /// which may itself be <see langword="null"/>; this property governs
    /// neither. Only an unstyled <see cref="PlainTextSpec"/> reads it.
    /// </remarks>
    public required TextStyleSpec DefaultTextStyle { get; init; }

    /// <summary>
    /// Raw TrueType font bytes for every embedded face the document uses. A
    /// <see cref="FontSpec"/> of kind <see cref="FontKind.Embedded"/> refers to
    /// one of these by position. Each entry is registered with the underlying
    /// <c>Document</c> exactly once, regardless of how many styles reference it.
    /// </summary>
    /// <remarks>
    /// Per plan section 5.4, every entry is capped at <see cref="SpecLimits.MaxAssetBytes"/>
    /// and the whole list is snapshotted with a collection expression at
    /// construction, so mutating a list passed in cannot change this value afterward.
    /// </remarks>
    public IReadOnlyList<byte[]> EmbeddedFonts
    {
        get;
        init => field = ValidateEmbeddedFonts(value);
    } = [];

    /// <summary>The document's content, laid out in the order given. A document must have at least one item.</summary>
    /// <remarks>
    /// Per plan section 5.4, the list is capped at <see cref="SpecLimits.MaxContentItems"/>
    /// and snapshotted with a collection expression at construction, so an
    /// aliased, later-mutated <c>List&lt;ContentItemSpec&gt;</c> cannot empty
    /// this property out from under a fully constructed <see cref="DocumentSpec"/>.
    /// Every <see cref="ImageSpec"/> in the list also has its declared
    /// <see cref="ImageSpec.Format"/> checked against the magic bytes of its
    /// own <see cref="ImageSpec.Bytes"/>, which requires both properties to be
    /// already set and so cannot be done inside <see cref="ImageSpec"/> itself.
    /// </remarks>
    public required IReadOnlyList<ContentItemSpec> Content
    {
        get;
        init => field = ValidateContent(value);
    }

    /// <summary>The running header repeated on every page, if any.</summary>
    public RunningBandSpec? Header { get; init; }

    /// <summary>The running footer repeated on every page, if any.</summary>
    public RunningBandSpec? Footer { get; init; }

    /// <summary>The conformance profile the document claims, or <see cref="DocumentConformance.None"/>.</summary>
    public DocumentConformance Conformance { get; init; } = DocumentConformance.None;

    /// <summary>Whether the document carries a tagged structure tree.</summary>
    public bool Tagged { get; init; }

    /// <summary>The document's natural language, as a BCP 47 tag such as <c>"en"</c>.</summary>
    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }

    /// <summary>Document information dictionary entries, if any are set.</summary>
    public DocumentMetadataSpec? Metadata { get; init; }

    /// <summary>The output intent to embed, if any.</summary>
    /// <remarks>
    /// Per plan section 5.4, a <see cref="PdfAOutputIntentSpec"/> has its
    /// <see cref="PdfAOutputIntentSpec.IccProfile"/> header validated here
    /// against its own <see cref="PdfAOutputIntentSpec.ComponentCount"/>,
    /// which requires both properties to already be set and so cannot be done
    /// inside <see cref="PdfAOutputIntentSpec"/> itself. Left unvalidated, a
    /// three-byte junk profile would embed silently into a document claiming
    /// PDF/A or PDF/UA conformance.
    /// </remarks>
    public OutputIntentSpec? OutputIntent
    {
        get;
        init => field = value switch
        {
            PdfAOutputIntentSpec pdfA => Validated(pdfA),
            _ => value,
        };
    }

    /// <summary>
    /// Encryption settings, if the document is to be encrypted.
    /// </summary>
    /// <remarks>
    /// Per plan section 5.4 (C4-C-M5), a restricted <see cref="EncryptionSpec.Permissions"/>
    /// set requires a non-empty <see cref="EncryptionSpec.OwnerPassword"/>. The
    /// library authenticates full owner access to whichever password actually
    /// opens the document; with <see cref="EncryptionSpec.OwnerPassword"/> left
    /// unset, that password is <see cref="EncryptionSpec.UserPassword"/>, so a
    /// restricted permission set would bind nobody who can open the file, and
    /// the PDF is the one artefact that leaves the machine. This check
    /// requires both properties to already be set and so cannot be done
    /// inside <see cref="EncryptionSpec"/> itself.
    /// </remarks>
    public EncryptionSpec? Encryption
    {
        get;
        init => field = value switch
        {
            { Permissions: var permissions, OwnerPassword: null or "" } when permissions != PdfPermissions.All =>
                throw new ArgumentException(
                    "EncryptionSpec.OwnerPassword must be set whenever Permissions restricts any permission; " +
                    "otherwise the displayed permission set binds nobody who can open the file.",
                    nameof(Encryption)),
            _ => value,
        };
    }

    private static IReadOnlyList<byte[]> ValidateEmbeddedFonts(IReadOnlyList<byte[]> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(EmbeddedFonts));
        foreach (var font in value)
        {
            SpecLimits.ValidateAssetBytes(font, nameof(EmbeddedFonts));
        }

        return [.. value];
    }

    private static IReadOnlyList<ContentItemSpec> ValidateContent(IReadOnlyList<ContentItemSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Content));

        if (value.Count == 0)
        {
            throw new ArgumentException("A document must have at least one item of content.", nameof(Content));
        }

        if (value.Count > SpecLimits.MaxContentItems)
        {
            throw new ArgumentException(
                $"A document must not have more than {SpecLimits.MaxContentItems} items of content; got {value.Count}.",
                nameof(Content));
        }

        var snapshot = (IReadOnlyList<ContentItemSpec>)[.. value];

        foreach (var item in snapshot)
        {
            if (item is ImageSpec image && !ImageSignature.Matches(image.Format, image.Bytes))
            {
                throw new ArgumentException(
                    $"ImageSpec.Bytes does not match the declared ImageFormat.{image.Format}; the file's own signature says otherwise.",
                    nameof(Content));
            }
        }

        return snapshot;
    }

    private static PdfAOutputIntentSpec Validated(PdfAOutputIntentSpec pdfA)
    {
        IccProfileHeader.Validate(pdfA.IccProfile, pdfA.ComponentCount);
        return pdfA;
    }
}

/// <summary>A page size expressed directly in PDF points.</summary>
public sealed record PageSizeSpec(double WidthPoints, double HeightPoints)
{
    /// <summary>Builds a <see cref="PageSizeSpec"/> from a Kernel <c>PdfRectangle</c>, such as one of the <c>PageSize</c> presets.</summary>
    public static PageSizeSpec FromRectangle(VellumPdf.Document.PdfRectangle rectangle) =>
        new(rectangle.Width, rectangle.Height);
}

/// <summary>Which of the two font sources a <see cref="FontSpec"/> selects.</summary>
public enum FontKind
{
    /// <summary>One of the fourteen built-in faces. No embedding, no font file.</summary>
    Standard14,

    /// <summary>An embedded TrueType face, referenced by index into <see cref="DocumentSpec.EmbeddedFonts"/>.</summary>
    Embedded,
}

/// <summary>
/// A reference to a font, either a <see cref="Standard14"/> face or an embedded
/// TrueType face by position in <see cref="DocumentSpec.EmbeddedFonts"/>. Kept
/// separate from the library's own <c>FontReference</c>, which wraps an
/// <c>EmbeddedFontHandle</c> tied to one already-constructed <c>Document</c> and
/// so cannot be prepared ahead of rendering.
/// </summary>
public sealed record FontSpec
{
    public required FontKind Kind { get; init; }
    public Standard14 Standard14Face { get; init; }
    public int EmbeddedFontIndex { get; init; }

    public static FontSpec FromStandard14(Standard14 face) =>
        new() { Kind = FontKind.Standard14, Standard14Face = face };

    public static FontSpec FromEmbedded(int embeddedFontIndex) =>
        new() { Kind = FontKind.Embedded, EmbeddedFontIndex = embeddedFontIndex };
}

/// <summary>Mirrors the settable members of the library's <c>TextStyle</c>.</summary>
/// <remarks>
/// Per plan section 3.4.0.1, every member of this record must implement value
/// equality. <see cref="Generation.SpecRenderer"/>'s style cache and
/// <see cref="Generation.SpecCodeEmitter"/>'s style hoisting both key on this
/// record's own equality, and they agree about when a style is shared only
/// because both keys behave identically. C# record equality falls back to
/// reference equality for any member whose type does not implement value
/// equality, so adding a member of such a type here (an array or list, for
/// instance) would silently make two value-equal styles compare unequal and
/// reintroduce the divergence between the renderer and the emitter that took
/// two review cycles to find and fix. If a future member cannot implement
/// value equality, give this record an explicit <c>Equals</c> and
/// <c>GetHashCode</c> covering it.
/// </remarks>
public sealed record TextStyleSpec
{
    public required FontSpec Font { get; init; }

    /// <summary>Defaults to 12, matching <c>TextStyle</c>'s own default exactly, for the same reason given on <see cref="PieChartSpec.StartAngle"/>.</summary>
    public double FontSize { get; init; } = 12;

    public double? Leading { get; init; }
    public ColorRgb Color { get; init; } = ColorRgb.Black;

    /// <summary>
    /// A URI a run of this style links to, or <see langword="null"/> for none.
    /// Restricted to the <c>http</c> and <c>https</c> schemes: this value ends
    /// up in a downloadable PDF's <c>/URI</c> action, and a <c>javascript:</c>
    /// or <c>data:</c> scheme is not a hyperlink there.
    /// </summary>
    public string? LinkUri
    {
        get;
        init => field = value is null || HasAllowedScheme(value)
            ? SpecLimits.ValidateOptionalString(value, SpecLimits.MaxUriLength, nameof(LinkUri))
            : throw new ArgumentException(
                $"LinkUri must use the http or https scheme; got {value}.",
                nameof(LinkUri));
    }

    private static bool HasAllowedScheme(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}

/// <summary>One inline run of a <see cref="ParagraphSpec"/>, matching the library's <c>TextRun</c>.</summary>
public sealed record TextRunSpec(string Text, TextStyleSpec Style);

/// <summary>The base type for one item of ordered document content.</summary>
public abstract record ContentItemSpec;

/// <summary>A <c>Heading</c>. A <see langword="null"/> <see cref="Style"/> lets the library apply automatic styling for the level.</summary>
public sealed record HeadingSpec : ContentItemSpec
{
    public required string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    }

    public required int Level { get; init; }
    public TextStyleSpec? Style { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public EdgeInsets? Margins { get; init; }

    public string? BookmarkTitle
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(BookmarkTitle));
    }

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }
}

/// <summary>
/// A <c>Paragraph</c>. A single-entry <see cref="Runs"/> list is the common
/// case of one style throughout; more entries produce the mixed-style inline
/// runs the library's <c>Paragraph(IEnumerable&lt;TextRun&gt;)</c> constructor accepts.
/// </summary>
public sealed record ParagraphSpec : ContentItemSpec
{
    /// <summary>
    /// Per plan section 5.4, the list is snapshotted with a collection
    /// expression at construction and each run's <see cref="TextRunSpec.Text"/>
    /// is capped at <see cref="SpecLimits.MaxTextLength"/>.
    /// </summary>
    public required IReadOnlyList<TextRunSpec> Runs
    {
        get;
        init => field = ValidateRuns(value);
    }

    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public EdgeInsets? Margins { get; init; }

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }

    public static ParagraphSpec FromText(string text, TextStyleSpec style) =>
        new() { Runs = [new TextRunSpec(text, style)] };

    private static IReadOnlyList<TextRunSpec> ValidateRuns(IReadOnlyList<TextRunSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Runs));
        foreach (var run in value)
        {
            SpecLimits.ValidateString(run.Text, SpecLimits.MaxTextLength, nameof(Runs));
        }

        return [.. value];
    }
}

/// <summary>
/// A plain string added through the library's <c>Document.Add(string, TextStyle?)</c>
/// overload, the one member of <c>Document</c> listed in plan section 3.1 that
/// this model could not previously express. When <see cref="Style"/> is
/// <see langword="null"/>, the rendered text uses whatever style
/// <see cref="DocumentSpec.DefaultTextStyle"/> registered through
/// <c>Document.SetDefaultFont</c>. <see cref="HeadingSpec"/> and
/// <see cref="ParagraphSpec"/> resolve their own fallback directly instead and
/// never consult that value; see the remark on <see cref="DocumentSpec.DefaultTextStyle"/>
/// for the two content items that are neither.
/// </summary>
public sealed record PlainTextSpec : ContentItemSpec
{
    public required string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    }

    public TextStyleSpec? Style { get; init; }
}

/// <summary>A <c>ListElement</c>, unordered or one of the three ordered forms.</summary>
public sealed record ListSpec : ContentItemSpec
{
    public required ListStyle Style { get; init; }

    /// <summary>Snapshotted with a collection expression at construction, per plan section 5.4.</summary>
    public required IReadOnlyList<ListItemSpec> Items
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Items));
            field = [.. value];
        }
    }

    public double? Indent { get; init; }
    public EdgeInsets? Margins { get; init; }
    public TextStyleSpec? DefaultStyle { get; init; }
}

/// <summary>One <c>ListItem</c>. Nesting is expressed through <see cref="Children"/>, matching <c>ListItem.AddChild</c>.</summary>
public sealed record ListItemSpec
{
    public required string Text
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Text));
    }

    public TextStyleSpec? Style { get; init; }

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }

    /// <summary>
    /// Per plan section 5.4 (C4-C-M2), snapshotted with a collection
    /// expression at construction, and the nesting depth reachable through
    /// this item is capped at <see cref="SpecLimits.MaxListNestingDepth"/>.
    /// Unbounded nesting recurses without a bound in both the renderer and
    /// the emitter and overflows the CLR stack; a stack overflow cannot be
    /// caught, so this is the one control in section 5.4 that a wrapped
    /// parser call cannot rescue.
    /// </summary>
    public IReadOnlyList<ListItemSpec> Children
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Children));
            var snapshot = value.Count == 0 ? (IReadOnlyList<ListItemSpec>)[] : [.. value];
            var depth = snapshot.Count == 0 ? 1 : 1 + snapshot.Max(child => child.Depth);

            if (depth > SpecLimits.MaxListNestingDepth)
            {
                throw new ArgumentException(
                    $"List nesting must not exceed {SpecLimits.MaxListNestingDepth} levels; this item would reach depth {depth}.",
                    nameof(Children));
            }

            field = snapshot;
            Depth = depth;
        }
    } = [];

    /// <summary>The greatest nesting depth reachable from this item, counting itself as depth 1. Not part of the library's own shape; used only to enforce <see cref="SpecLimits.MaxListNestingDepth"/> as each level is constructed.</summary>
    private int Depth { get; set; } = 1;
}

/// <summary>A <c>TableElement</c>.</summary>
public sealed record TableSpec : ContentItemSpec
{
    /// <summary>
    /// Per plan section 5.4, capped at <see cref="SpecLimits.MaxTableRows"/>
    /// and snapshotted with a collection expression at construction.
    /// </summary>
    public required IReadOnlyList<TableRowSpec> Rows
    {
        get;
        init => field = ValidateRows(value);
    }

    public IReadOnlyList<double>? ColumnWidths
    {
        get;
        init => field = value is null ? null : [.. value];
    }

    public TextStyleSpec? DefaultCellStyle { get; init; }
    public double? BorderWidth { get; init; }
    public ColorRgb? BorderColor { get; init; }
    public EdgeInsets? Margins { get; init; }

    private static IReadOnlyList<TableRowSpec> ValidateRows(IReadOnlyList<TableRowSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Rows));

        if (value.Count == 0)
        {
            throw new ArgumentException("A table must have at least one row.", nameof(Rows));
        }

        if (value.Count > SpecLimits.MaxTableRows)
        {
            throw new ArgumentException($"A table must not have more than {SpecLimits.MaxTableRows} rows; got {value.Count}.", nameof(Rows));
        }

        return [.. value];
    }
}

/// <summary>
/// One <c>Row</c> of a <see cref="TableSpec"/>. The library declares
/// <c>Row.Background</c> as an ordinary settable property with a public
/// parameterless constructor, so <c>new Row { Background = ..., IsHeader = true }</c>
/// compiles on its own. The obstacle is containment, not initialisation:
/// a <c>Row</c> reaches a <c>TableElement</c> only through
/// <c>TableElement.AddRow</c> or <c>AddHeaderRow</c>, both of which construct
/// and return their own instance, and <c>TableElement.Rows</c> is a get-only
/// list with no <c>AddRow(Row)</c> overload to attach a caller-built one. No
/// external caller can therefore get a <c>Row</c> it built itself into a
/// table. Per-row background is thus not modelled here; per-cell background,
/// set on a <see cref="TableCellSpec"/> the caller constructs directly, is
/// unaffected.
/// </summary>
public sealed record TableRowSpec
{
    /// <summary>
    /// Per plan section 5.4, capped at <see cref="SpecLimits.MaxTableCellsPerRow"/>
    /// and snapshotted with a collection expression at construction.
    /// </summary>
    public required IReadOnlyList<TableCellSpec> Cells
    {
        get;
        init => field = ValidateCells(value);
    }

    public bool IsHeader { get; init; }

    private static IReadOnlyList<TableCellSpec> ValidateCells(IReadOnlyList<TableCellSpec> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Cells));

        if (value.Count == 0)
        {
            throw new ArgumentException("A table row must have at least one cell.", nameof(Cells));
        }

        if (value.Count > SpecLimits.MaxTableCellsPerRow)
        {
            throw new ArgumentException(
                $"A table row must not have more than {SpecLimits.MaxTableCellsPerRow} cells; got {value.Count}.",
                nameof(Cells));
        }

        return [.. value];
    }
}

/// <summary>One <c>Cell</c> of a <see cref="TableRowSpec"/>. Content is plain text; the library does not support nested elements in a cell.</summary>
public sealed record TableCellSpec
{
    public required string Content
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Content));
    }

    public int ColSpan { get; init; } = 1;
    public int RowSpan { get; init; } = 1;
    public TextStyleSpec? Style { get; init; }
    public EdgeInsets? Padding { get; init; }
    public ColorRgb? Background { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;

    public string? Language
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxLanguageTagLength, nameof(Language));
    }
}

/// <summary>The five raster formats the Kernel image loaders accept.</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Bmp,
    Gif,
    Tiff,
}

/// <summary>A <c>LayoutImage</c>, decoded through the matching Kernel loader for <see cref="Format"/>.</summary>
/// <remarks>
/// Per plan section 5.4, <see cref="Bytes"/> is capped at
/// <see cref="SpecLimits.MaxAssetBytes"/> here; whether it actually matches
/// <see cref="Format"/>'s magic bytes is checked by <see cref="DocumentSpec.Content"/>,
/// which is the only property that ever sees both this record's properties
/// fully set.
/// </remarks>
public sealed record ImageSpec : ContentItemSpec
{
    public required ImageFormat Format { get; init; }

    public required byte[] Bytes
    {
        get;
        init => field = SpecLimits.ValidateAssetBytes(value, nameof(Bytes));
    }

    public double? Width { get; init; }
    public double? Height { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Left;
    public EdgeInsets? Margins { get; init; }

    public string? AltText
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(AltText));
    }
}

/// <summary>A <c>PieChart</c>, using the library's own <c>PieSlice</c> value for each slice.</summary>
public sealed record PieChartSpec : ContentItemSpec
{
    /// <summary>
    /// Per plan section 5.4, capped at <see cref="SpecLimits.MaxChartSlices"/>
    /// and snapshotted with a collection expression at construction.
    /// </summary>
    public required IReadOnlyList<PieSlice> Slices
    {
        get;
        init => field = ValidateSlices(value);
    }

    public required double Diameter { get; init; }
    public EdgeInsets? Margins { get; init; }
    public ColorRgb? StrokeColor { get; init; }
    public double StrokeWidth { get; init; } = 0.5;
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Center;

    /// <summary>
    /// Defaults to <c>π/2</c> (12 o'clock), matching <c>PieChart</c>'s own
    /// default exactly. Every default in this record matches the library's so
    /// that <see cref="Generation.SpecCodeEmitter"/> can safely omit an
    /// unset property from the code it emits, relying on the library to apply
    /// the identical default that <see cref="Generation.SpecRenderer"/> set explicitly.
    /// </summary>
    public double StartAngle { get; init; } = double.Pi / 2;

    public bool Clockwise { get; init; } = true;

    public string? AltText
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(AltText));
    }

    public bool Decorative { get; init; }

    private static IReadOnlyList<PieSlice> ValidateSlices(IReadOnlyList<PieSlice> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(Slices));

        if (value.Count == 0)
        {
            throw new ArgumentException("A pie chart must have at least one slice.", nameof(Slices));
        }

        if (value.Count > SpecLimits.MaxChartSlices)
        {
            throw new ArgumentException($"A pie chart must not have more than {SpecLimits.MaxChartSlices} slices; got {value.Count}.", nameof(Slices));
        }

        return [.. value];
    }
}

/// <summary>A <c>LineSeparator</c>, the library's only vector primitive in the Layout API.</summary>
public sealed record LineSeparatorSpec : ContentItemSpec
{
    public double LineWidth { get; init; } = 1;
    public ColorRgb Color { get; init; } = ColorRgb.Black;
    public EdgeInsets? Margins { get; init; }
}

/// <summary>
/// A running header or footer. The template is one string substituting
/// <c>{page}</c> and <c>{pages}</c>; the library accepts nothing richer here.
/// </summary>
public sealed record RunningBandSpec
{
    public required string Template
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(Template));
    }

    public required TextStyleSpec Style { get; init; }
    public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Center;
    public double? Height { get; init; }
}

/// <summary>Entries for the document's <c>PdfDocumentInfo</c>.</summary>
public sealed record DocumentMetadataSpec
{
    public string? Title
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Title));
    }

    public string? Author
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Author));
    }

    public string? Subject
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Subject));
    }

    public string? Keywords
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Keywords));
    }

    public string? Creator
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Creator));
    }

    public string? Producer
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Producer));
    }
}

/// <summary>The base type for the two output intents <c>Document</c> can embed.</summary>
public abstract record OutputIntentSpec;

/// <summary>
/// An ICC-based output intent for a PDF/A or PDF/UA claim, matching
/// <c>Document.SetPdfAOutputIntent</c>.
/// </summary>
/// <remarks>
/// Per plan section 5.4, <see cref="IccProfile"/> is capped at
/// <see cref="SpecLimits.MaxAssetBytes"/> here; whether its header is
/// internally consistent with <see cref="ComponentCount"/> is checked by
/// <see cref="DocumentSpec.OutputIntent"/>, which is the only property that
/// ever sees both this record's properties fully set.
/// </remarks>
public sealed record PdfAOutputIntentSpec : OutputIntentSpec
{
    public required byte[] IccProfile
    {
        get;
        init => field = SpecLimits.ValidateAssetBytes(value, nameof(IccProfile));
    }

    public required int ComponentCount { get; init; }

    public required string OutputConditionIdentifier
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(OutputConditionIdentifier));
    }

    public string? Info
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(Info));
    }
}

/// <summary>
/// A device CMYK output intent, matching <c>Document.UseCmykOutputIntent</c>.
/// Measured directly against the library: the call writes nothing into the
/// saved bytes unless <see cref="DocumentSpec.Conformance"/> is set to
/// something other than <see cref="DocumentConformance.None"/>. A sample
/// using this type without a conformance claim would round-trip correctly
/// while exercising nothing.
/// </summary>
public sealed record CmykOutputIntentSpec : OutputIntentSpec
{
    public required string OutputConditionIdentifier
    {
        get;
        init => field = SpecLimits.ValidateString(value, SpecLimits.MaxTextLength, nameof(OutputConditionIdentifier));
    }
}

/// <summary>
/// Encryption settings, matching <c>PdfEncryptionSettings</c>.
/// </summary>
/// <remarks>
/// Per plan section 5.4 (C4-C-M5): whether <see cref="OwnerPassword"/> may be
/// left unset depends on <see cref="Permissions"/>, so that rule is enforced
/// by <see cref="DocumentSpec.Encryption"/>, the only property that ever sees
/// both fully set. Measured directly against the library: with
/// <see cref="OwnerPassword"/> unset, the password that actually authenticates
/// full (owner) access is <see cref="UserPassword"/>, so a restricted
/// <see cref="Permissions"/> value would otherwise bind nobody who can open
/// the file.
/// </remarks>
public sealed record EncryptionSpec
{
    public string? UserPassword
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(UserPassword));
    }

    public string? OwnerPassword
    {
        get;
        init => field = SpecLimits.ValidateOptionalString(value, SpecLimits.MaxTextLength, nameof(OwnerPassword));
    }

    public PdfPermissions Permissions { get; init; } = PdfPermissions.All;
    public bool EncryptMetadata { get; init; } = true;
}
