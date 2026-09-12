using VellumPdf.Fonts;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Assets;
using VellumPdfShowcase.Web.Model;
using DocumentConformance = VellumPdf.Document.PdfConformance;

namespace VellumPdfShowcase.Web.Catalog;

/// <summary>
/// Every capability the gallery presents, in the order it presents them.
/// </summary>
/// <remarks>
/// This list is deliberately additive. Each entry is self-contained: an
/// identifier, the prose that describes it, the assets it needs, and one
/// function returning the document that demonstrates it. Adding a capability
/// means adding an entry and nothing else, which is what allows the catalogue
/// to be filled in over many sittings without any page changing.
///
/// NOTE the entries marked <see cref="CapabilityStatus.Planned"/> are not
/// decoration. The gallery shows the shape of the whole library rather than only
/// its finished parts, so a capability the pinned package cannot do appears as a
/// card that says so, with its milestone, rather than being absent and leaving
/// the visitor to guess whether it exists.
/// </remarks>
public static class CapabilityCatalog
{
    private static readonly ColorRgb Ink = new(0.11, 0.13, 0.15);
    private static readonly ColorRgb Accent = new(0.0, 0.35, 0.6);

    /// <summary>Every capability, available and planned alike.</summary>
    public static IReadOnlyList<Capability> All { get; } =
    [
        new Capability
        {
            Id = "standard-14-text",
            Title = "Standard 14 text",
            Summary = "Headings, paragraphs and styled runs set in the fourteen faces every reader carries, with no font to embed.",
            Category = CapabilityCategory.Text,
            Build = _ => StandardFourteenText(),
        },
        new Capability
        {
            Id = "embedded-truetype",
            Title = "Embedded TrueType",
            Summary = "A TrueType face embedded in the file, so the document renders identically wherever it is opened.",
            Category = CapabilityCategory.Text,
            RequiredAssets = [ShowcaseAssets.LiberationSansRegular],
            Build = assets => EmbeddedTrueType(assets[ShowcaseAssets.LiberationSansRegular]),
        },
        new Capability
        {
            Id = "lists",
            Title = "Ordered and unordered lists",
            Summary = "All four list styles in one document: unordered, decimal, alphabetic and roman, and one level of nesting.",
            Category = CapabilityCategory.Layout,
            Build = _ => Lists(),
        },
        new Capability
        {
            Id = "tables",
            Title = "Tables",
            Summary = "A header row, spanning cells, explicit column widths, cell backgrounds and a drawn border.",
            Category = CapabilityCategory.Layout,
            Build = _ => Tables(),
        },
        new Capability
        {
            Id = "running-bands",
            Title = "Running headers and footers",
            Summary = "A header and a footer repeated on every page, with page number substitution.",
            Category = CapabilityCategory.Layout,
            Build = _ => RunningBands(),
        },
        new Capability
        {
            Id = "images",
            Title = "Images",
            Summary = "The same picture embedded from four encodings, so a reader can compare what each format preserves.",
            Category = CapabilityCategory.Graphics,
            RequiredAssets =
            [
                ShowcaseAssets.TestCardPng,
                ShowcaseAssets.TestCardJpeg,
                ShowcaseAssets.TestCardBmp,
                ShowcaseAssets.TestCardTiff,
            ],
            Build = Images,
        },
        new Capability
        {
            Id = "pie-chart",
            Title = "Pie charts",
            Summary = "A pie chart drawn as vector content, with a stroke, a start angle and a direction.",
            Category = CapabilityCategory.Graphics,
            Build = _ => PieChart(),
        },
        new Capability
        {
            Id = "pdfa-conformance",
            Title = "PDF/A-2b with an output intent",
            Summary = "A tagged document claiming PDF/A-2b, with an embedded face and an sRGB output intent, validated in the browser.",
            Category = CapabilityCategory.Conformance,
            RequiredAssets = [ShowcaseAssets.LiberationSansRegular, ShowcaseAssets.SrgbIccProfile],
            Build = assets => PdfA2b(
                assets[ShowcaseAssets.LiberationSansRegular],
                assets[ShowcaseAssets.SrgbIccProfile]),
        },
        new Capability
        {
            Id = "encryption",
            Title = "Encryption and permissions",
            Summary = "A document encrypted with a user password and a restricted permission set.",
            Category = CapabilityCategory.Documents,

            // The library encrypts perfectly well; the browser cannot. Saving an
            // encrypted document raises "Algorithm 'Aes' is not supported on this
            // platform" on the browser-wasm runtime, which does not carry the AES
            // implementation the encryption path needs.
            //
            // NOTE this entry keeps its Build. The specification is valid and the
            // test suite, which runs on desktop .NET, still renders it, so the
            // document stays under test even though no page here generates it.
            // That is also why this was not caught: the suite runs on a runtime
            // the site does not.
            Status = CapabilityStatus.UnavailableInBrowser,
            BrowserLimitation =
                "Encryption needs AES, which the browser's .NET runtime does not provide. "
                + "The library encrypts normally on a server or a desktop application.",
            Build = _ => Encrypted(),
        },

        // Planned. Each of these is absent from the pinned package, and the card
        // says so rather than the capability being omitted.
        new Capability
        {
            Id = "nested-lists",
            Title = "Nested lists beyond one level",
            Summary = "List items nested more than one level deep. The pinned package discards the third level and below.",
            Category = CapabilityCategory.Layout,
            Status = CapabilityStatus.Planned,
            Milestone = "2.3.3",
            TrackingUri = "https://github.com/Tim81/VellumPdf/issues/479",
        },
        new Capability
        {
            Id = "gif-images",
            Title = "GIF images",
            Summary = "GIF is an advertised image format, but the pinned package refuses ordinary GIF files while decoding trivial ones.",
            Category = CapabilityCategory.Graphics,
            Status = CapabilityStatus.Planned,
            Milestone = "not yet scheduled",
            TrackingUri = "https://github.com/Tim81/VellumPdf/issues",
        },
    ];

    /// <summary>The capability with this identifier, or null when there is none.</summary>
    public static Capability? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(capability => capability.Id == id);

    /// <summary>The categories that actually carry a capability, in declaration order.</summary>
    public static IReadOnlyList<CapabilityCategory> Categories { get; } =
        [.. All.Select(capability => capability.Category).Distinct()];

    private static TextStyleSpec Standard14Style(Standard14 face, double size) =>
        new() { Font = FontSpec.FromStandard14(face), FontSize = size, Color = Ink };

    private static DocumentSpec StandardFourteenText()
    {
        var body = Standard14Style(Standard14.Helvetica, 11);
        var emphasis = Standard14Style(Standard14.HelveticaOblique, 11);
        var mono = Standard14Style(Standard14.Courier, 10);

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Content =
            [
                new HeadingSpec { Text = "Standard 14 text", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
                new ParagraphSpec
                {
                    Runs =
                    [
                        new TextRunSpec("The fourteen standard faces need no embedding: every conforming reader carries them. ", body),
                        new TextRunSpec("Runs within one paragraph may differ in face, size and colour", emphasis),
                        new TextRunSpec(", and adjacent runs sharing a style are merged by the library.", body),
                    ],
                },
                new HeadingSpec { Text = "A second level", Level = 1, Style = Standard14Style(Standard14.HelveticaBold, 14) },
                new ParagraphSpec { Runs = [new TextRunSpec("Fixed-pitch text is available through the Courier faces.", mono)] },
                new LineSeparatorSpec { LineWidth = 0.75, Color = Accent },
                new PlainTextSpec { Text = "A plain string added without a style resolves to the document default." },
            ],
        };
    }

    private static DocumentSpec EmbeddedTrueType(byte[] face)
    {
        var body = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11, Color = Ink };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            EmbeddedFonts = [face],
            Content =
            [
                new HeadingSpec { Text = "Embedded TrueType", Level = 0, Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 20, Color = Ink } },
                new ParagraphSpec
                {
                    Runs =
                    [
                        new TextRunSpec(
                            "This paragraph is set in Liberation Sans, embedded in the file itself. A reader with no such face installed still renders it exactly as written.",
                            body),
                    ],
                },
            ],
        };
    }

    private static DocumentSpec Lists()
    {
        var body = Standard14Style(Standard14.Helvetica, 11);

        static ListSpec Make(ListStyle style, TextStyleSpec text) => new()
        {
            Style = style,
            DefaultStyle = text,
            Items = [new ListItemSpec { Text = "First" }, new ListItemSpec { Text = "Second" }, new ListItemSpec { Text = "Third" }],
        };

        // NOTE the nesting stops at one level deliberately, and the depth is the
        // point of this section rather than an accident of it. Measured against
        // the pinned 2.3.2 package: a chain of list items draws the first two
        // levels and discards every level below, with nothing reported on the
        // document, and the inflated content stream is identical at every chain
        // depth from two to eight. NOTE: identical, not byte-identical. Two
        // renders of one specification never match byte for byte, because the
        // library writes a random document identifier into each.
        //
        // One level is therefore what the renderer does, so a sample built at
        // that depth is correct today and stays correct when deeper nesting
        // arrives. The nested item is drawn properly rather than merely
        // present: its own marker sits at the parent's text position and its
        // text one indent further, with numbering of its own.
        //
        // The limit itself is stated on the "nested-lists" capability card,
        // which is a Planned entry and therefore builds no document. Nothing in
        // the catalogue demonstrates the discard, deliberately: a sample built
        // to show the limit would have to be rewritten when the library's own
        // capability table is corrected and again when nesting works.
        static ListSpec Nested(TextStyleSpec text) => new()
        {
            Style = ListStyle.OrderedDecimal,
            DefaultStyle = text,
            Items =
            [
                new ListItemSpec { Text = "First" },
                new ListItemSpec
                {
                    Text = "Second",
                    Children = [new ListItemSpec { Text = "Nested under the second item" }],
                },
                new ListItemSpec { Text = "Third" },
            ],
        };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Content =
            [
                new HeadingSpec { Text = "Lists", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
                new PlainTextSpec { Text = "Unordered", Style = Standard14Style(Standard14.HelveticaBold, 12) },
                Make(ListStyle.Unordered, body),
                new PlainTextSpec { Text = "Decimal", Style = Standard14Style(Standard14.HelveticaBold, 12) },
                Make(ListStyle.OrderedDecimal, body),
                new PlainTextSpec { Text = "Alphabetic", Style = Standard14Style(Standard14.HelveticaBold, 12) },
                Make(ListStyle.OrderedAlpha, body),
                new PlainTextSpec { Text = "Roman", Style = Standard14Style(Standard14.HelveticaBold, 12) },
                Make(ListStyle.OrderedRoman, body),
                new PlainTextSpec { Text = "Nested, one level", Style = Standard14Style(Standard14.HelveticaBold, 12) },
                Nested(body),
            ],
        };
    }

    private static DocumentSpec Tables()
    {
        var body = Standard14Style(Standard14.Helvetica, 10);
        var header = Standard14Style(Standard14.HelveticaBold, 10);

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Content =
            [
                new HeadingSpec { Text = "Tables", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
                new TableSpec
                {
                    DefaultCellStyle = body,
                    BorderWidth = 0.5,
                    BorderColor = Ink,
                    ColumnWidths = [140, 90, 90],
                    Rows =
                    [
                        new TableRowSpec
                        {
                            IsHeader = true,
                            Cells =
                            [
                                new TableCellSpec { Content = "Profile", Style = header },
                                new TableCellSpec { Content = "Tagged", Style = header },
                                new TableCellSpec { Content = "Output intent", Style = header },
                            ],
                        },
                        new TableRowSpec
                        {
                            Cells =
                            [
                                new TableCellSpec { Content = "PDF/A-2b" },
                                new TableCellSpec { Content = "No" },
                                new TableCellSpec { Content = "Required" },
                            ],
                        },
                        new TableRowSpec
                        {
                            Cells =
                            [
                                new TableCellSpec { Content = "PDF/A-2a" },
                                new TableCellSpec { Content = "Yes" },
                                new TableCellSpec { Content = "Required" },
                            ],
                        },
                        new TableRowSpec
                        {
                            Cells =
                            [
                                new TableCellSpec { Content = "A cell spanning all three columns", ColSpan = 3, Background = new ColorRgb(0.93, 0.95, 0.98) },
                            ],
                        },
                    ],
                },
            ],
        };
    }

    private static DocumentSpec RunningBands()
    {
        var body = Standard14Style(Standard14.Helvetica, 11);
        var band = Standard14Style(Standard14.Helvetica, 9);

        List<ContentItemSpec> content =
        [
            new HeadingSpec { Text = "Running headers and footers", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
        ];

        for (var i = 0; i < 40; i++)
        {
            content.Add(new ParagraphSpec
            {
                Runs = [new TextRunSpec("A band is drawn on every page the document produces, so this text is long enough to produce several.", body)],
            });
        }

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Header = new RunningBandSpec { Template = "VellumPdf showcase", Style = band, Alignment = HorizontalAlignment.Left },
            Footer = new RunningBandSpec { Template = "Page {page} of {pages}", Style = band, Alignment = HorizontalAlignment.Center },
            Content = content,
        };
    }

    private static DocumentSpec Images(CapabilityAssets assets)
    {
        var body = Standard14Style(Standard14.Helvetica, 10);
        var label = Standard14Style(Standard14.HelveticaBold, 10);

        (string Path, ImageFormat Format, string Name)[] encodings =
        [
            (ShowcaseAssets.TestCardPng, ImageFormat.Png, "PNG"),
            (ShowcaseAssets.TestCardJpeg, ImageFormat.Jpeg, "JPEG"),
            (ShowcaseAssets.TestCardBmp, ImageFormat.Bmp, "Windows bitmap"),
            (ShowcaseAssets.TestCardTiff, ImageFormat.Tiff, "TIFF"),
        ];

        List<ContentItemSpec> content =
        [
            new HeadingSpec { Text = "Images", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
            new ParagraphSpec
            {
                Runs = [new TextRunSpec("One test card, encoded four ways. The gradient shows palette banding, the hard edges show lossy ringing.", body)],
            },
        ];

        foreach (var (path, format, name) in encodings)
        {
            content.Add(new PlainTextSpec { Text = name, Style = label });
            content.Add(new ImageSpec
            {
                Bytes = assets[path],
                Format = format,
                Width = 200,
                AltText = $"Test card encoded as {name}",
            });
        }

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Content = content,
        };
    }

    private static DocumentSpec PieChart()
    {
        var body = Standard14Style(Standard14.Helvetica, 11);

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Content =
            [
                new HeadingSpec { Text = "Pie charts", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
                new PieChartSpec
                {
                    Diameter = 220,
                    StartAngle = 90,
                    Clockwise = true,
                    StrokeColor = new ColorRgb(1, 1, 1),
                    StrokeWidth = 1.5,
                    AltText = "Distribution across four categories",
                    Slices =
                    [
                        new PieSlice(42, new ColorRgb(0.00, 0.45, 0.70), "Text"),
                        new PieSlice(28, new ColorRgb(0.90, 0.62, 0.00), "Layout"),
                        new PieSlice(18, new ColorRgb(0.00, 0.62, 0.45), "Graphics"),
                        new PieSlice(12, new ColorRgb(0.80, 0.40, 0.00), "Conformance"),
                    ],
                },
            ],
        };
    }

    private static DocumentSpec PdfA2b(byte[] face, byte[] iccProfile)
    {
        var body = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 11, Color = Ink };

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            EmbeddedFonts = [face],
            Conformance = DocumentConformance.PdfA2b,
            Tagged = true,
            Language = "en",
            Content =
            [
                new HeadingSpec
                {
                    Text = "PDF/A-2b",
                    Level = 0,
                    Language = "en",
                    Style = new TextStyleSpec { Font = FontSpec.FromEmbedded(0), FontSize = 20, Color = Ink },
                },
                new ParagraphSpec
                {
                    Language = "en",
                    Runs = [new TextRunSpec("An archival claim requires every face to be embedded and a colour space to be declared.", body)],
                },
            ],
            OutputIntent = new PdfAOutputIntentSpec
            {
                IccProfile = iccProfile,
                ComponentCount = 3,
                OutputConditionIdentifier = "sRGB IEC61966-2.1",
            },
        };
    }

    private static DocumentSpec Encrypted()
    {
        var body = Standard14Style(Standard14.Helvetica, 11);

        return new DocumentSpec
        {
            Page = PageSizeSpec.FromRectangle(VellumPdf.Document.PageSize.A4),
            Margins = new EdgeInsets(64),
            DefaultTextStyle = body,
            Content =
            [
                new HeadingSpec { Text = "Encryption", Level = 0, Style = Standard14Style(Standard14.HelveticaBold, 20) },
                new ParagraphSpec
                {
                    Runs = [new TextRunSpec("This document opens with the user password and permits printing only.", body)],
                },
            ],
            Encryption = new EncryptionSpec
            {
                UserPassword = "showcase",
                OwnerPassword = "showcase-owner",
                Permissions = VellumPdf.Encryption.PdfPermissions.Print,
            },
        };
    }
}
