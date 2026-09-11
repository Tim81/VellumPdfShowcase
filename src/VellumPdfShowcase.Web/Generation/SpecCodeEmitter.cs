using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using VellumPdf.Encryption;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Model;
using DocumentConformance = VellumPdf.Document.PdfConformance;

namespace VellumPdfShowcase.Web.Generation;

/// <summary>
/// Emits the C# a developer would write, by hand, against
/// <c>VellumPdf.Layout</c> to produce the document described by a
/// <see cref="DocumentSpec"/>. The output is a self-contained script: it opens
/// with its own <see langword="using"/> directives and ends by returning the
/// rendered <c>byte[]</c>, and it assumes the three names <see cref="SpecAssets"/>
/// declares (<c>EmbeddedFonts</c>, <c>Images</c>, <c>IccProfile</c>) are
/// already in scope under those names, exactly as a page that fetched them
/// once through <c>HttpClient</c> would have them. <c>EmbeddedFonts</c> and
/// <c>Images</c> are each an <see cref="IReadOnlyList{T}"/> of <c>byte[]</c>;
/// <c>IccProfile</c> is a bare <c>byte[]</c>, not a collection.
/// </summary>
/// <remarks>
/// This type and <see cref="SpecRenderer"/> both read only the
/// <see cref="DocumentSpec"/> passed to them. Nothing here calls
/// <see cref="SpecRenderer"/> or vice versa; the test project's Roslyn
/// round-trip test is what keeps the two from silently drifting apart.
/// </remarks>
public static class SpecCodeEmitter
{
    private static readonly (string Name, double Width, double Height)[] NamedPageSizes =
    [
        ("A0", 2383.94, 3370.39),
        ("A1", 1683.78, 2383.94),
        ("A2", 1190.55, 1683.78),
        ("A3", 841.89, 1190.55),
        ("A4", 595.28, 841.89),
        ("A5", 419.53, 595.28),
        ("A6", 297.64, 419.53),
        ("Letter", 612, 792),
        ("Legal", 612, 1008),
        ("Ledger", 1224, 792),
    ];

    /// <summary>Produces the C# snippet that builds the document described by <paramref name="spec"/>.</summary>
    /// <remarks>
    /// SECURITY: the returned text carries visitor-supplied content (headings,
    /// paragraph runs, metadata, passwords, and so on) verbatim, with only the
    /// C# string-literal escaping <see cref="Literal"/> applies. It must never
    /// be handed to a syntax highlighter, or anything else, through
    /// <c>MarkupString</c>, <c>innerHTML</c>, or any other unencoded HTML sink.
    /// No such sink exists in this application today; none may be added
    /// without first HTML-encoding this text, or choosing a highlighter that
    /// encodes its own input.
    /// <para>
    /// The classic <c>using (...) { }</c> statement is used in place of the
    /// newer <c>using var</c> declaration. Both are equally idiomatic modern
    /// C#, but only the block form is accepted by the Roslyn scripting host
    /// the round-trip test compiles this snippet with; a script's top level is
    /// not a method body, and the compiler rejects a using declaration there.
    /// </para>
    /// <para>
    /// Exception contract: every failure this method can produce surfaces as
    /// <see cref="ArgumentException"/> (including its
    /// <see cref="ArgumentOutOfRangeException"/> and <see cref="ArgumentNullException"/>
    /// subtypes, for a malformed <paramref name="spec"/> the model failed to
    /// reject), the SAME family <see cref="Generation.SpecRenderer.Render"/>
    /// promises. Unlike <see cref="Generation.SpecRenderer.Render"/>, this
    /// method NEVER throws <see cref="InvalidOperationException"/>: it emits
    /// TEXT referencing <paramref name="spec"/>'s asset bytes (<c>Images[N]</c>,
    /// an embedded font, an ICC profile) by position, and never decodes or
    /// parses any of them itself, so a well-signed but structurally corrupt
    /// image or font that makes <see cref="Generation.SpecRenderer.Render"/>
    /// throw <see cref="InvalidOperationException"/> makes this method return
    /// successfully instead, with code that is nonetheless CORRECT: a visitor
    /// who copies it and runs it against the same bytes hits the identical
    /// decode failure <see cref="Generation.SpecRenderer.Render"/> already
    /// hit, unwrapped, in their own environment, which is the expected
    /// outcome of copying code that references bad bytes, not a defect in
    /// what this method produced. A SECOND case joins it now that the model no
    /// longer bounds page geometry itself: a page too small to lay one line
    /// out on, or an element demanding more page continuations than the
    /// library permits, makes <see cref="Generation.SpecRenderer.Render"/>
    /// throw while this method returns successfully. The emitted code is
    /// correct in that case too, for the same reason: a visitor who runs it
    /// meets the identical refusal from the identical library call. Both
    /// consumers still agree about every rejection the MODEL performs, which
    /// is what <c>SymmetryTests</c> guards.
    /// A caller wanting one catch clause complete
    /// for THIS method alone can therefore catch <see cref="ArgumentException"/>;
    /// a caller wanting completeness across both this method and
    /// <see cref="Generation.SpecRenderer.Render"/> together still needs
    /// <see cref="InvalidOperationException"/> too, exactly as
    /// <see cref="Generation.SpecRenderer.Render"/>'s own contract already states.
    /// </para>
    /// </remarks>
    public static string Emit(DocumentSpec spec)
    {
        spec.ValidateEmbeddedFontReferences();
        spec.ValidateAggregateAssetBytes();

        var writer = new CodeWriter();
        new Emitter(spec, writer).EmitDocument();
        return writer.ToString();
    }

    /// <summary>
    /// Holds the state one call to <see cref="Emit"/> threads through every
    /// helper below: the output buffer, the running index used to name each
    /// loaded image, and the <see cref="TextStyleSpec"/> instances used more
    /// than once across <paramref name="documentSpec"/>, each hoisted into its
    /// own local variable up front rather than written out again, in full, at
    /// every use site.
    /// </summary>
    private sealed class Emitter(DocumentSpec documentSpec, CodeWriter writer)
    {
        private readonly Dictionary<TextStyleSpec, string> _hoistedStyles = BuildHoistedStyleNames(documentSpec);

        /// <summary>
        /// The asset index (into <c>Images</c>) assigned to every DISTINCT
        /// <see cref="ImageSpec"/> reachable from <see cref="DocumentSpec.Content"/>,
        /// one entry per instance, keyed by REFERENCE identity. Built from
        /// <see cref="DistinctContentImagesByReference"/>, the SAME method
        /// <see cref="SpecAssets.FromSpec"/> calls to decide how many byte
        /// arrays <c>Images</c> holds and in what order, so this emitter and
        /// the assets the round-trip test (and a real page) load for it
        /// cannot disagree about which occurrence is "the same image" and
        /// which is a new one.
        /// </summary>
        private readonly Dictionary<ImageSpec, int> _imageIndices = BuildImageIndices(documentSpec);

        /// <summary>
        /// The subset of <see cref="_imageIndices"/>'s keys that occur two or
        /// more times in <see cref="DocumentSpec.Content"/>, keyed the same
        /// way, by reference identity rather than <see cref="ImageSpec"/>'s
        /// own value equality: see the remark on
        /// <see cref="Generation.SpecRenderer.RenderContext"/>'s own
        /// <c>ImageCache</c> for why reference identity, not value equality,
        /// is the right rule for "is this the same image" here, and why
        /// stating it with an explicit comparer rather than leaning on
        /// <see cref="ImageSpec"/>'s record-equality fallback is deliberate.
        /// A member of this set is declared once, up front, by
        /// <see cref="EmitHoistedImages"/>, exactly as <see cref="_hoistedStyles"/>
        /// hoists a <see cref="TextStyleSpec"/> used more than once; an image
        /// used exactly once is left inline at its one occurrence instead,
        /// unchanged from how every image was emitted before this member
        /// existed.
        /// </summary>
        private readonly IReadOnlySet<ImageSpec> _hoistedImages = BuildHoistedImages(documentSpec);

        private int _listIndex;
        private int _tableIndex;
        private int _multiRunIndex;

        public void EmitDocument()
        {
            EmitUsings();
            writer.Line();
            EmitRequiredAssetsComment();

            var initializers = BuildDocumentInitializers(documentSpec);
            if (initializers.Count > 0)
            {
                writer.Line("using (var document = new Document");
                writer.Line("{");
                using (writer.Indent())
                {
                    foreach (var initializer in initializers)
                    {
                        writer.Line(initializer);
                    }
                }

                writer.Line("})");
            }
            else
            {
                writer.Line("using (var document = new Document())");
            }

            writer.Line("{");
            using (writer.Indent())
            {
                EmitEmbeddedFonts();
                EmitHoistedImages();
                EmitHoistedStyles();

                // Consulted only by the one Document.Add(string, TextStyle?)
                // overload, which EmitPlainText emits for a PlainTextSpec left
                // unstyled: see the remark on DocumentSpec.DefaultTextStyle.
                // HeadingSpec and ParagraphSpec resolve their own fallback
                // directly below and never read this value; ListItemSpec and
                // TableCellSpec, when unstyled, stay null and are resolved
                // later by their own container. This call is emitted only
                // when the specification actually contains an unstyled
                // PlainTextSpec: in every other document it would be fully
                // spelled out and provably inert, sitting above unstyled
                // ListItem and Cell constructions it does nothing for.
                // HasUnstyledPlainText below, CollectTextStyles' conditional
                // yield of this same style, and SpecRenderer's own call to
                // the identical predicate before calling Document.SetDefaultFont
                // must all agree. EmitHoistedStyles always assigns every
                // hoisted local it declares, so a disagreement never leaves
                // anything unassigned; what it actually produces is a
                // snippet that either hoists DefaultTextStyle into a local
                // nothing visibly uses, or calls SetDefaultFont without the
                // style having been counted toward hoisting, changing the
                // displayed text while the rendered PDF stays identical, so
                // a byte comparison of rendered output cannot catch it.
                if (HasUnstyledPlainText(documentSpec))
                {
                    writer.Line($"document.SetDefaultFont({StyleExpression(documentSpec.DefaultTextStyle)});");
                    writer.Line();
                }

                EmitMetadata();

                foreach (var item in documentSpec.Content)
                {
                    EmitContentItem(item);
                    writer.Line();
                }

                if (documentSpec.Header is { } header)
                {
                    EmitRunningBand("Header", header);
                }

                if (documentSpec.Footer is { } footer)
                {
                    EmitRunningBand("Footer", footer);
                }

                EmitOutputIntent();
                EmitEncryption();

                writer.EnsureBlankLine();
                writer.Line("using (var output = new MemoryStream())");
                writer.Line("{");
                using (writer.Indent())
                {
                    writer.Line("document.Save(output);");
                    writer.Line("return output.ToArray();");
                }

                writer.Line("}");
            }

            writer.Line("}");
        }

        private void EmitUsings()
        {
            writer.Line("using System;");
            writer.Line("using System.IO;");
            writer.Line("using VellumPdf.Fonts;");
            writer.Line("using VellumPdf.Layout;");
            writer.Line("using VellumPdf.Layout.Core;");
            writer.Line("using VellumPdf.Layout.Elements;");
            writer.Line("using VellumPdf.Layout.Elements.Table;");

            if (EmitPageSizeInitializer(documentSpec.Page) is not null)
            {
                writer.Line("using VellumPdf.Document;");
            }

            if (documentSpec.Content.OfType<ImageSpec>().Any())
            {
                writer.Line("using VellumPdf.Images;");
            }

            if (documentSpec.Conformance != DocumentConformance.None)
            {
                writer.Line("using DocumentConformance = VellumPdf.Document.PdfConformance;");
            }

            if (documentSpec.Encryption is not null)
            {
                writer.Line("using VellumPdf.Encryption;");
            }
        }

        /// <summary>
        /// Names whichever of the
        /// three asset names <see cref="SpecAssets"/> declares this
        /// particular snippet actually reads as a bare identifier, together
        /// with its real type, so a reader is not left to guess where
        /// <c>EmbeddedFonts</c>, <c>Images</c> or <c>IccProfile</c> come from
        /// or what to declare in their place. <c>EmbeddedFonts</c> and
        /// <c>Images</c> are each an <see cref="IReadOnlyList{T}"/> of
        /// <c>byte[]</c>; <c>IccProfile</c> is a bare <c>byte[]</c>. The two
        /// shapes are never conflated into one sentence, since only a
        /// disjoint subset of the three names shares either shape. Writes
        /// nothing when the snippet uses none of them.
        /// </summary>
        private void EmitRequiredAssetsComment()
        {
            List<string> names = [];

            if (documentSpec.EmbeddedFonts.Count > 0)
            {
                names.Add("EmbeddedFonts");
            }

            if (documentSpec.Content.OfType<ImageSpec>().Any())
            {
                names.Add("Images");
            }

            if (documentSpec.OutputIntent is PdfAOutputIntentSpec)
            {
                names.Add("IccProfile");
            }

            if (names.Count == 0)
            {
                return;
            }

            writer.Line("// This snippet expects the following to already be in scope, fetched");
            writer.Line("// exactly as HttpClient would return them:");

            foreach (var name in names)
            {
                writer.Line($"// {name}, {AssetTypeDescriptions[name]}.");
            }

            writer.Line();
        }

        private static readonly Dictionary<string, string> AssetTypeDescriptions = new()
        {
            ["EmbeddedFonts"] = "an IReadOnlyList<byte[]>",
            ["Images"] = "an IReadOnlyList<byte[]>",
            ["IccProfile"] = "a byte[]",
        };

        private void EmitEmbeddedFonts()
        {
            for (var i = 0; i < documentSpec.EmbeddedFonts.Count; i++)
            {
                writer.Line($"var embeddedFont{i} = document.UseTrueTypeFont(EmbeddedFonts[{i}]);");
            }

            if (documentSpec.EmbeddedFonts.Count > 0)
            {
                writer.Line();
            }
        }

        /// <summary>
        /// Declares one <c>var imageN = Loader.Load(Images[N]);</c> local, up
        /// front, for every <see cref="ImageSpec"/> reference used two or
        /// more times in <see cref="DocumentSpec.Content"/> (<see cref="_hoistedImages"/>),
        /// mirroring <see cref="EmitHoistedStyles"/> exactly: a shared
        /// occurrence gets one declaration here, reused by every later
        /// <see cref="EmitImage"/> call site rather than re-decoded, so the
        /// compiled-and-executed snippet decodes and embeds that image once,
        /// matching <see cref="Generation.SpecRenderer.BuildImage"/>'s own
        /// image cache. An image used only once is left inline at its one
        /// occurrence, unchanged from before this method existed.
        /// </summary>
        private void EmitHoistedImages()
        {
            if (_hoistedImages.Count == 0)
            {
                return;
            }

            foreach (var image in _hoistedImages.OrderBy(image => _imageIndices[image]))
            {
                EmitImageLoadDeclaration(image);
            }

            writer.Line();
        }

        private void EmitImageLoadDeclaration(ImageSpec imageSpec)
        {
            var index = _imageIndices[imageSpec];
            var loaderName = ImageLoaderName(imageSpec.Format);
            writer.Line($"var image{index} = {loaderName}.Load(Images[{index}]);");
        }

        private void EmitHoistedStyles()
        {
            if (_hoistedStyles.Count == 0)
            {
                return;
            }

            foreach (var (style, name) in _hoistedStyles.OrderBy(pair => StyleIndex(pair.Value)))
            {
                writer.Line($"var {name} = {BuildTextStyleExpression(style)};");
            }

            writer.Line();
        }

        /// <summary>
        /// Extracts the numeric suffix of a hoisted style name such as
        /// <c>style12</c>, so declarations sort in numeric rather than
        /// ordinal order: ordinal sorting would place <c>style10</c> before
        /// <c>style2</c> once ten or more styles are hoisted.
        /// </summary>
        private static int StyleIndex(string name) => int.Parse(name.AsSpan("style".Length), CultureInfo.InvariantCulture);

        private void EmitMetadata()
        {
            if (documentSpec.Metadata is not { } metadata)
            {
                return;
            }

            if (metadata.Title is not null)
            {
                writer.Line($"document.Info.Title = {Literal(metadata.Title)};");
            }

            if (metadata.Author is not null)
            {
                writer.Line($"document.Info.Author = {Literal(metadata.Author)};");
            }

            if (metadata.Subject is not null)
            {
                writer.Line($"document.Info.Subject = {Literal(metadata.Subject)};");
            }

            if (metadata.Keywords is not null)
            {
                writer.Line($"document.Info.Keywords = {Literal(metadata.Keywords)};");
            }

            if (metadata.Creator is not null)
            {
                writer.Line($"document.Info.Creator = {Literal(metadata.Creator)};");
            }

            if (metadata.Producer is not null)
            {
                writer.Line($"document.Info.Producer = {Literal(metadata.Producer)};");
            }

            writer.Line();
        }

        private void EmitContentItem(ContentItemSpec item)
        {
            switch (item)
            {
                case PlainTextSpec plainText:
                    EmitPlainText(plainText);
                    break;
                case HeadingSpec heading:
                    EmitHeading(heading);
                    break;
                case ParagraphSpec paragraph:
                    EmitParagraph(paragraph);
                    break;
                case ListSpec list:
                    EmitList(list);
                    break;
                case TableSpec table:
                    EmitTable(table);
                    break;
                case ImageSpec image:
                    EmitImage(image);
                    break;
                case PieChartSpec pieChart:
                    EmitPieChart(pieChart);
                    break;
                case LineSeparatorSpec lineSeparator:
                    EmitLineSeparator(lineSeparator);
                    break;

                    // No default arm: DocumentSpec.Content's own construction-time
                    // validation (IsRecognisedContentItemType) already rejects any
                    // ContentItemSpec subtype other than the eight named above, so
                    // this switch STATEMENT (unlike a switch expression) compiles
                    // without one and has no unreachable branch for the coverage
                    // gate to find. See the remark on ContentItemSpec.
            }
        }

        /// <summary>
        /// The one call site that reads <see cref="DocumentSpec.DefaultTextStyle"/>
        /// indirectly: an unstyled <see cref="PlainTextSpec"/> omits the trailing
        /// style argument outright, the same convention <see cref="EmitHeading"/>
        /// uses for an unset <c>Style</c>, so the document's registered default
        /// governs the rendered text rather than a style spelled out here.
        /// </summary>
        private void EmitPlainText(PlainTextSpec plainTextSpec)
        {
            writer.Line(plainTextSpec.Style is { } style
                ? $"document.Add({Literal(plainTextSpec.Text)}, {StyleExpression(style)});"
                : $"document.Add({Literal(plainTextSpec.Text)});");
        }

        private void EmitHeading(HeadingSpec headingSpec)
        {
            List<string> initializers = [];

            // Level defaults to 0 (top-level), matching the library's own
            // Heading, so the same omit-when-default convention every other
            // optional member here follows applies to it too.
            if (headingSpec.Level != 0)
            {
                initializers.Add($"Level = {headingSpec.Level}");
            }

            if (headingSpec.Alignment != HorizontalAlignment.Left)
            {
                initializers.Add($"Alignment = HorizontalAlignment.{headingSpec.Alignment}");
            }

            if (headingSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            if (headingSpec.BookmarkTitle is not null)
            {
                initializers.Add($"BookmarkTitle = {Literal(headingSpec.BookmarkTitle)}");
            }

            if (headingSpec.Language is not null)
            {
                initializers.Add($"Language = {Literal(headingSpec.Language)}");
            }

            // Heading's style parameter defaults to null (automatic per-level
            // styling), so an unset style is a trailing argument omitted
            // outright rather than spelled out as an explicit null.
            var ctorExpr = headingSpec.Style is { } style
                ? $"new Heading({Literal(headingSpec.Text)}, {StyleExpression(style)})"
                : $"new Heading({Literal(headingSpec.Text)})";

            EmitAdd(ctorExpr, initializers);
        }

        private void EmitParagraph(ParagraphSpec paragraphSpec)
        {
            List<string> initializers = [];

            if (paragraphSpec.Alignment != HorizontalAlignment.Left)
            {
                initializers.Add($"Alignment = HorizontalAlignment.{paragraphSpec.Alignment}");
            }

            if (paragraphSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            if (paragraphSpec.Language is not null)
            {
                initializers.Add($"Language = {Literal(paragraphSpec.Language)}");
            }

            if (paragraphSpec.Runs.Count == 1)
            {
                var run = paragraphSpec.Runs[0];
                EmitAdd($"new Paragraph({Literal(run.Text)}, {StyleExpression(run.Style)})", initializers);
                return;
            }

            var runsVariable = $"runs{_multiRunIndex++}";
            writer.Line($"var {runsVariable} = new TextRun[]");
            writer.Line("{");
            using (writer.Indent())
            {
                foreach (var run in paragraphSpec.Runs)
                {
                    writer.Line($"new TextRun({Literal(run.Text)}, {StyleExpression(run.Style)}),");
                }
            }

            writer.Line("};");
            EmitAdd($"new Paragraph({runsVariable})", initializers);
        }

        // ListElement, ListItem, TableElement and Cell all expose their settable
        // members as `init`, which the language only allows to be assigned inside
        // the object-initializer expression that constructs the instance, so the
        // emitted code folds every optional property into the one declaration
        // EmitDeclaration writes rather than assigning to it afterward.
        // Uniqueness across two lists, tables or multi-run paragraphs in the same
        // document comes from the per-class counters _listIndex, _tableIndex and
        // _multiRunIndex, the same mechanism EmitImage uses for image0, image1;
        // row and cell names are then derived from their own table's
        // already-unique name.
        private void EmitList(ListSpec listSpec)
        {
            List<string> initializers = [];

            if (listSpec.Indent is { } indent)
            {
                initializers.Add($"Indent = {Num(indent)}");
            }

            if (listSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            if (listSpec.DefaultStyle is not null)
            {
                initializers.Add($"DefaultStyle = {StyleExpression(listSpec.DefaultStyle)}");
            }

            var listVariable = $"list{_listIndex++}";
            EmitDeclaration(listVariable, $"new ListElement(ListStyle.{listSpec.Style})", initializers);

            for (var i = 0; i < listSpec.Items.Count; i++)
            {
                var itemVariable = EmitListItem(listSpec.Items[i], $"{listVariable}Item{i}");
                writer.Line($"{listVariable}.Add({itemVariable});");
            }

            writer.Line($"document.Add({listVariable});");
        }

        private string EmitListItem(ListItemSpec itemSpec, string variableName)
        {
            List<string> initializers = [];

            if (itemSpec.Language is not null)
            {
                initializers.Add($"Language = {Literal(itemSpec.Language)}");
            }

            var ctorExpr = itemSpec.Style is { } style
                ? $"new ListItem({Literal(itemSpec.Text)}, {StyleExpression(style)})"
                : $"new ListItem({Literal(itemSpec.Text)})";

            EmitDeclaration(variableName, ctorExpr, initializers);

            for (var i = 0; i < itemSpec.Children.Count; i++)
            {
                var childVariable = EmitListItem(itemSpec.Children[i], $"{variableName}Child{i}");
                writer.Line($"{variableName}.AddChild({childVariable});");
            }

            return variableName;
        }

        private void EmitTable(TableSpec tableSpec)
        {
            List<string> initializers = [];

            if (tableSpec.DefaultCellStyle is not null)
            {
                initializers.Add($"DefaultCellStyle = {StyleExpression(tableSpec.DefaultCellStyle)}");
            }

            if (tableSpec.BorderWidth is { } borderWidth)
            {
                initializers.Add($"BorderWidth = {Num(borderWidth)}");
            }

            if (tableSpec.BorderColor is { } borderColor)
            {
                initializers.Add($"BorderColor = {EmitColor(borderColor)}");
            }

            if (tableSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            var tableVariable = $"table{_tableIndex++}";
            EmitDeclaration(tableVariable, "new TableElement()", initializers);

            if (tableSpec.ColumnWidths is { Count: > 0 } widths)
            {
                writer.Line($"{tableVariable}.SetColumnWidths([{string.Join(", ", widths.Select(Num))}]);");
            }

            for (var rowIndex = 0; rowIndex < tableSpec.Rows.Count; rowIndex++)
            {
                var row = tableSpec.Rows[rowIndex];
                var rowVariable = $"{tableVariable}Row{rowIndex}";
                var addRowCall = row.IsHeader ? $"{tableVariable}.AddHeaderRow()" : $"{tableVariable}.AddRow()";
                writer.Line($"var {rowVariable} = {addRowCall};");

                for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    EmitCell(row.Cells[cellIndex], rowVariable, $"{rowVariable}Cell{cellIndex}");
                }
            }

            writer.Line($"document.Add({tableVariable});");
        }

        private void EmitCell(TableCellSpec cellSpec, string rowVariable, string cellVariable)
        {
            List<string> initializers = [];

            if (cellSpec.ColSpan != 1)
            {
                initializers.Add($"ColSpan = {cellSpec.ColSpan}");
            }

            if (cellSpec.RowSpan != 1)
            {
                initializers.Add($"RowSpan = {cellSpec.RowSpan}");
            }

            if (cellSpec.Style is not null)
            {
                initializers.Add($"Style = {StyleExpression(cellSpec.Style)}");
            }

            if (cellSpec.Padding is { } padding)
            {
                initializers.Add($"Padding = {EmitEdgeInsets(padding)}");
            }

            if (cellSpec.Background is { } background)
            {
                initializers.Add($"Background = {EmitColor(background)}");
            }

            if (cellSpec.Alignment != HorizontalAlignment.Left)
            {
                initializers.Add($"Alignment = HorizontalAlignment.{cellSpec.Alignment}");
            }

            if (cellSpec.Language is not null)
            {
                initializers.Add($"Language = {Literal(cellSpec.Language)}");
            }

            EmitDeclaration(cellVariable, $"new Cell({Literal(cellSpec.Content)})", initializers);
            writer.Line($"{rowVariable}.AddCell({cellVariable});");
        }

        /// <summary>
        /// Emits one content position's <c>document.Add(new LayoutImage(...))</c>.
        /// <paramref name="imageSpec"/>'s decode is emitted inline, right
        /// here, only the FIRST time this reference is encountered and it is
        /// not one of <see cref="_hoistedImages"/> (used exactly once, so
        /// hoisting it into the shared preamble would only move the
        /// declaration, not share it); a reference used two or more times has
        /// already been declared by <see cref="EmitHoistedImages"/>, so every
        /// occurrence here, including the first, only references the
        /// existing <c>imageN</c> local.
        /// </summary>
        private void EmitImage(ImageSpec imageSpec)
        {
            var imageVariable = $"image{_imageIndices[imageSpec]}";

            if (!_hoistedImages.Contains(imageSpec))
            {
                EmitImageLoadDeclaration(imageSpec);
            }

            List<string> initializers = [];

            if (imageSpec.Width is { } width)
            {
                initializers.Add($"Width = {Num(width)}");
            }

            if (imageSpec.Height is { } height)
            {
                initializers.Add($"Height = {Num(height)}");
            }

            if (imageSpec.Alignment != HorizontalAlignment.Left)
            {
                initializers.Add($"Alignment = HorizontalAlignment.{imageSpec.Alignment}");
            }

            if (imageSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            if (imageSpec.AltText is not null)
            {
                initializers.Add($"AltText = {Literal(imageSpec.AltText)}");
            }

            EmitAdd($"new LayoutImage({imageVariable})", initializers);
        }

        private void EmitPieChart(PieChartSpec pieChartSpec)
        {
            List<string> initializers =
            [
                $"Slices = [{string.Join(", ", pieChartSpec.Slices.Select(EmitPieSlice))}]",
                $"Diameter = {Num(pieChartSpec.Diameter)}",
            ];

            if (pieChartSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            if (pieChartSpec.StrokeColor is { } strokeColor)
            {
                initializers.Add($"StrokeColor = {EmitColor(strokeColor)}");
            }

            if (pieChartSpec.StrokeWidth != 0.5)
            {
                initializers.Add($"StrokeWidth = {Num(pieChartSpec.StrokeWidth)}");
            }

            if (pieChartSpec.Alignment != HorizontalAlignment.Center)
            {
                initializers.Add($"Alignment = HorizontalAlignment.{pieChartSpec.Alignment}");
            }

            if (pieChartSpec.StartAngle != double.Pi / 2)
            {
                initializers.Add($"StartAngle = {Num(pieChartSpec.StartAngle)}");
            }

            if (!pieChartSpec.Clockwise)
            {
                initializers.Add("Clockwise = false");
            }

            if (pieChartSpec.AltText is not null)
            {
                initializers.Add($"AltText = {Literal(pieChartSpec.AltText)}");
            }

            if (pieChartSpec.Decorative)
            {
                initializers.Add("Decorative = true");
            }

            EmitAdd("new PieChart()", initializers);
        }

        private void EmitLineSeparator(LineSeparatorSpec lineSeparatorSpec)
        {
            List<string> initializers = [];

            if (lineSeparatorSpec.LineWidth != 1)
            {
                initializers.Add($"LineWidth = {Num(lineSeparatorSpec.LineWidth)}");
            }

            if (!IsColorBitwiseIdentical(lineSeparatorSpec.Color, ColorRgb.Black))
            {
                initializers.Add($"Color = {EmitColor(lineSeparatorSpec.Color)}");
            }

            if (lineSeparatorSpec.Margins is { } margins)
            {
                initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
            }

            EmitAdd("new LineSeparator()", initializers);
        }

        private void EmitRunningBand(string kind, RunningBandSpec bandSpec)
        {
            // Both Style and Alignment are optional constructor parameters
            // (Alignment defaults to Center), so a document-default alignment
            // is a trailing argument omitted outright, the same way EmitHeading
            // omits an unset style rather than passing it explicitly.
            var ctorExpr = bandSpec.Alignment == HorizontalAlignment.Center
                ? $"new RunningBand({Literal(bandSpec.Template)}, {StyleExpression(bandSpec.Style)})"
                : $"new RunningBand({Literal(bandSpec.Template)}, {StyleExpression(bandSpec.Style)}, HorizontalAlignment.{bandSpec.Alignment})";

            List<string> initializers = bandSpec.Height is { } height ? [$"Height = {Num(height)}"] : [];
            EmitAssignment($"document.{kind}", ctorExpr, initializers);
        }

        private void EmitOutputIntent()
        {
            switch (documentSpec.OutputIntent)
            {
                case PdfAOutputIntentSpec pdfA:
                    // info is an optional trailing parameter (defaults to null),
                    // so an unset Info is omitted rather than passed explicitly.
                    var infoArg = pdfA.Info is null ? "" : $", {Literal(pdfA.Info)}";
                    writer.Line($"document.SetPdfAOutputIntent(IccProfile, {pdfA.ComponentCount}, {Literal(pdfA.OutputConditionIdentifier)}{infoArg});");
                    break;
                case CmykOutputIntentSpec cmyk:
                    writer.Line($"document.UseCmykOutputIntent({Literal(cmyk.OutputConditionIdentifier)});");
                    break;
            }
        }

        private void EmitEncryption()
        {
            if (documentSpec.Encryption is not { } encryption)
            {
                return;
            }

            writer.Line("document.Encrypt(new PdfEncryptionSettings");
            writer.Line("{");
            using (writer.Indent())
            {
                if (encryption.UserPassword is not null)
                {
                    writer.Line($"UserPassword = {Literal(encryption.UserPassword)},");
                }

                if (encryption.OwnerPassword is not null)
                {
                    writer.Line($"OwnerPassword = {Literal(encryption.OwnerPassword)},");
                }

                // Permissions defaults to PdfPermissions.All on both
                // PdfEncryptionSettings and EncryptionSpec, so the same
                // omit-when-default convention every other optional member
                // here follows applies to it too.
                if (encryption.Permissions != PdfPermissions.All)
                {
                    writer.Line($"Permissions = {EmitPermissions(encryption.Permissions)},");
                }

                if (!encryption.EncryptMetadata)
                {
                    writer.Line("EncryptMetadata = false,");
                }
            }

            writer.Line("});");
        }

        /// <summary>
        /// Returns the expression for <paramref name="styleSpec"/>: the shared
        /// local variable name when it was hoisted (used two or more times
        /// across the document), or the full <c>new TextStyle { ... }</c>
        /// expression otherwise.
        /// </summary>
        private string StyleExpression(TextStyleSpec styleSpec) =>
            _hoistedStyles.TryGetValue(styleSpec, out var name) ? name : BuildTextStyleExpression(styleSpec);

        private void EmitAdd(string ctorExpr, IReadOnlyList<string> initializers) =>
            EmitWrapped("document.Add(", ")", ctorExpr, initializers);

        /// <summary>Emits <c>var {variableName} = {ctorExpr}{ initializers };</c>, folding every optional property into the one declaration an init-only type requires.</summary>
        private void EmitDeclaration(string variableName, string ctorExpr, IReadOnlyList<string> initializers) =>
            EmitWrapped($"var {variableName} = ", "", ctorExpr, initializers);

        /// <summary>Emits <c>{target} = {ctorExpr}{ initializers };</c>, for a mutable property (such as <c>Document.Header</c>) assigned an init-only-typed value.</summary>
        private void EmitAssignment(string target, string ctorExpr, IReadOnlyList<string> initializers) =>
            EmitWrapped($"{target} = ", "", ctorExpr, initializers);

        private void EmitWrapped(string prefix, string suffix, string ctorExpr, IReadOnlyList<string> initializers)
        {
            if (initializers.Count == 0)
            {
                writer.Line($"{prefix}{ctorExpr}{suffix};");
                return;
            }

            writer.Line($"{prefix}{ctorExpr}");
            writer.Line("{");
            using (writer.Indent())
            {
                foreach (var initializer in initializers)
                {
                    writer.Line($"{initializer},");
                }
            }

            writer.Line($"}}{suffix};");
        }
    }

    /// <summary>
    /// Walks every corner of <paramref name="spec"/> a <see cref="TextStyleSpec"/>
    /// can appear in, and assigns a shared local variable name to each style
    /// referenced two or more times, in first-encountered order. A style used
    /// only once is left to be inlined at its one use site. Keyed on
    /// <see cref="TextStyleSpec"/>'s own record value equality (the default
    /// dictionary comparer, not a reference comparer), so two distinct but
    /// value-equal instances hoist to one shared local exactly as
    /// <see cref="SpecRenderer"/>'s own style cache merges them into one
    /// shared <c>TextStyle</c> instance. Changing one side without the other
    /// would let the library's adjacent-run merging diverge between the two.
    /// </summary>
    private static Dictionary<TextStyleSpec, string> BuildHoistedStyleNames(DocumentSpec spec)
    {
        var counts = new Dictionary<TextStyleSpec, int>();
        List<TextStyleSpec> firstSeenOrder = [];

        foreach (var style in CollectTextStyles(spec))
        {
            if (counts.TryGetValue(style, out var count))
            {
                counts[style] = count + 1;
            }
            else
            {
                counts.Add(style, 1);
                firstSeenOrder.Add(style);
            }
        }

        var names = new Dictionary<TextStyleSpec, string>();
        var index = 0;

        foreach (var style in firstSeenOrder)
        {
            if (counts[style] > 1)
            {
                names.Add(style, $"style{index++}");
            }
        }

        return names;
    }

    /// <summary>
    /// Every <see cref="ImageSpec"/> reachable through <paramref name="spec"/>'s
    /// <see cref="DocumentSpec.Content"/>, deduplicated by REFERENCE identity,
    /// in first-occurrence order: an instance appearing at several positions
    /// is listed here once, at its first position. This is the single
    /// definition of which occurrence is "the same image" and which is a new
    /// one; <see cref="SpecAssets.FromSpec"/> calls it to decide how many
    /// byte arrays <c>Images</c> holds and in what order, and
    /// <see cref="Emitter"/>'s own <c>BuildImageIndices</c> calls it to assign
    /// the identical index to the identical instance, so the two cannot
    /// silently disagree about which <c>Images[N]</c> entry an occurrence
    /// reads. <see cref="Generation.SpecRenderer.RenderContext"/>'s own
    /// <c>ImageCache</c> answers the same question, independently, by the
    /// same rule (reference identity); see its remark for why value equality
    /// is the wrong rule here even though <see cref="ImageSpec"/>'s own
    /// record equality happens to fall back to something close to it.
    /// </summary>
    internal static IReadOnlyList<ImageSpec> DistinctContentImagesByReference(DocumentSpec spec)
    {
        var seen = new HashSet<ImageSpec>(ReferenceEqualityComparer.Instance);
        List<ImageSpec> distinct = [];

        foreach (var image in spec.Content.OfType<ImageSpec>())
        {
            if (seen.Add(image))
            {
                distinct.Add(image);
            }
        }

        return distinct;
    }

    /// <summary>
    /// Assigns the asset index <see cref="Emitter"/> uses for
    /// <c>imageN</c>/<c>Images[N]</c> to every distinct <see cref="ImageSpec"/>
    /// reference <see cref="DistinctContentImagesByReference"/> finds, in the
    /// same order: index 0 is that list's first entry, and so on. Kept
    /// separate from <see cref="BuildHoistedImages"/> because every distinct
    /// image needs an index (even one used only once, to name its inline
    /// declaration and its <c>Images[N]</c> read), while only a repeated one
    /// needs hoisting.
    /// </summary>
    private static Dictionary<ImageSpec, int> BuildImageIndices(DocumentSpec spec)
    {
        var indices = new Dictionary<ImageSpec, int>(ReferenceEqualityComparer.Instance);
        var distinct = DistinctContentImagesByReference(spec);

        for (var i = 0; i < distinct.Count; i++)
        {
            indices.Add(distinct[i], i);
        }

        return indices;
    }

    /// <summary>
    /// The subset of <see cref="DistinctContentImagesByReference"/>'s result
    /// that occurs two or more times in <paramref name="spec"/>'s
    /// <see cref="DocumentSpec.Content"/>, keyed the same way, by reference
    /// identity. Mirrors <see cref="BuildHoistedStyleNames"/>'s "used more
    /// than once" rule, applied to images instead of styles, and existing for
    /// the identical reason: <see cref="Emitter.EmitHoistedImages"/> declares
    /// each of these once, up front, so a repeated reference is decoded, and
    /// embedded, once rather than once per occurrence, matching
    /// <see cref="Generation.SpecRenderer.RenderContext"/>'s own <c>ImageCache</c>.
    /// </summary>
    private static IReadOnlySet<ImageSpec> BuildHoistedImages(DocumentSpec spec)
    {
        var counts = new Dictionary<ImageSpec, int>(ReferenceEqualityComparer.Instance);

        foreach (var image in spec.Content.OfType<ImageSpec>())
        {
            counts[image] = counts.TryGetValue(image, out var count) ? count + 1 : 1;
        }

        var hoisted = new HashSet<ImageSpec>(ReferenceEqualityComparer.Instance);

        foreach (var (image, count) in counts)
        {
            if (count > 1)
            {
                hoisted.Add(image);
            }
        }

        return hoisted;
    }

    /// <summary>
    /// Whether <paramref name="spec"/> contains a <see cref="PlainTextSpec"/>
    /// with no explicit <see cref="PlainTextSpec.Style"/>, the only content
    /// item whose emitted code reads <see cref="DocumentSpec.DefaultTextStyle"/>.
    /// Used by <see cref="Emitter.EmitDocument"/>, to decide whether
    /// <c>document.SetDefaultFont</c> is emitted at all; by
    /// <see cref="CollectTextStyles(DocumentSpec)"/>, so hoisting counts this
    /// style exactly when the emitted code actually references it; and by
    /// <see cref="SpecRenderer.Render"/>, which calls the library's own
    /// <c>Document.SetDefaultFont</c> under the identical condition, so the
    /// two sides cannot disagree about whether that call happens. Internal
    /// rather than private for that last reason.
    /// </summary>
    internal static bool HasUnstyledPlainText(DocumentSpec spec) =>
        spec.Content.Any(item => item is PlainTextSpec { Style: null });

    private static IEnumerable<TextStyleSpec> CollectTextStyles(DocumentSpec spec)
    {
        if (HasUnstyledPlainText(spec))
        {
            yield return spec.DefaultTextStyle;
        }

        if (spec.Header is { } header)
        {
            yield return header.Style;
        }

        if (spec.Footer is { } footer)
        {
            yield return footer.Style;
        }

        foreach (var item in spec.Content)
        {
            foreach (var style in CollectTextStyles(item))
            {
                yield return style;
            }
        }
    }

    private static IEnumerable<TextStyleSpec> CollectTextStyles(ContentItemSpec item)
    {
        switch (item)
        {
            case PlainTextSpec { Style: { } style }:
                yield return style;
                break;
            case HeadingSpec { Style: { } style }:
                yield return style;
                break;
            case ParagraphSpec paragraph:
                foreach (var run in paragraph.Runs)
                {
                    yield return run.Style;
                }

                break;
            case ListSpec list:
                if (list.DefaultStyle is { } listDefaultStyle)
                {
                    yield return listDefaultStyle;
                }

                foreach (var listItem in list.Items)
                {
                    foreach (var style in CollectTextStyles(listItem))
                    {
                        yield return style;
                    }
                }

                break;
            case TableSpec table:
                if (table.DefaultCellStyle is { } tableDefaultStyle)
                {
                    yield return tableDefaultStyle;
                }

                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.Style is { } cellStyle)
                        {
                            yield return cellStyle;
                        }
                    }
                }

                break;
        }
    }

    private static IEnumerable<TextStyleSpec> CollectTextStyles(ListItemSpec item)
    {
        if (item.Style is { } style)
        {
            yield return style;
        }

        foreach (var child in item.Children)
        {
            foreach (var childStyle in CollectTextStyles(child))
            {
                yield return childStyle;
            }
        }
    }

    private static List<string> BuildDocumentInitializers(DocumentSpec spec)
    {
        List<string> initializers = [];

        if (EmitPageSizeInitializer(spec.Page) is { } pageSizeInitializer)
        {
            initializers.Add(pageSizeInitializer);
        }

        if (!spec.Margins.Equals(new EdgeInsets(72)))
        {
            initializers.Add($"Margins = {EmitEdgeInsets(spec.Margins)},");
        }

        if (spec.Conformance != DocumentConformance.None)
        {
            initializers.Add($"Conformance = DocumentConformance.{spec.Conformance},");
        }

        if (spec.Tagged)
        {
            initializers.Add("Tagged = true,");
        }

        if (spec.UseObjectStreams)
        {
            initializers.Add("UseObjectStreams = true,");
        }

        if (spec.Language is not null)
        {
            initializers.Add($"Language = {Literal(spec.Language)},");
        }

        return initializers;
    }

    /// <summary>
    /// Matches <paramref name="page"/> against the ten named page sizes
    /// <c>VellumPdf.Document.PageSize</c> declares, so the emitted code can
    /// read <c>PageSize.Letter</c> rather than the four raw points that
    /// constant expands to, and omits the initializer entirely when it
    /// matches A4, <c>Document</c>'s own default page size. <c>PageSize</c>
    /// and <c>PdfRectangle</c> are referenced unqualified: <see cref="EmitUsings"/>
    /// adds <c>using VellumPdf.Document;</c> whenever this method returns a
    /// non-null initializer, and neither name collides with anything else
    /// the snippet imports, so the full <c>VellumPdf.Document.</c> prefix
    /// this method used to emit was unnecessary.
    /// </summary>
    private static string? EmitPageSizeInitializer(PageSizeSpec page)
    {
        var preset = NamedPageSizes
            .Where(named => named.Width.Equals(page.WidthPoints) && named.Height.Equals(page.HeightPoints))
            .Select(named => named.Name)
            .FirstOrDefault();

        if (preset == "A4")
        {
            return null;
        }

        var pageSizeExpr = preset is not null
            ? $"PageSize.{preset}"
            : $"new PdfRectangle(0, 0, {Num(page.WidthPoints)}, {Num(page.HeightPoints)})";

        return $"PageSize = {pageSizeExpr},";
    }

    /// <summary>
    /// Never called with <see cref="PdfPermissions.All"/>: <see cref="Emitter.EmitEncryption"/>
    /// omits the <c>Permissions</c> initializer entirely whenever
    /// <see cref="EncryptionSpec.Permissions"/> equals <c>All</c>, the same
    /// default <see cref="PdfEncryptionSettings"/> itself applies, so a
    /// dedicated fast path for that value here would be dead code; there was
    /// one until measurement showed no sample, and no possible caller, could
    /// ever reach it.
    /// </summary>
    private static string EmitPermissions(PdfPermissions permissions)
    {
        if (permissions == PdfPermissions.None)
        {
            return "PdfPermissions.None";
        }

        var flags = Enum.GetValues<PdfPermissions>()
            .Where(flag => flag is not (PdfPermissions.None or PdfPermissions.All) && permissions.HasFlag(flag))
            .Select(flag => $"PdfPermissions.{flag}");

        return string.Join(" | ", flags);
    }

    /// <summary>
    /// The Kernel loader type name for each <see cref="ImageFormat"/> this
    /// model recognises.
    /// </summary>
    /// <remarks>
    /// COVERAGE: <see cref="ImageLoaderName"/> was once written as a
    /// <c>switch</c> expression with a <c>default</c> arm throwing
    /// <see cref="ArgumentOutOfRangeException"/>, unreachable from the public
    /// API (see below) but required by the compiler because
    /// <see cref="ImageFormat"/> is a public enumeration C# cannot prove
    /// exhaustive from its five named members alone. A branch-coverage gate
    /// cannot see that arm taken, and marking the WHOLE METHOD
    /// <c>[ExcludeFromCodeCoverage]</c> to accommodate it also hid the
    /// five REACHABLE arms from the gate; a later attempt to extract just
    /// the throw into its own excluded method did not help either, because the
    /// switch's own branch outcome ("which arm matched") is attributed to
    /// the enclosing, non-excluded method regardless of where the THROWN
    /// exception is constructed. A lookup table has no such branch at all:
    /// every entry below executes unconditionally, once, when this table is
    /// initialised, so the branch-coverage gate has nothing to find
    /// unreached here, and no <c>[ExcludeFromCodeCoverage]</c> is needed on
    /// this member or on <see cref="ImageLoaderName"/> itself.
    /// <see cref="ImageLoaderNames"/> not containing <paramref name="format"/>
    /// remains unreachable from the public API for the same reason the
    /// original throw arm was: <see cref="DocumentSpec.Content"/> rejects any
    /// <see cref="ImageSpec"/> whose declared <see cref="ImageSpec.Format"/>
    /// does not match its own byte signature, and <see cref="ImageSignature.Matches"/>,
    /// which performs that check, itself throws on any <see cref="ImageFormat"/>
    /// value outside the five named members before a <see cref="DocumentSpec"/>
    /// carrying one can ever be constructed; the indexer below throwing the
    /// BCL's own <see cref="KeyNotFoundException"/> rather than
    /// <see cref="ArgumentOutOfRangeException"/> in that unreachable case is
    /// an acceptable difference for something nothing can ever actually hit.
    /// </remarks>
    private static readonly IReadOnlyDictionary<ImageFormat, string> ImageLoaderNames = new Dictionary<ImageFormat, string>
    {
        [ImageFormat.Png] = "PngImageLoader",
        [ImageFormat.Jpeg] = "JpegImageLoader",
        [ImageFormat.Bmp] = "BmpImageLoader",
        [ImageFormat.Gif] = "GifImageLoader",
        [ImageFormat.Tiff] = "TiffImageLoader",
    };

    private static string ImageLoaderName(ImageFormat format) => ImageLoaderNames[format];

    private static string EmitPieSlice(PieSlice slice) =>
        string.IsNullOrEmpty(slice.Label)
            ? $"new PieSlice({Num(slice.Value)}, {EmitColor(slice.Color)})"
            : $"new PieSlice({Num(slice.Value)}, {EmitColor(slice.Color)}, {Literal(slice.Label)})";

    private static string BuildTextStyleExpression(TextStyleSpec spec)
    {
        // FontReference has an implicit conversion from both Standard14 and
        // EmbeddedFontHandle, so FontRef can be assigned the face or handle
        // directly, without the otherwise-redundant `new FontReference(...)`.
        //
        // A two-way comparison against FontKind.Embedded, not a switch over
        // both named members, for the identical coverage reason documented on
        // SpecRenderer.ImageLoaders, SpecRenderer.ToTextStyle and
        // SpecCodeEmitter.ImageLoaderName: a switch EXPRESSION here would need
        // a default arm the compiler requires but FontSpec.Kind's own
        // construction-time validation (see its remark) makes unreachable,
        // and that arm's branch outcome would be attributed to this method
        // regardless of where any thrown exception is constructed. FontKind
        // has exactly two members; anything other than Embedded is
        // Standard14 by construction, for any FontSpec that exists.
        var fontRefExpr = spec.Font.Kind == FontKind.Embedded
            ? $"embeddedFont{spec.Font.EmbeddedFontIndex}"
            : $"Standard14.{spec.Font.Standard14Face}";

        List<string> properties = [$"FontRef = {fontRefExpr}", $"FontSize = {Num(spec.FontSize)}"];

        if (spec.Leading is { } leading)
        {
            properties.Add($"Leading = {Num(leading)}");
        }

        if (!IsColorBitwiseIdentical(spec.Color, ColorRgb.Black))
        {
            properties.Add($"Color = {EmitColor(spec.Color)}");
        }

        if (spec.LinkUri is not null)
        {
            properties.Add($"LinkUri = {Literal(spec.LinkUri)}");
        }

        return $"new TextStyle {{ {string.Join(", ", properties)} }}";
    }

    private static string EmitEdgeInsets(EdgeInsets insets) =>
        IsBitwiseIdentical(insets.Top, insets.Right) && IsBitwiseIdentical(insets.Right, insets.Bottom) && IsBitwiseIdentical(insets.Bottom, insets.Left)
            ? $"new EdgeInsets({Num(insets.Top)})"
            : $"new EdgeInsets({Num(insets.Top)}, {Num(insets.Right)}, {Num(insets.Bottom)}, {Num(insets.Left)})";

    private static string EmitColor(ColorRgb color) =>
        $"new ColorRgb({Num(color.R)}, {Num(color.G)}, {Num(color.B)})";

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> are BIT-IDENTICAL,
    /// distinguishing positive from negative zero where <see cref="double.Equals(double)"/>,
    /// and the <c>==</c> operator it defers to for a non-NaN operand, do not:
    /// <c>(-0.0).Equals(0.0)</c> and <c>-0.0 == 0.0</c> are both <see langword="true"/>.
    /// Deciding what to OMIT or COLLAPSE in the emitted C# on that equality would
    /// let a stored negative zero be treated as identical to a positive-zero
    /// default it is not, which is exactly the omission half of the divergence
    /// <see cref="Num"/>'s own remark describes; the formatting half is fixed
    /// there. No NaN case is needed here: every double this method compares has
    /// already passed <see cref="SpecLimits.ValidateFinite"/> or one of its
    /// callers, so NaN never reaches it.
    /// </summary>
    private static bool IsBitwiseIdentical(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

    /// <summary>Component-wise <see cref="IsBitwiseIdentical(double, double)"/> for a <see cref="ColorRgb"/>, used wherever a colour is compared against a default to decide whether to omit it from the emitted C#.</summary>
    private static bool IsColorBitwiseIdentical(ColorRgb a, ColorRgb b) =>
        IsBitwiseIdentical(a.R, b.R) && IsBitwiseIdentical(a.G, b.G) && IsBitwiseIdentical(a.B, b.B);

    /// <summary>
    /// Renders <paramref name="value"/> as a C# <see langword="double"/>
    /// literal. <see cref="double.ToString(IFormatProvider?)"/> writes an
    /// integer-valued double with no decimal point or exponent, for instance
    /// <c>5</c> for <c>5.0</c>; that text is read back as the INTEGER literal
    /// <c>5</c>, which implicitly converts to <c>5.0</c> without incident,
    /// because a positive (or ordinarily negative) integer has only one
    /// floating-point value to become. Negative zero does not: <c>value.ToString()</c>
    /// on <c>-0.0</c> is the text <c>-0</c>, which C# parses as unary minus
    /// applied to the INTEGER literal <c>0</c>, and negating the integer zero
    /// stays the integer zero, so the implicit conversion that follows produces
    /// POSITIVE zero, silently losing the sign <see cref="double.IsNegative(double)"/>
    /// reports on the stored value. MEASURED: before this fix, a
    /// <see cref="Model.LineSeparatorSpec.LineWidth"/> of <c>-0.0</c> emitted
    /// the text <c>LineWidth = -0,</c>, which evaluates to <c>+0.0</c>. This is
    /// avoided by writing negative zero as the explicit double literal
    /// <c>-0.0</c> instead, which negates the double literal <c>0.0</c> rather
    /// than an integer one, and so evaluates to <c>-0.0</c> under IEEE 754
    /// arithmetic; <c>NegativeZeroFormattingTests</c> in the test project
    /// proves this via Roslyn rather than assuming it. Every other double this
    /// model stores is unaffected: no other value's <see cref="double.ToString(IFormatProvider?)"/>
    /// text is ambiguous between an integer and a floating-point literal.
    /// </summary>
    private static string Num(double value) =>
        value == 0 && double.IsNegative(value) ? "-0.0" : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders <paramref name="value"/> as a C# string literal. Beyond the
    /// quote, backslash and the eight named escapes, every remaining
    /// character in the C0 control range (U+0000-U+001F) and the three
    /// additional characters the C# lexer itself treats as a line terminator
    /// inside a string but that no named escape covers (U+0085 NEL, U+2028
    /// LINE SEPARATOR, U+2029 PARAGRAPH SEPARATOR) is escaped as <c>\uXXXX</c>.
    /// Left raw, any one of those three terminates the literal mid-string,
    /// producing a cascade of unrelated-looking compiler errors; U+2028 and
    /// U+2029 in particular survive an ordinary copy-paste from a word
    /// processor or a PDF with no malicious intent required.
    /// </summary>
    /// <remarks>
    /// This method walks UTF-16 code units, so it also escapes an unpaired
    /// (lone) surrogate rather than passing it through raw: a well-formed
    /// surrogate pair is left untouched, but a high surrogate with no
    /// following low surrogate, or a low surrogate with no preceding high
    /// surrogate, is not valid UTF-16 on its own and would not survive
    /// re-encoding the snippet as UTF-8 for display.
    /// </remarks>
    private static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];

            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(ch).Append(value[i + 1]);
                i++;
                continue;
            }

            builder.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when RequiresUnicodeEscape(ch) => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private static bool RequiresUnicodeEscape(char ch) =>
        ch <= '\u001f' || ch is '\u0085' or '\u2028' or '\u2029' || char.IsSurrogate(ch);

    /// <summary>A minimal indenting text builder, private to this emitter.</summary>
    private sealed class CodeWriter
    {
        private readonly StringBuilder _builder = new();
        private int _indentLevel;
        private bool _lastLineWasBlank;

        public void Line(string text = "")
        {
            if (text.Length == 0)
            {
                _builder.Append('\n');
                _lastLineWasBlank = true;
                return;
            }

            _builder.Append(' ', _indentLevel * 4).Append(text).Append('\n');
            _lastLineWasBlank = false;
        }

        /// <summary>
        /// Writes a blank line unless the previous one already was one, so two
        /// optional sections next to each other (or one next to the content
        /// loop's own trailing blank) never leave a doubled blank line behind.
        /// </summary>
        public void EnsureBlankLine()
        {
            if (!_lastLineWasBlank)
            {
                Line();
            }
        }

        public IndentScope Indent() => new(this);

        public override string ToString() => _builder.ToString();

        public readonly struct IndentScope : IDisposable
        {
            private readonly CodeWriter _writer;

            public IndentScope(CodeWriter writer)
            {
                _writer = writer;
                _writer._indentLevel++;
            }

            public void Dispose() => _writer._indentLevel--;
        }
    }
}
