using VellumPdf.Encryption;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Four ways to build a <see cref="DocumentSpec"/> the library cannot render,
/// all four now rejected at construction with a message naming the actual
/// problem, because <see cref="DocumentSpec.Content"/>,
/// <see cref="PieChartSpec.Slices"/>, <see cref="TableSpec.Rows"/> and
/// <see cref="TableRowSpec.Cells"/> are all <see langword="required"/>
/// properties with a non-empty guard rather than a sensible empty value.
/// </summary>
public class DocumentSpecValidationTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void DocumentSpec_EmptyContent_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = [] });
        Assert.Contains("content", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PieChartSpec_EmptySlices_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new PieChartSpec { Slices = [], Diameter = 100 });
        Assert.Contains("slice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableSpec_EmptyRows_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableSpec { Rows = [] });
        Assert.Contains("row", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableRowSpec_EmptyCells_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TableRowSpec { Cells = [] });
        Assert.Contains("cell", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://example.com/file")]
    public void TextStyleSpec_LinkUriWithDisallowedScheme_ThrowsAtConstruction(string uri)
    {
        Assert.Throws<ArgumentException>(() => new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = uri });
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    public void TextStyleSpec_LinkUriWithAllowedScheme_Constructs(string uri)
    {
        var style = new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = uri };
        Assert.Equal(uri, style.LinkUri);
    }

    /// <summary>
    /// Plan section 3.4.0.1: every member of <see cref="TextStyleSpec"/> must
    /// implement value equality, because <see cref="Generation.SpecRenderer"/>'s
    /// style cache and <see cref="Generation.SpecCodeEmitter"/>'s style
    /// hoisting both key on this record's own equality, and C# record
    /// equality falls back to reference equality for any member whose type
    /// does not implement value equality. Two independently constructed but
    /// equal instances must therefore be <c>Equals</c>, hash alike, and
    /// collide as the same dictionary key.
    /// </summary>
    [Fact]
    public void TextStyleSpec_TwoEqualInstances_AreEqualHashAlikeAndCollideInADictionary()
    {
        var first = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.HelveticaBold),
            FontSize = 13,
            Leading = 15,
            Color = new ColorRgb(0.1, 0.2, 0.3),
            LinkUri = "https://example.com",
        };
        var second = new TextStyleSpec
        {
            Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.HelveticaBold),
            FontSize = 13,
            Leading = 15,
            Color = new ColorRgb(0.1, 0.2, 0.3),
            LinkUri = "https://example.com",
        };

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        var dictionary = new Dictionary<TextStyleSpec, string> { [first] = "value" };
        Assert.True(dictionary.ContainsKey(second));
        Assert.Equal("value", dictionary[second]);
    }
}

/// <summary>
/// C4-C-M1: every collection member snapshots the caller's value with a
/// collection expression at construction, so a fully constructed record
/// cannot be emptied out from under itself by mutating a list the caller
/// happened to alias. Before the fix, each of these nine tests failed: the
/// init accessor checked <c>Count</c> once and then stored the caller's own
/// reference.
/// </summary>
public class DocumentSpecCollectionAliasingTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static DocumentSpec MinimalDocument(IReadOnlyList<ContentItemSpec> content) =>
        new() { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = content };

    [Fact]
    public void DocumentSpec_Content_SnapshotsAliasedList()
    {
        List<ContentItemSpec> aliased = [new PlainTextSpec { Text = "kept" }];
        var spec = MinimalDocument(aliased);

        aliased.Clear();

        Assert.Single(spec.Content);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFonts_SnapshotsAliasedList()
    {
        List<byte[]> aliased = [[1, 2, 3]];
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            EmbeddedFonts = aliased,
            Content = [new PlainTextSpec { Text = "x" }],
        };

        aliased.Clear();

        Assert.Single(spec.EmbeddedFonts);
    }

    [Fact]
    public void ParagraphSpec_Runs_SnapshotsAliasedList()
    {
        List<TextRunSpec> aliased = [new TextRunSpec("kept", Style())];
        var spec = new ParagraphSpec { Runs = aliased };

        aliased.Clear();

        Assert.Single(spec.Runs);
    }

    [Fact]
    public void ListSpec_Items_SnapshotsAliasedList()
    {
        List<ListItemSpec> aliased = [new ListItemSpec { Text = "kept" }];
        var spec = new ListSpec { Style = ListStyle.Unordered, Items = aliased };

        aliased.Clear();

        Assert.Single(spec.Items);
    }

    [Fact]
    public void ListItemSpec_Children_SnapshotsAliasedList()
    {
        List<ListItemSpec> aliased = [new ListItemSpec { Text = "kept" }];
        var spec = new ListItemSpec { Text = "parent", Children = aliased };

        aliased.Clear();

        Assert.Single(spec.Children);
    }

    [Fact]
    public void TableSpec_Rows_SnapshotsAliasedList()
    {
        List<TableRowSpec> aliased = [new TableRowSpec { Cells = [new TableCellSpec { Content = "kept" }] }];
        var spec = new TableSpec { Rows = aliased };

        aliased.Clear();

        Assert.Single(spec.Rows);
    }

    [Fact]
    public void TableSpec_ColumnWidths_SnapshotsAliasedList()
    {
        List<double> aliased = [100, 200];
        var spec = new TableSpec
        {
            Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] }],
            ColumnWidths = aliased,
        };

        aliased.Clear();

        Assert.Equal(2, spec.ColumnWidths!.Count);
    }

    [Fact]
    public void TableRowSpec_Cells_SnapshotsAliasedList()
    {
        List<TableCellSpec> aliased = [new TableCellSpec { Content = "kept" }];
        var spec = new TableRowSpec { Cells = aliased };

        aliased.Clear();

        Assert.Single(spec.Cells);
    }

    [Fact]
    public void PieChartSpec_Slices_SnapshotsAliasedList()
    {
        List<PieSlice> aliased = [new PieSlice(1, ColorRgb.Black)];
        var spec = new PieChartSpec { Diameter = 10, Slices = aliased };

        aliased.Clear();

        Assert.Single(spec.Slices);
    }
}

/// <summary>
/// C4-C-M2: <see cref="ListItemSpec.Children"/> caps nesting depth at
/// <see cref="SpecLimits.MaxListNestingDepth"/>, the one plan section 5.4
/// control a wrapped parser call cannot rescue, because an uncaught stack
/// overflow terminates the process outright.
/// </summary>
public class ListNestingDepthTests
{
    [Fact]
    public void ListItemSpec_NestingAtLimit_Constructs()
    {
        ListItemSpec item = new() { Text = "leaf" };
        for (var depth = 1; depth < SpecLimits.MaxListNestingDepth; depth++)
        {
            item = new ListItemSpec { Text = $"level{depth}", Children = [item] };
        }

        Assert.NotNull(item);
    }

    [Fact]
    public void ListItemSpec_NestingOneBeyondLimit_ThrowsAtConstruction()
    {
        ListItemSpec item = new() { Text = "leaf" };
        for (var depth = 1; depth < SpecLimits.MaxListNestingDepth; depth++)
        {
            item = new ListItemSpec { Text = $"level{depth}", Children = [item] };
        }

        var captured = item;
        var exception = Assert.Throws<ArgumentException>(() => new ListItemSpec { Text = "one too many", Children = [captured] });
        Assert.Contains("nest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Plan section 5.4: caps on specification size, so no single specification can freeze or exhaust the tab.</summary>
public class SpecSizeLimitTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void DocumentSpec_ContentBeyondLimit_ThrowsAtConstruction()
    {
        List<ContentItemSpec> content = [.. Enumerable.Range(0, SpecLimits.MaxContentItems + 1).Select(i => (ContentItemSpec)new PlainTextSpec { Text = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = content });
        Assert.Contains("content", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableSpec_RowsBeyondLimit_ThrowsAtConstruction()
    {
        List<TableRowSpec> rows = [.. Enumerable.Range(0, SpecLimits.MaxTableRows + 1).Select(_ => new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] })];

        var exception = Assert.Throws<ArgumentException>(() => new TableSpec { Rows = rows });
        Assert.Contains("row", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableRowSpec_CellsBeyondLimit_ThrowsAtConstruction()
    {
        List<TableCellSpec> cells = [.. Enumerable.Range(0, SpecLimits.MaxTableCellsPerRow + 1).Select(i => new TableCellSpec { Content = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() => new TableRowSpec { Cells = cells });
        Assert.Contains("cell", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PieChartSpec_SlicesBeyondLimit_ThrowsAtConstruction()
    {
        List<PieSlice> slices = [.. Enumerable.Range(0, SpecLimits.MaxChartSlices + 1).Select(_ => new PieSlice(1, ColorRgb.Black))];

        var exception = Assert.Throws<ArgumentException>(() => new PieChartSpec { Diameter = 10, Slices = slices });
        Assert.Contains("slice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeadingSpec_TextBeyondLimit_ThrowsAtConstruction()
    {
        var tooLong = new string('a', SpecLimits.MaxTextLength + 1);
        var exception = Assert.Throws<ArgumentException>(() => new HeadingSpec { Text = tooLong, Level = 1 });
        Assert.Contains("Text", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingSpec_TextAtLimit_Constructs()
    {
        var atLimit = new string('a', SpecLimits.MaxTextLength);
        var heading = new HeadingSpec { Text = atLimit, Level = 1 };
        Assert.Equal(SpecLimits.MaxTextLength, heading.Text.Length);
    }

    [Fact]
    public void DocumentSpec_LanguageBeyondLimit_ThrowsAtConstruction()
    {
        var tooLong = new string('a', SpecLimits.MaxLanguageTagLength + 1);
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style(), Content = [new PlainTextSpec { Text = "x" }], Language = tooLong });
        Assert.Contains("Language", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextStyleSpec_LinkUriBeyondLimit_ThrowsAtConstruction()
    {
        var tooLong = "https://example.com/" + new string('a', SpecLimits.MaxUriLength);
        var exception = Assert.Throws<ArgumentException>(() =>
            new TextStyleSpec { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica), LinkUri = tooLong });
        Assert.Contains("LinkUri", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageSpec_BytesBeyondLimit_ThrowsAtConstruction()
    {
        var tooLarge = new byte[SpecLimits.MaxAssetBytes + 1];
        var exception = Assert.Throws<ArgumentException>(() => new ImageSpec { Format = ImageFormat.Png, Bytes = tooLarge });
        Assert.Contains("Bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFontBeyondLimit_ThrowsAtConstruction()
    {
        var tooLarge = new byte[SpecLimits.MaxAssetBytes + 1];
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
                EmbeddedFonts = [tooLarge],
            });
        Assert.Contains("EmbeddedFonts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfAOutputIntentSpec_IccProfileBeyondLimit_ThrowsAtConstruction()
    {
        var tooLarge = new byte[SpecLimits.MaxAssetBytes + 1];
        var exception = Assert.Throws<ArgumentException>(() =>
            new PdfAOutputIntentSpec { IccProfile = tooLarge, ComponentCount = 3, OutputConditionIdentifier = "x" });
        Assert.Contains("IccProfile", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Plan section 5.4 control 2: the declared <see cref="ImageFormat"/> must
/// agree with the image's own magic bytes, checked by
/// <see cref="DocumentSpec.Content"/> because that is the only property that
/// ever sees both <see cref="ImageSpec.Format"/> and <see cref="ImageSpec.Bytes"/>
/// fully set.
/// </summary>
public class ImageFormatAgreementTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void DocumentSpec_ImageBytesContradictDeclaredFormat_ThrowsAtConstruction()
    {
        // Ten arbitrary bytes matching none of the five signatures ImageSignature checks.
        byte[] notPng = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = notPng }],
            });
        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Plan section 5.4: an ICC profile header must be internally consistent
/// before it is embedded as an output intent on a document claiming PDF/A or
/// PDF/UA conformance. Checked by <see cref="DocumentSpec.OutputIntent"/>
/// because that is the only property that ever sees both
/// <see cref="PdfAOutputIntentSpec.IccProfile"/> and
/// <see cref="PdfAOutputIntentSpec.ComponentCount"/> fully set.
/// </summary>
public class IccProfileHeaderTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static DocumentSpec DocumentWithOutputIntent(PdfAOutputIntentSpec outputIntent) => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x" }],
        OutputIntent = outputIntent,
    };

    [Fact]
    public void ThreeJunkBytes_ThrowsAtConstruction_RatherThanEmbeddingSilently()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = [1, 2, 3], ComponentCount = 3, OutputConditionIdentifier = "x" }));
        Assert.Contains("too small", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SizeFieldDisagreesWithActualLength_Throws()
    {
        var profile = DocumentSpecSamples.SrgbIccProfileBytes();
        var tampered = (byte[])profile.Clone();
        tampered[3] = unchecked((byte)(tampered[3] + 1)); // corrupt the low byte of the big-endian size field

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = tampered, ComponentCount = 3, OutputConditionIdentifier = "x" }));
        Assert.Contains("size field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingAcspMagic_Throws()
    {
        var profile = DocumentSpecSamples.SrgbIccProfileBytes();
        var tampered = (byte[])profile.Clone();
        tampered[36] = (byte)'X';

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = tampered, ComponentCount = 3, OutputConditionIdentifier = "x" }));
        Assert.Contains("acsp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComponentCountDisagreesWithDeclaredColorSpace_Throws()
    {
        var profile = DocumentSpecSamples.SrgbIccProfileBytes(); // declares "RGB ", which requires ComponentCount 3

        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithOutputIntent(new PdfAOutputIntentSpec { IccProfile = profile, ComponentCount = 4, OutputConditionIdentifier = "x" }));
        Assert.Contains("colour space", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidSrgbProfileWithMatchingComponentCount_Constructs()
    {
        var spec = DocumentWithOutputIntent(new PdfAOutputIntentSpec
        {
            IccProfile = DocumentSpecSamples.SrgbIccProfileBytes(),
            ComponentCount = 3,
            OutputConditionIdentifier = "sRGB IEC61966-2.1",
        });

        Assert.IsType<PdfAOutputIntentSpec>(spec.OutputIntent);
    }
}

/// <summary>
/// C4-C-M5: a restricted <see cref="EncryptionSpec.Permissions"/> value
/// requires an <see cref="EncryptionSpec.OwnerPassword"/>, because the
/// library authenticates full owner access to whichever password opens the
/// document, and with no owner password set that password is
/// <see cref="EncryptionSpec.UserPassword"/>. Checked by
/// <see cref="DocumentSpec.Encryption"/> because that is the only property
/// that ever sees both <see cref="EncryptionSpec.Permissions"/> and
/// <see cref="EncryptionSpec.OwnerPassword"/> fully set.
/// </summary>
public class EncryptionOwnerPasswordTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    private static DocumentSpec DocumentWithEncryption(EncryptionSpec encryption) => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x" }],
        Encryption = encryption,
    };

    [Fact]
    public void RestrictedPermissionsWithNullOwnerPassword_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = "user-secret", Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictedPermissionsWithEmptyOwnerPassword_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = "user-secret", OwnerPassword = "", Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestrictedPermissionsWithOwnerPassword_Constructs()
    {
        var spec = DocumentWithEncryption(new EncryptionSpec
        {
            UserPassword = "user-secret",
            OwnerPassword = "owner-secret",
            Permissions = PdfPermissions.Print,
        });

        Assert.NotNull(spec.Encryption);
    }

    [Fact]
    public void UnrestrictedPermissionsWithNullOwnerPassword_Constructs()
    {
        var spec = DocumentWithEncryption(new EncryptionSpec { UserPassword = "user-secret" });

        Assert.NotNull(spec.Encryption);
        Assert.Null(spec.Encryption!.OwnerPassword);
        Assert.Equal(PdfPermissions.All, spec.Encryption.Permissions);
    }
}
