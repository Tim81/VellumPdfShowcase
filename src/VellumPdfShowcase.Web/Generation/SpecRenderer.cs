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
/// for an unset optional spec field are the library's own defaults, read directly
/// off a default-constructed instance of the corresponding type, so leaving a
/// field unset in a <see cref="DocumentSpec"/> renders identically to leaving it
/// unset when calling the API by hand.
/// </remarks>
public static class SpecRenderer
{
    /// <summary>Builds the document described by <paramref name="spec"/> and returns its PDF bytes.</summary>
    public static byte[] Render(DocumentSpec spec)
    {
        using var document = new Document
        {
            PageSize = new VellumPdf.Document.PdfRectangle(0, 0, spec.Page.WidthPoints, spec.Page.HeightPoints),
            Margins = spec.Margins,
            Conformance = spec.Conformance,
            Tagged = spec.Tagged,
        };

        if (spec.Language is not null)
        {
            document.Language = spec.Language;
        }

        var embeddedFonts = spec.EmbeddedFonts.Select(document.UseTrueTypeFont).ToArray();

        document.SetDefaultFont(ToTextStyle(spec.DefaultTextStyle, embeddedFonts));

        if (spec.Metadata is { } metadata)
        {
            ApplyMetadata(document.Info, metadata);
        }

        foreach (var item in spec.Content)
        {
            AddContentItem(document, item, embeddedFonts);
        }

        if (spec.Header is { } header)
        {
            document.Header = BuildRunningBand(header, embeddedFonts);
        }

        if (spec.Footer is { } footer)
        {
            document.Footer = BuildRunningBand(footer, embeddedFonts);
        }

        switch (spec.OutputIntent)
        {
            case PdfAOutputIntentSpec pdfA:
                document.SetPdfAOutputIntent(pdfA.IccProfile, pdfA.ComponentCount, pdfA.OutputConditionIdentifier, pdfA.Info);
                break;
            case CmykOutputIntentSpec cmyk:
                document.UseCmykOutputIntent(cmyk.OutputConditionIdentifier);
                break;
            case null:
                break;
        }

        if (spec.Encryption is { } encryption)
        {
            document.Encrypt(new PdfEncryptionSettings
            {
                UserPassword = encryption.UserPassword,
                OwnerPassword = encryption.OwnerPassword,
                Permissions = encryption.Permissions,
                EncryptMetadata = encryption.EncryptMetadata,
            });
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
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

    private static void AddContentItem(Document document, ContentItemSpec item, IReadOnlyList<EmbeddedFontHandle> fonts)
    {
        switch (item)
        {
            case HeadingSpec heading:
                document.Add(BuildHeading(heading, fonts));
                break;
            case ParagraphSpec paragraph:
                document.Add(BuildParagraph(paragraph, fonts));
                break;
            case ListSpec list:
                document.Add(BuildList(list, fonts));
                break;
            case TableSpec table:
                document.Add(BuildTable(table, fonts));
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
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "Unrecognised content item type.");
        }
    }

    private static Heading BuildHeading(HeadingSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts) =>
        new(spec.Text, ToTextStyleOrNull(spec.Style, fonts))
        {
            Level = spec.Level,
            Alignment = spec.Alignment,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            BookmarkTitle = spec.BookmarkTitle,
            Language = spec.Language,
        };

    private static Paragraph BuildParagraph(ParagraphSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts)
    {
        var runs = spec.Runs.Select(run => new TextRun(run.Text, ToTextStyle(run.Style, fonts)));
        return new Paragraph(runs)
        {
            Alignment = spec.Alignment,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            Language = spec.Language,
        };
    }

    private static ListElement BuildList(ListSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts)
    {
        var list = new ListElement(spec.Style)
        {
            Indent = spec.Indent ?? 20,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            DefaultStyle = ToTextStyleOrNull(spec.DefaultStyle, fonts),
        };

        foreach (var item in spec.Items)
        {
            list.Add(BuildListItem(item, fonts));
        }

        return list;
    }

    private static ListItem BuildListItem(ListItemSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts)
    {
        var item = new ListItem(spec.Text, ToTextStyleOrNull(spec.Style, fonts))
        {
            Language = spec.Language,
        };

        foreach (var child in spec.Children)
        {
            item.AddChild(BuildListItem(child, fonts));
        }

        return item;
    }

    private static TableElement BuildTable(TableSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts)
    {
        var table = new TableElement
        {
            DefaultCellStyle = ToTextStyleOrNull(spec.DefaultCellStyle, fonts),
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
            var row = table.AddRow(rowSpec.IsHeader);

            foreach (var cellSpec in rowSpec.Cells)
            {
                row.AddCell(BuildCell(cellSpec, fonts));
            }
        }

        return table;
    }

    private static Cell BuildCell(TableCellSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts) =>
        new(spec.Content)
        {
            ColSpan = spec.ColSpan,
            RowSpan = spec.RowSpan,
            Style = ToTextStyleOrNull(spec.Style, fonts),
            Padding = spec.Padding ?? new EdgeInsets(4, 6, 4, 6),
            Background = spec.Background,
            Alignment = spec.Alignment,
            Language = spec.Language,
        };

    private static LayoutImage BuildImage(ImageSpec spec)
    {
        var xObject = spec.Format switch
        {
            ImageFormat.Png => PngImageLoader.Load(spec.Bytes),
            ImageFormat.Jpeg => JpegImageLoader.Load(spec.Bytes),
            ImageFormat.Bmp => BmpImageLoader.Load(spec.Bytes),
            ImageFormat.Gif => GifImageLoader.Load(spec.Bytes),
            ImageFormat.Tiff => TiffImageLoader.Load(spec.Bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Format, "Unrecognised image format."),
        };

        return new LayoutImage(xObject)
        {
            Width = spec.Width,
            Height = spec.Height,
            Alignment = spec.Alignment,
            Margins = spec.Margins ?? EdgeInsets.Zero,
            AltText = spec.AltText,
        };
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

    private static RunningBand BuildRunningBand(RunningBandSpec spec, IReadOnlyList<EmbeddedFontHandle> fonts)
    {
        var style = ToTextStyle(spec.Style, fonts);
        return spec.Height is { } height
            ? new RunningBand(spec.Template, style, spec.Alignment) { Height = height }
            : new RunningBand(spec.Template, style, spec.Alignment);
    }

    private static TextStyle ToTextStyle(TextStyleSpec spec, IReadOnlyList<EmbeddedFontHandle> embeddedFonts)
    {
        var fontRef = spec.Font.Kind switch
        {
            FontKind.Standard14 => new FontReference(spec.Font.Standard14Face),
            FontKind.Embedded => new FontReference(embeddedFonts[spec.Font.EmbeddedFontIndex]),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Font.Kind, "Unrecognised font kind."),
        };

        return new TextStyle
        {
            FontRef = fontRef,
            FontSize = spec.FontSize,
            Leading = spec.Leading ?? 0,
            Color = spec.Color,
            LinkUri = spec.LinkUri,
        };
    }

    private static TextStyle? ToTextStyleOrNull(TextStyleSpec? spec, IReadOnlyList<EmbeddedFontHandle> embeddedFonts) =>
        spec is null ? null : ToTextStyle(spec, embeddedFonts);
}
