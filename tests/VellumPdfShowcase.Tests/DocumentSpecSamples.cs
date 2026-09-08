using VellumPdf.Encryption;
using VellumPdf.Fonts;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Model;
using DocumentConformance = VellumPdf.Document.PdfConformance;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// <see cref="DocumentSpec"/> instances used by the round-trip tests. Each
/// exercises a different combination of the model so that, between them, every
/// content item type and every optional feature in plan section 6.2 is
/// covered at least once.
/// </summary>
internal static class DocumentSpecSamples
{
    /// <summary>A 1x1 transparent PNG, the smallest input every Kernel image loader accepts unambiguously.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    /// <summary>A 1x1, 32-bit uncompressed BMP, used to exercise an image loader other than <see cref="ImageFormat.Png"/>.</summary>
    private static readonly byte[] OnePixelBmp = Convert.FromBase64String(
        "Qk06AAAAAAAAADYAAAAoAAAAAQAAAAEAAAABACAAAAAAAAAAAADEDgAAxA4AAAAAAAAAAAAAHhQK/w==");

    private static byte[] ReadTestAsset(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", fileName));

    public static byte[] LiberationSansBytes() => ReadTestAsset("LiberationSans-Regular.ttf");

    public static byte[] SrgbIccProfileBytes() => ReadTestAsset("sRGB2014.icc");

    /// <summary>
    /// Every content item type, with every optional property set to a
    /// non-default value: mixed-style inline paragraph runs, a nested list, a
    /// spanning table cell with a background, an embedded font, a running
    /// header and footer, tagged output, document and per-element language,
    /// and document metadata.
    /// </summary>
    public static DocumentSpec EveryContentItemTypeFullyCustomised()
    {
        var bodyStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };
        var embeddedHeadingStyle = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 18 };
        var linkStyle = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.HelveticaOblique),
            FontSize = 11,
            Color = new ColorRgb(0.1, 0.1, 0.8),
            LinkUri = "https://example.com",
        };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.Letter),
            Margins = new EdgeInsets(72),
            DefaultTextStyle = bodyStyle,
            EmbeddedFonts = [LiberationSansBytes()],
            Tagged = true,
            Language = "en",
            Metadata = new DocumentMetadataSpec
            {
                Title = "Round-trip sample",
                Author = "VellumPdf Showcase",
                Subject = "Testing",
                Keywords = "test,roundtrip",
                Creator = "VellumPdfShowcase.Tests",
                Producer = "VellumPdf",
            },
            Header = new RunningBandSpec { Template = "VellumPdf Showcase", Style = bodyStyle },
            Footer = new RunningBandSpec { Template = "Page {page} of {pages}", Style = bodyStyle, Height = 30 },
            Content =
            [
                new HeadingSpec
                {
                    Text = "Capability round trip",
                    Level = 1,
                    Style = embeddedHeadingStyle,
                    BookmarkTitle = "Round trip",
                    Language = "en",
                },
                ParagraphSpec.FromText("A single-style paragraph.", bodyStyle),
                new ParagraphSpec
                {
                    Runs =
                    [
                        new TextRunSpec("Mixed ", bodyStyle),
                        new TextRunSpec("styles ", embeddedHeadingStyle),
                        new TextRunSpec("and a link.", linkStyle),
                    ],
                },
                new ListSpec
                {
                    Style = ListStyle.OrderedDecimal,
                    Items =
                    [
                        new ListItemSpec { Text = "First" },
                        new ListItemSpec
                        {
                            Text = "Second",
                            Children = [new ListItemSpec { Text = "Nested", Language = "en" }],
                        },
                    ],
                },
                new TableSpec
                {
                    ColumnWidths = [200, 100],
                    BorderColor = new ColorRgb(0.4, 0.4, 0.4),
                    Rows =
                    [
                        new TableRowSpec
                        {
                            IsHeader = true,
                            Cells = [new TableCellSpec { Content = "Name" }, new TableCellSpec { Content = "Value" }],
                        },
                        new TableRowSpec
                        {
                            Cells =
                            [
                                new TableCellSpec
                                {
                                    Content = "Spanning",
                                    ColSpan = 2,
                                    Background = new ColorRgb(0.9, 0.9, 0.9),
                                },
                            ],
                        },
                    ],
                },
                new ImageSpec
                {
                    Format = ImageFormat.Png,
                    Bytes = OnePixelPng,
                    Width = 40,
                    AltText = "A single test pixel.",
                },
                new PieChartSpec
                {
                    Diameter = 120,
                    StartAngle = 0,
                    Clockwise = false,
                    Slices =
                    [
                        new PieSlice(60, new ColorRgb(0.2, 0.4, 0.8), "A"),
                        new PieSlice(40, new ColorRgb(0.8, 0.4, 0.2), "B"),
                    ],
                },
                new LineSeparatorSpec { LineWidth = 2, Color = new ColorRgb(0.5, 0.5, 0.5) },
            ],
        };
    }

    /// <summary>
    /// Every content item type again, but with every optional property left
    /// unset, including the page size, which is why this is the one sample
    /// that uses <c>PageSize.A4</c>, <c>Document</c>'s own default: it exercises
    /// the fallback value each builder applies for an omitted field.
    /// <see cref="Generation.SpecRenderer"/> must fall back to the same value
    /// the library itself defaults to, or this spec would render one way and
    /// the code <see cref="Generation.SpecCodeEmitter"/> emits for it, which
    /// omits the same unset properties rather than spelling out the library's
    /// default, would render another.
    /// </summary>
    public static DocumentSpec EveryContentItemTypeAtDefault()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            DefaultTextStyle = style,
            Content =
            [
                new HeadingSpec { Text = "Defaults", Level = 2 },
                ParagraphSpec.FromText("Paragraph with every optional field left at its default.", style),
                new ListSpec
                {
                    Style = ListStyle.Unordered,
                    Items = [new ListItemSpec { Text = "One" }],
                },
                new TableSpec
                {
                    Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "Cell" }] }],
                },
                new ImageSpec { Format = ImageFormat.Png, Bytes = OnePixelPng },
                new PieChartSpec
                {
                    Diameter = 100,
                    Slices = [new PieSlice(1, ColorRgb.Black)],
                },
                new LineSeparatorSpec(),
            ],
        };
    }

    /// <summary>A PDF/A-2b claim with a fully embedded face and an explicit sRGB output intent, as section 6.2 requires for any conformance sample.</summary>
    public static DocumentSpec PdfA2bWithOutputIntent()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.Legal),
            Margins = new EdgeInsets(72),
            DefaultTextStyle = style,
            EmbeddedFonts = [LiberationSansBytes()],
            Conformance = DocumentConformance.PdfA2b,
            Tagged = true,
            Language = "en",
            Content =
            [
                new HeadingSpec { Text = "PDF/A-2b sample", Level = 1, Style = style, Language = "en" },
                ParagraphSpec.FromText("Embeds a Liberation Sans face and declares an sRGB output intent.", style),
            ],
            OutputIntent = new PdfAOutputIntentSpec
            {
                IccProfile = SrgbIccProfileBytes(),
                ComponentCount = 3,
                OutputConditionIdentifier = "sRGB IEC61966-2.1",
            },
        };
    }

    /// <summary>AES-256 encryption with a distinct user and owner password and a restricted permission set.</summary>
    public static DocumentSpec Encrypted()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.Ledger),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText("Encrypted, no conformance claim.", style)],
            Encryption = new EncryptionSpec
            {
                UserPassword = "user-secret",
                OwnerPassword = "owner-secret",
                Permissions = PdfPermissions.Print | PdfPermissions.Copy,
                EncryptMetadata = false,
            },
        };
    }

    /// <summary>
    /// Two of every element whose emitted code declares a fixed-name local
    /// variable: two lists (in fact all four list styles, since plan section
    /// 6.2 requires unordered, decimal, alpha and roman in one document),
    /// two tables and two multi-run paragraphs. This is what would have
    /// caught S4-H1: the Roslyn round-trip test compiles the emitted code,
    /// and a second <c>list</c>, <c>table</c> or <c>runs</c> sharing the name
    /// of the first is a compile error the moment such a sample exists. Also
    /// covers asymmetric document margins and cell padding, a non-default
    /// alignment on a heading, a paragraph and a cell, and an image format
    /// other than PNG.
    /// </summary>
    public static DocumentSpec MultipleListsTablesAndParagraphs()
    {
        var bodyStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };
        var emphasisStyle = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.HelveticaBoldOblique),
            FontSize = 11,
            Color = new ColorRgb(0.6, 0.1, 0.1),
        };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(500, 700),
            Margins = new EdgeInsets(50, 60, 40, 30),
            DefaultTextStyle = bodyStyle,
            Content =
            [
                new HeadingSpec { Text = "Repeated content types", Level = 1, Alignment = HorizontalAlignment.Right },
                new ParagraphSpec
                {
                    Alignment = HorizontalAlignment.Justify,
                    Runs =
                    [
                        new TextRunSpec("First multi-run paragraph, ", bodyStyle),
                        new TextRunSpec("with an emphasised second run.", emphasisStyle),
                    ],
                },
                new ParagraphSpec
                {
                    Runs =
                    [
                        new TextRunSpec("Second multi-run paragraph, ", bodyStyle),
                        new TextRunSpec("also with two runs.", emphasisStyle),
                    ],
                },
                new ListSpec
                {
                    Style = ListStyle.Unordered,
                    Items = [new ListItemSpec { Text = "Unordered first" }, new ListItemSpec { Text = "Unordered second" }],
                },
                new ListSpec
                {
                    Style = ListStyle.OrderedDecimal,
                    Items = [new ListItemSpec { Text = "Decimal first" }, new ListItemSpec { Text = "Decimal second" }],
                },
                new ListSpec
                {
                    Style = ListStyle.OrderedAlpha,
                    Items = [new ListItemSpec { Text = "Alpha first" }, new ListItemSpec { Text = "Alpha second" }],
                },
                new ListSpec
                {
                    Style = ListStyle.OrderedRoman,
                    Items = [new ListItemSpec { Text = "Roman first" }, new ListItemSpec { Text = "Roman second" }],
                },
                new TableSpec
                {
                    Rows =
                    [
                        new TableRowSpec
                        {
                            IsHeader = true,
                            Cells = [new TableCellSpec { Content = "First table" }, new TableCellSpec { Content = "Column two" }],
                        },
                        new TableRowSpec
                        {
                            Cells = [new TableCellSpec { Content = "A" }, new TableCellSpec { Content = "B" }],
                        },
                    ],
                },
                new TableSpec
                {
                    ColumnWidths = [150, 150],
                    Rows =
                    [
                        new TableRowSpec
                        {
                            IsHeader = true,
                            Cells = [new TableCellSpec { Content = "Second table" }, new TableCellSpec { Content = "Column two" }],
                        },
                        new TableRowSpec
                        {
                            Cells =
                            [
                                new TableCellSpec
                                {
                                    Content = "Padded and centred",
                                    Padding = new EdgeInsets(2, 4, 6, 8),
                                    Alignment = HorizontalAlignment.Center,
                                },
                                new TableCellSpec { Content = "Plain" },
                            ],
                        },
                    ],
                },
                new ImageSpec
                {
                    Format = ImageFormat.Bmp,
                    Bytes = OnePixelBmp,
                    Width = 20,
                    AltText = "A single test pixel, BMP-encoded.",
                },
            ],
        };
    }

    /// <summary>
    /// A PDF/A-2u claim, the conformance profile the round trip exercises
    /// alongside PDF/A-2b (plan section 6.2 lists all four PDF/A and PDF/UA
    /// profiles as available; PdfA2b is covered by <see cref="PdfA2bWithOutputIntent"/>).
    /// </summary>
    public static DocumentSpec PdfA2uWithOutputIntent()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A3),
            DefaultTextStyle = style,
            EmbeddedFonts = [LiberationSansBytes()],
            Conformance = DocumentConformance.PdfA2u,
            Tagged = true,
            Language = "en",
            Content =
            [
                new HeadingSpec { Text = "PDF/A-2u sample", Level = 1, Style = style, Language = "en" },
                ParagraphSpec.FromText("Embeds a Liberation Sans face and declares an sRGB output intent.", style),
            ],
            OutputIntent = new PdfAOutputIntentSpec
            {
                IccProfile = SrgbIccProfileBytes(),
                ComponentCount = 3,
                OutputConditionIdentifier = "sRGB IEC61966-2.1",
            },
        };
    }

    /// <summary>
    /// A string containing every character <see cref="Generation.SpecCodeEmitter"/>
    /// must escape beyond the four named escapes it already handled (quote,
    /// backslash, LF, CR, TAB): the whole C0 control range, plus NEL, LINE
    /// SEPARATOR and PARAGRAPH SEPARATOR, together with a literal quote,
    /// backslash and brace pair, so the round-trip test both compiles and
    /// executes the emitted string literal. See S4-H2.
    /// </summary>
    public static DocumentSpec ControlCharactersAndLineSeparators()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };
        var weird = "quote\" backslash\\ brace{}" +
            new string([.. Enumerable.Range(0, 0x20).Select(codePoint => (char)codePoint)]) +
            "\u0085\u2028\u2029end";

        return new DocumentSpec
        {
            Page = new PageSizeSpec(400, 400),
            DefaultTextStyle = style,
            Metadata = new DocumentMetadataSpec { Title = weird },
            Content = [ParagraphSpec.FromText(weird, style)],
        };
    }
}
