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
/// rendered <c>byte[]</c>, and it assumes the three asset collections described
/// by <see cref="SpecAssets"/> (<c>EmbeddedFonts</c>, <c>Images</c>,
/// <c>IccProfile</c>) are already in scope under those names, exactly as a page
/// that fetched them once through <c>HttpClient</c> would have them.
/// </summary>
/// <remarks>
/// This type and <see cref="SpecRenderer"/> both read only the
/// <see cref="DocumentSpec"/> passed to them. Nothing here calls
/// <see cref="SpecRenderer"/> or vice versa; the test project's Roslyn
/// round-trip test is what keeps the two from silently drifting apart.
/// </remarks>
public static class SpecCodeEmitter
{
    /// <summary>Produces the C# snippet that builds the document described by <paramref name="spec"/>.</summary>
    /// <remarks>
    /// The classic <c>using (...) { }</c> statement is used in place of the
    /// newer <c>using var</c> declaration. Both are equally idiomatic modern
    /// C#, but only the block form is accepted by the Roslyn scripting host
    /// the round-trip test compiles this snippet with; a script's top level is
    /// not a method body, and the compiler rejects a using declaration there.
    /// </remarks>
    public static string Emit(DocumentSpec spec)
    {
        var writer = new CodeWriter();

        EmitUsings(spec, writer);
        writer.Line();

        writer.Line("using (var document = new Document");
        writer.Line("{");
        using (writer.Indent())
        {
            foreach (var initializer in BuildDocumentInitializers(spec))
            {
                writer.Line(initializer);
            }
        }

        writer.Line("})");
        writer.Line("{");
        using (writer.Indent())
        {
            EmitEmbeddedFonts(spec, writer);
            writer.Line($"document.SetDefaultFont({EmitTextStyleExpression(spec.DefaultTextStyle)});");
            writer.Line();
            EmitMetadata(spec, writer);

            var imageIndex = 0;
            foreach (var item in spec.Content)
            {
                EmitContentItem(item, writer, ref imageIndex);
                writer.Line();
            }

            if (spec.Header is { } header)
            {
                EmitRunningBand("Header", header, writer);
            }

            if (spec.Footer is { } footer)
            {
                EmitRunningBand("Footer", footer, writer);
            }

            EmitOutputIntent(spec, writer);
            EmitEncryption(spec, writer);

            writer.Line();
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

        return writer.ToString();
    }

    private static void EmitUsings(DocumentSpec spec, CodeWriter writer)
    {
        writer.Line("using System;");
        writer.Line("using System.IO;");
        writer.Line("using VellumPdf.Fonts;");
        writer.Line("using VellumPdf.Layout;");
        writer.Line("using VellumPdf.Layout.Core;");
        writer.Line("using VellumPdf.Layout.Elements;");
        writer.Line("using VellumPdf.Layout.Elements.Table;");

        if (spec.Content.OfType<ImageSpec>().Any())
        {
            writer.Line("using VellumPdf.Images;");
        }

        if (spec.Conformance != DocumentConformance.None)
        {
            writer.Line("using DocumentConformance = VellumPdf.Document.PdfConformance;");
        }

        if (spec.Encryption is not null)
        {
            writer.Line("using VellumPdf.Encryption;");
        }
    }

    private static List<string> BuildDocumentInitializers(DocumentSpec spec)
    {
        var initializers = new List<string>
        {
            $"PageSize = new VellumPdf.Document.PdfRectangle(0, 0, {Num(spec.Page.WidthPoints)}, {Num(spec.Page.HeightPoints)}),",
        };

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

        if (spec.Language is not null)
        {
            initializers.Add($"Language = {Literal(spec.Language)},");
        }

        return initializers;
    }

    private static void EmitEmbeddedFonts(DocumentSpec spec, CodeWriter writer)
    {
        for (var i = 0; i < spec.EmbeddedFonts.Count; i++)
        {
            writer.Line($"var embeddedFont{i} = document.UseTrueTypeFont(EmbeddedFonts[{i}]);");
        }

        if (spec.EmbeddedFonts.Count > 0)
        {
            writer.Line();
        }
    }

    private static void EmitMetadata(DocumentSpec spec, CodeWriter writer)
    {
        if (spec.Metadata is not { } metadata)
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

    private static void EmitContentItem(ContentItemSpec item, CodeWriter writer, ref int imageIndex)
    {
        switch (item)
        {
            case HeadingSpec heading:
                EmitHeading(heading, writer);
                break;
            case ParagraphSpec paragraph:
                EmitParagraph(paragraph, writer);
                break;
            case ListSpec list:
                EmitList(list, writer);
                break;
            case TableSpec table:
                EmitTable(table, writer);
                break;
            case ImageSpec image:
                EmitImage(image, writer, imageIndex);
                imageIndex++;
                break;
            case PieChartSpec pieChart:
                EmitPieChart(pieChart, writer);
                break;
            case LineSeparatorSpec lineSeparator:
                EmitLineSeparator(lineSeparator, writer);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "Unrecognised content item type.");
        }
    }

    private static void EmitHeading(HeadingSpec spec, CodeWriter writer)
    {
        var styleExpr = spec.Style is null ? "null" : EmitTextStyleExpression(spec.Style);
        var initializers = new List<string> { $"Level = {spec.Level}" };

        if (spec.Alignment != HorizontalAlignment.Left)
        {
            initializers.Add($"Alignment = HorizontalAlignment.{spec.Alignment}");
        }

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        if (spec.BookmarkTitle is not null)
        {
            initializers.Add($"BookmarkTitle = {Literal(spec.BookmarkTitle)}");
        }

        if (spec.Language is not null)
        {
            initializers.Add($"Language = {Literal(spec.Language)}");
        }

        EmitAdd($"new Heading({Literal(spec.Text)}, {styleExpr})", initializers, writer);
    }

    private static void EmitParagraph(ParagraphSpec spec, CodeWriter writer)
    {
        var initializers = new List<string>();

        if (spec.Alignment != HorizontalAlignment.Left)
        {
            initializers.Add($"Alignment = HorizontalAlignment.{spec.Alignment}");
        }

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        if (spec.Language is not null)
        {
            initializers.Add($"Language = {Literal(spec.Language)}");
        }

        if (spec.Runs.Count == 1)
        {
            var run = spec.Runs[0];
            EmitAdd($"new Paragraph({Literal(run.Text)}, {EmitTextStyleExpression(run.Style)})", initializers, writer);
            return;
        }

        writer.Line("var runs = new TextRun[]");
        writer.Line("{");
        using (writer.Indent())
        {
            foreach (var run in spec.Runs)
            {
                writer.Line($"new TextRun({Literal(run.Text)}, {EmitTextStyleExpression(run.Style)}),");
            }
        }

        writer.Line("};");
        EmitAdd("new Paragraph(runs)", initializers, writer);
    }

    // ListElement, ListItem, TableElement and Cell all expose their settable
    // members as `init`, which the language only allows to be assigned inside
    // the object-initializer expression that constructs the instance. The
    // emitted code below therefore never assigns into one of these after
    // declaring it; every optional property is folded into the same
    // declaration through EmitDeclaration.
    private static void EmitList(ListSpec spec, CodeWriter writer)
    {
        var initializers = new List<string>();

        if (spec.Indent is { } indent)
        {
            initializers.Add($"Indent = {Num(indent)}");
        }

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        if (spec.DefaultStyle is not null)
        {
            initializers.Add($"DefaultStyle = {EmitTextStyleExpression(spec.DefaultStyle)}");
        }

        EmitDeclaration("list", $"new ListElement(ListStyle.{spec.Style})", initializers, writer);

        for (var i = 0; i < spec.Items.Count; i++)
        {
            var itemVariable = EmitListItem(spec.Items[i], $"listItem{i}", writer);
            writer.Line($"list.Add({itemVariable});");
        }

        writer.Line("document.Add(list);");
    }

    private static string EmitListItem(ListItemSpec spec, string variableName, CodeWriter writer)
    {
        var styleExpr = spec.Style is null ? "null" : EmitTextStyleExpression(spec.Style);
        var initializers = new List<string>();

        if (spec.Language is not null)
        {
            initializers.Add($"Language = {Literal(spec.Language)}");
        }

        EmitDeclaration(variableName, $"new ListItem({Literal(spec.Text)}, {styleExpr})", initializers, writer);

        for (var i = 0; i < spec.Children.Count; i++)
        {
            var childVariable = EmitListItem(spec.Children[i], $"{variableName}Child{i}", writer);
            writer.Line($"{variableName}.AddChild({childVariable});");
        }

        return variableName;
    }

    private static void EmitTable(TableSpec spec, CodeWriter writer)
    {
        var initializers = new List<string>();

        if (spec.DefaultCellStyle is not null)
        {
            initializers.Add($"DefaultCellStyle = {EmitTextStyleExpression(spec.DefaultCellStyle)}");
        }

        if (spec.BorderWidth is { } borderWidth)
        {
            initializers.Add($"BorderWidth = {Num(borderWidth)}");
        }

        if (spec.BorderColor is { } borderColor)
        {
            initializers.Add($"BorderColor = {EmitColor(borderColor)}");
        }

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        EmitDeclaration("table", "new TableElement()", initializers, writer);

        if (spec.ColumnWidths is { Count: > 0 } widths)
        {
            writer.Line($"table.SetColumnWidths([{string.Join(", ", widths.Select(Num))}]);");
        }

        for (var rowIndex = 0; rowIndex < spec.Rows.Count; rowIndex++)
        {
            var row = spec.Rows[rowIndex];
            var rowVariable = $"row{rowIndex}";
            writer.Line($"var {rowVariable} = table.AddRow({(row.IsHeader ? "true" : "false")});");

            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                EmitCell(row.Cells[cellIndex], rowVariable, $"{rowVariable}Cell{cellIndex}", writer);
            }
        }

        writer.Line("document.Add(table);");
    }

    private static void EmitCell(TableCellSpec spec, string rowVariable, string cellVariable, CodeWriter writer)
    {
        var initializers = new List<string>();

        if (spec.ColSpan != 1)
        {
            initializers.Add($"ColSpan = {spec.ColSpan}");
        }

        if (spec.RowSpan != 1)
        {
            initializers.Add($"RowSpan = {spec.RowSpan}");
        }

        if (spec.Style is not null)
        {
            initializers.Add($"Style = {EmitTextStyleExpression(spec.Style)}");
        }

        if (spec.Padding is { } padding)
        {
            initializers.Add($"Padding = {EmitEdgeInsets(padding)}");
        }

        if (spec.Background is { } background)
        {
            initializers.Add($"Background = {EmitColor(background)}");
        }

        if (spec.Alignment != HorizontalAlignment.Left)
        {
            initializers.Add($"Alignment = HorizontalAlignment.{spec.Alignment}");
        }

        if (spec.Language is not null)
        {
            initializers.Add($"Language = {Literal(spec.Language)}");
        }

        EmitDeclaration(cellVariable, $"new Cell({Literal(spec.Content)})", initializers, writer);
        writer.Line($"{rowVariable}.AddCell({cellVariable});");
    }

    private static void EmitImage(ImageSpec spec, CodeWriter writer, int imageIndex)
    {
        var loaderName = spec.Format switch
        {
            ImageFormat.Png => "PngImageLoader",
            ImageFormat.Jpeg => "JpegImageLoader",
            ImageFormat.Bmp => "BmpImageLoader",
            ImageFormat.Gif => "GifImageLoader",
            ImageFormat.Tiff => "TiffImageLoader",
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Format, "Unrecognised image format."),
        };

        var imageVariable = $"image{imageIndex}";
        writer.Line($"var {imageVariable} = {loaderName}.Load(Images[{imageIndex}]);");

        var initializers = new List<string>();

        if (spec.Width is { } width)
        {
            initializers.Add($"Width = {Num(width)}");
        }

        if (spec.Height is { } height)
        {
            initializers.Add($"Height = {Num(height)}");
        }

        if (spec.Alignment != HorizontalAlignment.Left)
        {
            initializers.Add($"Alignment = HorizontalAlignment.{spec.Alignment}");
        }

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        if (spec.AltText is not null)
        {
            initializers.Add($"AltText = {Literal(spec.AltText)}");
        }

        EmitAdd($"new LayoutImage({imageVariable})", initializers, writer);
    }

    private static void EmitPieChart(PieChartSpec spec, CodeWriter writer)
    {
        var initializers = new List<string>
        {
            $"Slices = [{string.Join(", ", spec.Slices.Select(EmitPieSlice))}]",
            $"Diameter = {Num(spec.Diameter)}",
        };

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        if (spec.StrokeColor is { } strokeColor)
        {
            initializers.Add($"StrokeColor = {EmitColor(strokeColor)}");
        }

        if (spec.StrokeWidth != 0.5)
        {
            initializers.Add($"StrokeWidth = {Num(spec.StrokeWidth)}");
        }

        if (spec.Alignment != HorizontalAlignment.Center)
        {
            initializers.Add($"Alignment = HorizontalAlignment.{spec.Alignment}");
        }

        if (spec.StartAngle != double.Pi / 2)
        {
            initializers.Add($"StartAngle = {Num(spec.StartAngle)}");
        }

        if (!spec.Clockwise)
        {
            initializers.Add("Clockwise = false");
        }

        if (spec.AltText is not null)
        {
            initializers.Add($"AltText = {Literal(spec.AltText)}");
        }

        if (spec.Decorative)
        {
            initializers.Add("Decorative = true");
        }

        EmitAdd("new PieChart()", initializers, writer);
    }

    private static string EmitPieSlice(PieSlice slice) =>
        string.IsNullOrEmpty(slice.Label)
            ? $"new PieSlice({Num(slice.Value)}, {EmitColor(slice.Color)})"
            : $"new PieSlice({Num(slice.Value)}, {EmitColor(slice.Color)}, {Literal(slice.Label)})";

    private static void EmitLineSeparator(LineSeparatorSpec spec, CodeWriter writer)
    {
        var initializers = new List<string>();

        if (spec.LineWidth != 1)
        {
            initializers.Add($"LineWidth = {Num(spec.LineWidth)}");
        }

        if (!spec.Color.Equals(ColorRgb.Black))
        {
            initializers.Add($"Color = {EmitColor(spec.Color)}");
        }

        if (spec.Margins is { } margins)
        {
            initializers.Add($"Margins = {EmitEdgeInsets(margins)}");
        }

        EmitAdd("new LineSeparator()", initializers, writer);
    }

    private static void EmitRunningBand(string kind, RunningBandSpec spec, CodeWriter writer)
    {
        var ctorExpr = $"new RunningBand({Literal(spec.Template)}, {EmitTextStyleExpression(spec.Style)}, HorizontalAlignment.{spec.Alignment})";
        var initializers = spec.Height is { } height ? new List<string> { $"Height = {Num(height)}" } : [];
        EmitAssignment($"document.{kind}", ctorExpr, initializers, writer);
    }

    private static void EmitOutputIntent(DocumentSpec spec, CodeWriter writer)
    {
        switch (spec.OutputIntent)
        {
            case PdfAOutputIntentSpec pdfA:
                writer.Line($"document.SetPdfAOutputIntent(IccProfile!, {pdfA.ComponentCount}, {Literal(pdfA.OutputConditionIdentifier)}, {(pdfA.Info is null ? "null" : Literal(pdfA.Info))});");
                break;
            case CmykOutputIntentSpec cmyk:
                writer.Line($"document.UseCmykOutputIntent({Literal(cmyk.OutputConditionIdentifier)});");
                break;
        }
    }

    private static void EmitEncryption(DocumentSpec spec, CodeWriter writer)
    {
        if (spec.Encryption is not { } encryption)
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

            writer.Line($"Permissions = {EmitPermissions(encryption.Permissions)},");

            if (!encryption.EncryptMetadata)
            {
                writer.Line("EncryptMetadata = false,");
            }
        }

        writer.Line("});");
    }

    private static string EmitPermissions(PdfPermissions permissions)
    {
        if (permissions == PdfPermissions.All)
        {
            return "PdfPermissions.All";
        }

        if (permissions == PdfPermissions.None)
        {
            return "PdfPermissions.None";
        }

        var flags = Enum.GetValues<PdfPermissions>()
            .Where(flag => flag is not (PdfPermissions.None or PdfPermissions.All) && permissions.HasFlag(flag))
            .Select(flag => $"PdfPermissions.{flag}");

        return string.Join(" | ", flags);
    }

    private static string EmitTextStyleExpression(TextStyleSpec spec)
    {
        var fontRefExpr = spec.Font.Kind switch
        {
            FontKind.Standard14 => $"new FontReference(Standard14.{spec.Font.Standard14Face})",
            FontKind.Embedded => $"new FontReference(embeddedFont{spec.Font.EmbeddedFontIndex})",
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Font.Kind, "Unrecognised font kind."),
        };

        var properties = new List<string> { $"FontRef = {fontRefExpr}", $"FontSize = {Num(spec.FontSize)}" };

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

    private static void EmitAdd(string ctorExpr, IReadOnlyList<string> initializers, CodeWriter writer) =>
        EmitWrapped("document.Add(", ")", ctorExpr, initializers, writer);

    /// <summary>Emits <c>var {variableName} = {ctorExpr}{ initializers };</c>, folding every optional property into the one declaration an init-only type requires.</summary>
    private static void EmitDeclaration(string variableName, string ctorExpr, IReadOnlyList<string> initializers, CodeWriter writer) =>
        EmitWrapped($"var {variableName} = ", "", ctorExpr, initializers, writer);

    /// <summary>Emits <c>{target} = {ctorExpr}{ initializers };</c>, for a mutable property (such as <c>Document.Header</c>) assigned an init-only-typed value.</summary>
    private static void EmitAssignment(string target, string ctorExpr, IReadOnlyList<string> initializers, CodeWriter writer) =>
        EmitWrapped($"{target} = ", "", ctorExpr, initializers, writer);

    private static void EmitWrapped(string prefix, string suffix, string ctorExpr, IReadOnlyList<string> initializers, CodeWriter writer)
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

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');

        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    /// <summary>A minimal indenting text builder, private to this emitter.</summary>
    private sealed class CodeWriter
    {
        private readonly StringBuilder _builder = new();
        private int _indentLevel;

        public void Line(string text = "")
        {
            if (text.Length == 0)
            {
                _builder.Append('\n');
                return;
            }

            _builder.Append(' ', _indentLevel * 4).Append(text).Append('\n');
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
