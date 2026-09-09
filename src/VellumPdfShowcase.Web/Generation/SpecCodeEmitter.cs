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
    /// </remarks>
    public static string Emit(DocumentSpec spec)
    {
        spec.ValidateEmbeddedFontReferences();
        spec.ValidateContentFitsPageArea();

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
        private int _imageIndex;
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
        /// Names, in the register of plan section 13.3, whichever of the
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), item, "Unrecognised content item type.");
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

        private void EmitImage(ImageSpec imageSpec)
        {
            var loaderName = ImageLoaderName(imageSpec.Format);

            var imageVariable = $"image{_imageIndex}";
            writer.Line($"var {imageVariable} = {loaderName}.Load(Images[{_imageIndex}]);");
            _imageIndex++;

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

            if (!lineSeparatorSpec.Color.Equals(ColorRgb.Black))
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

    /// <summary>The Kernel loader type name for <paramref name="format"/>.</summary>
    /// <remarks>
    /// SECURITY / COVERAGE: the <c>default</c> arm is excluded from the
    /// branch-coverage gate (<c>eng/check-emitter-branch-coverage.ps1</c>)
    /// rather than removed. It is unreachable from the public API:
    /// <see cref="DocumentSpec.Content"/> rejects any <see cref="ImageSpec"/>
    /// whose declared <see cref="ImageSpec.Format"/> does not match its own
    /// byte signature, and <see cref="ImageSignature.Matches"/>, which performs
    /// that check, itself throws on any <see cref="ImageFormat"/> value
    /// outside the five named members before a <see cref="DocumentSpec"/>
    /// carrying one can ever be constructed. The arm cannot be deleted in its
    /// place: <see cref="ImageFormat"/> is a public enumeration C# cannot
    /// prove exhaustive from its five named members alone, so removing it
    /// turns CS8509 into a build failure under <c>TreatWarningsAsErrors</c>.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static string ImageLoaderName(ImageFormat format) => format switch
    {
        ImageFormat.Png => "PngImageLoader",
        ImageFormat.Jpeg => "JpegImageLoader",
        ImageFormat.Bmp => "BmpImageLoader",
        ImageFormat.Gif => "GifImageLoader",
        ImageFormat.Tiff => "TiffImageLoader",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognised image format."),
    };

    private static string EmitPieSlice(PieSlice slice) =>
        string.IsNullOrEmpty(slice.Label)
            ? $"new PieSlice({Num(slice.Value)}, {EmitColor(slice.Color)})"
            : $"new PieSlice({Num(slice.Value)}, {EmitColor(slice.Color)}, {Literal(slice.Label)})";

    private static string BuildTextStyleExpression(TextStyleSpec spec)
    {
        // FontReference has an implicit conversion from both Standard14 and
        // EmbeddedFontHandle, so FontRef can be assigned the face or handle
        // directly, without the otherwise-redundant `new FontReference(...)`.
        var fontRefExpr = spec.Font.Kind switch
        {
            FontKind.Standard14 => $"Standard14.{spec.Font.Standard14Face}",
            FontKind.Embedded => $"embeddedFont{spec.Font.EmbeddedFontIndex}",
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Font.Kind, "Unrecognised font kind."),
        };

        List<string> properties = [$"FontRef = {fontRefExpr}", $"FontSize = {Num(spec.FontSize)}"];

        if (spec.Leading is { } leading)
        {
            properties.Add($"Leading = {Num(leading)}");
        }

        if (!spec.Color.Equals(ColorRgb.Black))
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
        insets.Top.Equals(insets.Right) && insets.Right.Equals(insets.Bottom) && insets.Bottom.Equals(insets.Left)
            ? $"new EdgeInsets({Num(insets.Top)})"
            : $"new EdgeInsets({Num(insets.Top)}, {Num(insets.Right)}, {Num(insets.Bottom)}, {Num(insets.Left)})";

    private static string EmitColor(ColorRgb color) =>
        $"new ColorRgb({Num(color.R)}, {Num(color.G)}, {Num(color.B)})";

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

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
