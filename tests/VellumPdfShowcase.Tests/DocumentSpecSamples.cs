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
            Page = new PageSizeSpec(595.2756, 841.8898),
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
    /// unset. This is what actually exercises the fallback value each builder
    /// applies for an omitted field: <see cref="Generation.SpecRenderer"/> must
    /// fall back to the same value the library itself defaults to, or this
    /// spec would render one way and the code <see cref="Generation.SpecCodeEmitter"/>
    /// emits for it — which omits the same unset properties rather than
    /// spelling out the library's default — would render another.
    /// </summary>
    public static DocumentSpec EveryContentItemTypeAtDefault()
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

        return new DocumentSpec
        {
            Page = new PageSizeSpec(595.2756, 841.8898),
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
            Page = new PageSizeSpec(595.2756, 841.8898),
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
            Page = new PageSizeSpec(595.2756, 841.8898),
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
}
