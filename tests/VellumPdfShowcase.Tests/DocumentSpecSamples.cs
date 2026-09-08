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

    /// <summary>A 1x1 red JPEG, used to exercise <see cref="ImageFormat.Jpeg"/>, one of the Kernel image loaders no other sample reaches.</summary>
    private static readonly byte[] OnePixelJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KKK+ZP3E//Z");

    /// <summary>A 1x1 red GIF, used to exercise <see cref="ImageFormat.Gif"/>.</summary>
    private static readonly byte[] OnePixelGif = Convert.FromBase64String(
        "R0lGODdhAQABAIEAAP8AAAAAAAAAAAAAACwAAAAAAQABAAAIBAABBAQAOw==");

    /// <summary>A 1x1 red, uncompressed, little-endian TIFF, used to exercise <see cref="ImageFormat.Tiff"/>.</summary>
    private static readonly byte[] OnePixelTiff = Convert.FromBase64String(
        "SUkqAAgAAAAKAAABBAABAAAAAQAAAAEBBAABAAAAAQAAAAIBAwADAAAAhgAAAAMBAwABAAAAAQAAAAYBAwABAAAAAgAAABEBBAABAAAAjAAAABUBAwABAAAAAwAAABYBBAABAAAAAQAAABcBBAABAAAAAwAAABwBAwABAAAAAQAAAAAAAAAIAAgACAD/AAA=");

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
    /// two tables and two multi-run paragraphs. The Roslyn round-trip test
    /// compiles the emitted code, and a second <c>list</c>, <c>table</c> or
    /// <c>runs</c> sharing the name of the first is a compile error the
    /// moment such a sample exists. Also
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
    /// executes the emitted string literal.
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

    /// <summary>
    /// Two two-run paragraphs. The first has both runs share one
    /// <see cref="TextStyleSpec"/> instance (the ordinary way to author "two
    /// runs, one style"); the second has two distinct instances that are
    /// value-equal but not reference-equal. <see cref="Generation.SpecRenderer"/>'s
    /// style cache and <see cref="Generation.SpecCodeEmitter"/>'s style
    /// hoisting both key on this record's value equality, so a style used
    /// twice, whether as one shared instance or two value-equal ones, must
    /// merge into the library's reference-identity run-merging identically on
    /// both sides. Both paragraphs must round-trip identically.
    /// </summary>
    public static DocumentSpec SharedAndValueEqualRunStyles()
    {
        var docDefault = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) };
        var sharedInstance = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.Helvetica),
            FontSize = 11,
            LinkUri = "https://example.com",
        };
        var valueEqualA = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.HelveticaBold), FontSize = 13 };
        var valueEqualB = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.HelveticaBold), FontSize = 13 };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(400, 300),
            DefaultTextStyle = docDefault,
            Content =
            [
                new ParagraphSpec
                {
                    Runs =
                    [
                        new TextRunSpec("Shared instance, ", sharedInstance),
                        new TextRunSpec("run two.", sharedInstance),
                    ],
                },
                new ParagraphSpec
                {
                    Runs =
                    [
                        new TextRunSpec("Value-equal, ", valueEqualA),
                        new TextRunSpec("distinct instances.", valueEqualB),
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// A pie chart with non-default <c>Alignment</c>,
    /// <c>StrokeColor</c> and <c>Decorative</c>; a list with
    /// <see cref="ListSpec.DefaultStyle"/>; a table with
    /// <see cref="TableSpec.DefaultCellStyle"/>; the JPEG loader; list
    /// nesting two levels deep; and one cell combining <c>ColSpan</c> and
    /// <c>RowSpan</c>.
    /// </summary>
    public static DocumentSpec AdditionalCoverage()
    {
        var bodyStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };
        var listDefaultStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.HelveticaOblique), FontSize = 10 };
        var cellDefaultStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.TimesRoman), FontSize = 10 };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(500, 700),
            DefaultTextStyle = bodyStyle,
            Content =
            [
                new PieChartSpec
                {
                    Diameter = 80,
                    Alignment = HorizontalAlignment.Right,
                    StrokeColor = new ColorRgb(0.2, 0.2, 0.2),
                    Decorative = true,
                    Slices = [new PieSlice(1, new ColorRgb(0.3, 0.5, 0.7))],
                },
                new ListSpec
                {
                    Style = ListStyle.Unordered,
                    DefaultStyle = listDefaultStyle,
                    Items =
                    [
                        new ListItemSpec
                        {
                            Text = "Level one",
                            Children =
                            [
                                new ListItemSpec
                                {
                                    Text = "Level two",
                                    Children = [new ListItemSpec { Text = "Level three" }],
                                },
                            ],
                        },
                    ],
                },
                new TableSpec
                {
                    DefaultCellStyle = cellDefaultStyle,
                    Rows =
                    [
                        new TableRowSpec
                        {
                            IsHeader = true,
                            Cells =
                            [
                                new TableCellSpec { Content = "Spans two columns and two rows", ColSpan = 2, RowSpan = 2 },
                                new TableCellSpec { Content = "Top right" },
                            ],
                        },
                        new TableRowSpec
                        {
                            Cells = [new TableCellSpec { Content = "Bottom right" }],
                        },
                    ],
                },
                new ImageSpec
                {
                    Format = ImageFormat.Jpeg,
                    Bytes = OnePixelJpeg,
                    Width = 20,
                    AltText = "A single test pixel, JPEG-encoded.",
                },
            ],
        };
    }

    /// <summary>
    /// A PDF/A-2a claim, the accessibility-conformant level of PDF/A-2,
    /// covering one of the four PDF/A and PDF/UA profiles plan section 6.2
    /// requires, alongside <see cref="PdfA2bWithOutputIntent"/>,
    /// <see cref="PdfA2uWithOutputIntent"/> and <see cref="PdfUA1WithOutputIntent"/>.
    /// </summary>
    public static DocumentSpec PdfA2aWithOutputIntent()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A5),
            DefaultTextStyle = style,
            EmbeddedFonts = [LiberationSansBytes()],
            Conformance = DocumentConformance.PdfA2a,
            Tagged = true,
            Language = "en",
            Content =
            [
                new HeadingSpec { Text = "PDF/A-2a sample", Level = 1, Style = style, Language = "en" },
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
    /// A PDF/UA-1 claim, the fourth and last of the four conformance profiles
    /// plan section 6.2 requires, alongside <see cref="PdfA2bWithOutputIntent"/>,
    /// <see cref="PdfA2uWithOutputIntent"/> and <see cref="PdfA2aWithOutputIntent"/>.
    /// Also sets <see cref="PdfAOutputIntentSpec.Info"/> explicitly, the one
    /// output intent member no other sample in this file sets.
    /// </summary>
    /// <remarks>
    /// Two members are load-bearing for actual PDF/UA-1 preflight compliance,
    /// found only once every conformant sample was preflighted structurally
    /// rather than at three hand-picked call sites (see
    /// <see cref="SpecRoundTripTests.SampleNamesClaimingConformance"/>).
    /// <see cref="Metadata"/>'s <see cref="DocumentMetadataSpec.Title"/> is
    /// required by ISO 14289-1:2014 clause 7.1 (a non-empty XMP
    /// <c>dc:title</c>). <see cref="HeadingSpec.Level"/> is zero-based per the
    /// library's own documentation ("0 = top-level, 1 = sub-heading, etc."),
    /// so the document's opening, and only, heading must be
    /// <see langword="0"/>; <see langword="1"/> tags it H2 with no preceding
    /// H1, which ISO 14289-1:2014 clause 7.4.2 rejects as a skipped level.
    /// PDF/A preflight does not check heading hierarchy at all, which is why
    /// every other sample in this file uses <see langword="1"/> for its sole
    /// heading without being caught.
    /// </remarks>
    public static DocumentSpec PdfUA1WithOutputIntent()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A6),
            DefaultTextStyle = style,
            EmbeddedFonts = [LiberationSansBytes()],
            Conformance = DocumentConformance.PdfUA1,
            Tagged = true,
            Language = "en",
            Metadata = new DocumentMetadataSpec { Title = "PDF/UA-1 sample" },
            Content =
            [
                new HeadingSpec { Text = "PDF/UA-1 sample", Level = 0, Style = style, Language = "en" },
                ParagraphSpec.FromText("Embeds a Liberation Sans face and declares an sRGB output intent.", style),
            ],
            OutputIntent = new PdfAOutputIntentSpec
            {
                IccProfile = SrgbIccProfileBytes(),
                ComponentCount = 3,
                OutputConditionIdentifier = "sRGB IEC61966-2.1",
                Info = "sRGB IEC61966-2.1 output profile",
            },
        };
    }

    /// <summary>
    /// AES-256 encryption using <see cref="EncryptionSpec"/>'s own defaults:
    /// full permissions and metadata encryption left on. Exercises
    /// <c>SpecCodeEmitter.EmitPermissions</c>'s <c>PdfPermissions.All</c> fast
    /// path and its omit-when-default <c>EncryptMetadata</c> branch, neither
    /// of which <see cref="Encrypted"/> reaches, since that sample restricts
    /// permissions and disables metadata encryption.
    /// </summary>
    public static DocumentSpec EncryptedWithDefaults()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.Letter),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText("Encrypted with default permissions and metadata encryption, no conformance claim.", style)],
            Encryption = new EncryptionSpec
            {
                UserPassword = "user-secret",
                OwnerPassword = "owner-secret",
            },
        };
    }

    /// <summary>
    /// C4-C-M5: an owner password with no user password. Anyone can open the
    /// file; only the owner password grants the restricted permissions.
    /// Exercises <c>SpecCodeEmitter.EmitEncryption</c>'s omit-when-null branch
    /// for <see cref="EncryptionSpec.UserPassword"/>, which no other sample
    /// reaches because every other encrypted sample sets both passwords.
    /// </summary>
    public static DocumentSpec EncryptedOwnerPasswordOnly()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.Letter),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText("Owner password only; anyone may open this file.", style)],
            Encryption = new EncryptionSpec
            {
                OwnerPassword = "owner-secret",
                Permissions = PdfPermissions.Print,
            },
        };
    }

    /// <summary>
    /// C4-C-M5: a user password with no owner password and unrestricted
    /// permissions, the one combination <see cref="EncryptionSpec"/> still
    /// allows a null <see cref="EncryptionSpec.OwnerPassword"/> for. Measured
    /// directly against the library and recorded on the type: with no owner
    /// password set, the password that actually authenticates full (owner)
    /// access is <see cref="EncryptionSpec.UserPassword"/> itself. Exercises
    /// <c>SpecCodeEmitter.EmitEncryption</c>'s omit-when-null branch for
    /// <see cref="EncryptionSpec.OwnerPassword"/>.
    /// </summary>
    public static DocumentSpec EncryptedNoOwnerPasswordUnrestricted()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.Letter),
            DefaultTextStyle = style,
            Content = [ParagraphSpec.FromText("User password only, no restrictions.", style)],
            Encryption = new EncryptionSpec { UserPassword = "user-secret" },
        };
    }

    /// <summary>
    /// A <see cref="PlainTextSpec"/> with no explicit <see cref="PlainTextSpec.Style"/>,
    /// the one content item that resolves through the library's
    /// <c>Document.Add(string, TextStyle?)</c> overload and so reads
    /// <see cref="DocumentSpec.DefaultTextStyle"/>, rather than resolving its
    /// own fallback the way every other content item does. The default style
    /// is set to values distinct from every built-in fallback, so a renderer
    /// or emitter that stopped consulting it would change the visible output.
    /// </summary>
    public static DocumentSpec PlainTextUsesDocumentDefault()
    {
        var defaultStyle = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(Standard14.TimesBoldItalic),
            FontSize = 17,
            Color = new ColorRgb(0.2, 0.4, 0.1),
        };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(350, 250),
            DefaultTextStyle = defaultStyle,
            Content = [new PlainTextSpec { Text = "Uses the document's registered default style." }],
        };
    }

    /// <summary>
    /// A device CMYK output intent, matching <c>Document.UseCmykOutputIntent</c>.
    /// Measured directly against the library, the call writes nothing into
    /// the saved bytes unless the document declares a conformance profile, so
    /// a sample without one would not exercise this branch at all, the same
    /// way the previous <c>DefaultTextStyle</c> sample looked like coverage
    /// without being load-bearing.
    /// </summary>
    /// <remarks>
    /// Claims PDF/UA-1, not PDF/A-2b. Measured directly, once every
    /// conformant sample was preflighted structurally rather than at three
    /// hand-picked call sites (see
    /// <see cref="SpecRoundTripTests.SampleNamesClaimingConformance"/>): a
    /// PDF/A-2b claim here fails ISO 19005-2:2011 clause 6.2.4.3, because
    /// this model can only ever paint content in DeviceRGB (plan section
    /// 6.2's CMYK note) and this sample's only output intent is a CMYK one,
    /// with no RGB <c>DestOutputProfile</c> to justify the DeviceRGB fills.
    /// That is an ISO 19005-2 content rule specifically; PDF/UA-1's rule
    /// catalogue (ISO 14289-1) has no equivalent, so the identical bytes,
    /// under the identical CMYK output intent, are genuinely compliant
    /// against PDF/UA-1 once the two PDF/UA-1 requirements every conformant
    /// sample must meet are also met: a document title (clause 7.1, via
    /// <see cref="Metadata"/>) and correct heading nesting (clause 7.4.2,
    /// moot here since this sample has no heading).
    /// </remarks>
    public static DocumentSpec CmykOutputIntent()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11 };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(400, 300),
            DefaultTextStyle = style,
            EmbeddedFonts = [LiberationSansBytes()],
            Conformance = DocumentConformance.PdfUA1,
            Tagged = true,
            Language = "en",
            Metadata = new DocumentMetadataSpec { Title = "CMYK output intent sample" },
            Content = [ParagraphSpec.FromText("Declares a device CMYK output intent.", style)],
            OutputIntent = new CmykOutputIntentSpec { OutputConditionIdentifier = "U.S. Web Coated (SWOP) v2" },
        };
    }

    /// <summary>
    /// The remaining emitter branches with no other coverage in this file:
    /// <see cref="TextStyleSpec.Leading"/>, <see cref="ListSpec.Indent"/>,
    /// <see cref="TableSpec.BorderWidth"/>, the GIF and TIFF Kernel image
    /// loaders, and <see cref="ImageSpec.Height"/> set independently of
    /// <see cref="ImageSpec.Width"/>.
    /// </summary>
    public static DocumentSpec RemainingBranchCoverage()
    {
        var leadedStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11, Leading = 16 };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(500, 700),
            DefaultTextStyle = leadedStyle,
            Content =
            [
                ParagraphSpec.FromText("A paragraph with explicit leading.", leadedStyle),
                new ListSpec
                {
                    Style = ListStyle.Unordered,
                    Indent = 30,
                    Items = [new ListItemSpec { Text = "Indented item" }],
                },
                new TableSpec
                {
                    BorderWidth = 2,
                    Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "Thick border" }] }],
                },
                new ImageSpec { Format = ImageFormat.Gif, Bytes = OnePixelGif, Height = 30 },
                new ImageSpec { Format = ImageFormat.Tiff, Bytes = OnePixelTiff },
            ],
        };
    }

    /// <summary>
    /// C4-C-M6: the thirteen emitter branches with no coverage anywhere else
    /// in this file. The largest cluster is element-level <c>Margins</c> on
    /// all seven content item types that expose it (heading, paragraph, list,
    /// table, image, pie chart and line separator), each given a fully
    /// asymmetric <see cref="EdgeInsets"/> so a scrambled parameter order in
    /// <c>SpecCodeEmitter.EmitEdgeInsets</c> (previously provable only through
    /// document-level margins) would render the recompiled document
    /// differently and fail the round trip. The rest:
    /// <see cref="ParagraphSpec.Language"/>; an explicit
    /// <see cref="TableCellSpec.Style"/> and <see cref="TableCellSpec.Language"/>;
    /// a non-default <see cref="ImageSpec.Alignment"/>; a non-default
    /// <see cref="PieChartSpec.StrokeWidth"/> and an explicit
    /// <see cref="PieChartSpec.AltText"/>; and an explicit
    /// <see cref="ListItemSpec.Style"/>.
    /// </summary>
    public static DocumentSpec RemainingEmitterBranchCoverage()
    {
        var bodyStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica), FontSize = 11 };
        var cellStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.TimesItalic), FontSize = 9 };
        var listItemStyle = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Courier), FontSize = 10 };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(500, 700),
            DefaultTextStyle = bodyStyle,
            Content =
            [
                new HeadingSpec { Text = "Margins on every element", Level = 2, Margins = new EdgeInsets(4, 8, 12, 16) },
                new ParagraphSpec
                {
                    Runs = [new TextRunSpec("A paragraph with margins and an explicit language.", bodyStyle)],
                    Margins = new EdgeInsets(3, 6, 9, 12),
                    Language = "fr",
                },
                new ListSpec
                {
                    Style = ListStyle.Unordered,
                    Margins = new EdgeInsets(5, 10, 15, 20),
                    Items = [new ListItemSpec { Text = "Styled item", Style = listItemStyle }],
                },
                new TableSpec
                {
                    Margins = new EdgeInsets(6, 12, 18, 24),
                    Rows =
                    [
                        new TableRowSpec
                        {
                            Cells = [new TableCellSpec { Content = "Styled, language-tagged cell", Style = cellStyle, Language = "de" }],
                        },
                    ],
                },
                new ImageSpec
                {
                    Format = ImageFormat.Png,
                    Bytes = OnePixelPng,
                    Alignment = HorizontalAlignment.Center,
                    Margins = new EdgeInsets(7, 14, 21, 28),
                },
                new PieChartSpec
                {
                    Diameter = 60,
                    Margins = new EdgeInsets(2, 4, 6, 8),
                    StrokeWidth = 1.5,
                    AltText = "A pie chart with a thick stroke.",
                    Slices = [new PieSlice(1, new ColorRgb(0.1, 0.6, 0.3))],
                },
                new LineSeparatorSpec { Margins = new EdgeInsets(9, 18, 27, 36) },
            ],
        };
    }
}
