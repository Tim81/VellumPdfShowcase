using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

    /// <summary>
    /// Guards plan section 3.4.0.1 the way <see cref="TextStyleSpec_TwoEqualInstances_AreEqualHashAlikeAndCollideInADictionary"/>
    /// cannot. That test constructs both instances by naming every member
    /// this record has TODAY; a member added later defaults to
    /// <see langword="null"/> on both without either instance ever setting
    /// it, so the two stay equal regardless of the new member's own equality
    /// behaviour, and the test that is supposed to guard the invariant stays
    /// green while the invariant it names is silently broken. This test
    /// instead reflects over whatever members <see cref="TextStyleSpec"/>
    /// actually has, so it inspects a future member without anyone having to
    /// remember to teach it that member's name.
    /// <para>
    /// A member's type has value equality here when it is a value type (a
    /// <see langword="struct"/>, <see langword="enum"/>, or
    /// <see cref="Nullable{T}"/> of one; <see cref="ValueType.Equals(object?)"/>
    /// compares every field), a <see cref="string"/>, or a reference type
    /// that itself overrides <c>Equals(object?)</c> rather than inheriting
    /// <see cref="object.Equals(object?)"/>'s reference comparison, which is
    /// exactly what a C# <see langword="record"/> generates automatically and
    /// what an ordinary array or <see cref="List{T}"/> does NOT. An
    /// array- or list-typed member, the shape plan section 3.4.0.1 names by
    /// example (a dash pattern), is caught by this last rule: neither type
    /// overrides <c>Equals(object?)</c>, so this test fails the moment one is
    /// added, whether or not the two constructed instances happen to set it.
    /// </para>
    /// </summary>
    [Fact]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetMethod cannot observe a member removed by the linker.")]
    public void TextStyleSpec_EveryMember_HasValueEquality()
    {
        var properties = typeof(TextStyleSpec).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            Assert.True(
                HasValueEquality(property.PropertyType),
                $"TextStyleSpec.{property.Name} has type {property.PropertyType}, which does not implement " +
                "value equality. Record equality falls back to reference equality for it, which would let " +
                "two value-equal TextStyleSpec instances compare unequal; see the remark on TextStyleSpec.");
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Test-only reflection over this assembly's own types; never trimmed or published, so " +
            "Type.GetMethod cannot observe a member removed by the linker.")]
    private static bool HasValueEquality(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsValueType || underlyingType == typeof(string))
        {
            return true;
        }

        var equalsMethod = underlyingType.GetMethod(nameof(Equals), BindingFlags.Public | BindingFlags.Instance, [typeof(object)]);
        return equalsMethod is not null && equalsMethod.DeclaringType != typeof(object);
    }
}

/// <summary>
/// Every collection member snapshots the caller's value with a
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
/// <see cref="ListItemSpec.Children"/> caps nesting depth at
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
/// A restricted <see cref="EncryptionSpec.Permissions"/> value
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

    /// <summary>
    /// The null-or-empty guard alone is one keystroke from useless,
    /// because setting <see cref="EncryptionSpec.OwnerPassword"/> equal to
    /// <see cref="EncryptionSpec.UserPassword"/> satisfies it while
    /// reproducing exactly the defect it exists to prevent: there is still
    /// only one password, so it still authenticates as owner and the
    /// restricted permission set still binds nobody who can open the file.
    /// </summary>
    [Fact]
    public void RestrictedPermissionsWithOwnerPasswordEqualToUserPassword_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            DocumentWithEncryption(new EncryptionSpec { UserPassword = "same-secret", OwnerPassword = "same-secret", Permissions = PdfPermissions.Print }));
        Assert.Contains("OwnerPassword", exception.Message, StringComparison.Ordinal);
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

/// <summary>
/// Every collection breadth cap bounds how many distinct objects a
/// specification may hold, but the five collections below had no cap at all
/// before this fix, and <see cref="SpecLimits.MaxWalkedNodes"/> separately
/// bounds the total work a shared subtree can be walked into performing,
/// which no per-collection cap can prevent on its own.
/// </summary>
public class SpecSizeLimitBreadthTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void ListSpec_ItemsBeyondLimit_ThrowsAtConstruction()
    {
        List<ListItemSpec> items = [.. Enumerable.Range(0, SpecLimits.MaxListItems + 1).Select(i => new ListItemSpec { Text = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() => new ListSpec { Style = ListStyle.Unordered, Items = items });
        Assert.Contains("items", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListItemSpec_ChildrenBreadthBeyondLimit_ThrowsAtConstruction()
    {
        List<ListItemSpec> children = [.. Enumerable.Range(0, SpecLimits.MaxListItemChildren + 1).Select(i => new ListItemSpec { Text = $"{i}" })];

        var exception = Assert.Throws<ArgumentException>(() => new ListItemSpec { Text = "parent", Children = children });
        Assert.Contains("children", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParagraphSpec_RunsBeyondLimit_ThrowsAtConstruction()
    {
        List<TextRunSpec> runs = [.. Enumerable.Range(0, SpecLimits.MaxParagraphRuns + 1).Select(i => new TextRunSpec($"{i}", Style()))];

        var exception = Assert.Throws<ArgumentException>(() => new ParagraphSpec { Runs = runs });
        Assert.Contains("runs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFontsCountBeyondLimit_ThrowsAtConstruction()
    {
        List<byte[]> fonts = [.. Enumerable.Range(0, SpecLimits.MaxEmbeddedFonts + 1).Select(_ => new byte[] { 1, 2, 3 })];

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new PlainTextSpec { Text = "x" }],
                EmbeddedFonts = fonts,
            });
        Assert.Contains("embedded fonts", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableSpec_ColumnWidthsBeyondLimit_ThrowsAtConstruction()
    {
        List<double> widths = [.. Enumerable.Range(0, SpecLimits.MaxTableColumnWidths + 1).Select(i => (double)i)];

        var exception = Assert.Throws<ArgumentException>(() =>
            new TableSpec { Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "x" }] }], ColumnWidths = widths });
        Assert.Contains("column widths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The decisive case: a small number of distinct objects, each
/// individually within every per-collection cap, made to multiply through
/// sharing rather than genuine size. <see cref="SpecLimits.MaxWalkedNodes"/>
/// is the only control that can catch this, since it counts the walk itself
/// rather than the number of distinct objects constructed.
/// </summary>
public class WalkedNodeLimitTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// Builds a chain nested one level short of <see cref="SpecLimits.MaxListNestingDepth"/>,
    /// then references THE SAME chain instance from
    /// <see cref="SpecLimits.MaxListItemChildren"/> sibling slots under one
    /// top-level item, so every individual collection (the chain's own
    /// depth, the top item's breadth, the list's one item) sits at or under
    /// its own per-collection cap. Distinct objects number in the low
    /// hundreds, but a walk that visits a shared reference once per sibling
    /// slot, rather than once per distinct object, visits the chain's full
    /// depth on every one of the hundred slots, and that total must be
    /// rejected even though no individual collection is oversized.
    /// </summary>
    [Fact]
    public void SharedDeeplyNestedSubtree_ReferencedFromManySiblings_ThrowsAtConstruction()
    {
        // NOTE: every ListItemSpec.Text below is empty, deliberately. This
        // walk sums characters against SpecLimits.MaxTotalTextLength exactly
        // as it counts nodes against SpecLimits.MaxWalkedNodes, so any
        // non-trivial text on the thousands of node visits this shared chain
        // produces would trip the CHARACTER limit before the NODE limit this
        // test exists to isolate, and assert the wrong message. Re-checked
        // after MaxTotalTextLength changed from 100,000 to 20,000: this is
        // still true and not merely a leftover from a smaller budget. The
        // chain's own original text ("leaf", "level1".."level62") sums to 429
        // characters; repeating it across shared sibling slots reaches
        // MaxTotalTextLength's 20,000 characters at roughly the 47th sibling
        // (node 2,939), well short of the 80th sibling (node 5,001) needed to
        // reach MaxWalkedNodes. Restoring that text would make this test
        // exercise the character limit instead of the node limit it exists to
        // isolate.
        ListItemSpec chain = new() { Text = "" };
        for (var depth = 1; depth < SpecLimits.MaxListNestingDepth - 1; depth++)
        {
            chain = new ListItemSpec { Text = "", Children = [chain] };
        }

        var sharedChain = chain;
        List<ListItemSpec> siblings = [.. Enumerable.Repeat(sharedChain, SpecLimits.MaxListItemChildren)];
        var topItem = new ListItemSpec { Text = "", Children = siblings };

        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content =
                [
                    new ListSpec { Style = ListStyle.Unordered, Items = [topItem] },
                ],
            });
        Assert.Contains("walking", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counterpart to the case above: a modest, non-shared document, far
    /// smaller than any per-collection cap, must not be rejected. This is
    /// what keeps <see cref="SpecLimits.MaxWalkedNodes"/> from being so tight
    /// that it interferes with ordinary use.
    /// </summary>
    [Fact]
    public void OrdinaryModestDocument_Constructs()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content =
            [
                new HeadingSpec { Text = "Heading", Level = 0 },
                ParagraphSpec.FromText("A short paragraph.", Style()),
                new ListSpec { Style = ListStyle.Unordered, Items = [new ListItemSpec { Text = "One" }, new ListItemSpec { Text = "Two" }] },
                new TableSpec { Rows = [new TableRowSpec { Cells = [new TableCellSpec { Content = "Cell" }] }] },
            ],
        };

        Assert.NotNull(spec);
    }
}

/// <summary>
/// Every one of these values was previously unverified, because
/// every test that exercised a cap derived its own input from the constant
/// under test, so a test could pin the PRESENCE of a check without ever
/// pinning its MAGNITUDE. These tests assert the literal values instead, so
/// that changing a cap is a deliberate change to this file, not a silent
/// side effect of changing <see cref="SpecLimits"/> alone.
/// </summary>
public class SpecLimitsValuesAreVerifiedTests
{
    [Fact]
    public void Values_MatchTheDocumentedConstants()
    {
        Assert.Equal(20 * 1024 * 1024, SpecLimits.MaxAssetBytes);
        Assert.Equal(100_000, SpecLimits.MaxTextLength);
        Assert.Equal(35, SpecLimits.MaxLanguageTagLength);
        Assert.Equal(2048, SpecLimits.MaxUriLength);
        Assert.Equal(2_000, SpecLimits.MaxContentItems);
        Assert.Equal(2_000, SpecLimits.MaxTableRows);
        Assert.Equal(100, SpecLimits.MaxTableCellsPerRow);
        Assert.Equal(100, SpecLimits.MaxChartSlices);
        Assert.Equal(64, SpecLimits.MaxListNestingDepth);
        Assert.Equal(100, SpecLimits.MaxTableColumnWidths);
        Assert.Equal(1_000, SpecLimits.MaxParagraphRuns);
        Assert.Equal(2_000, SpecLimits.MaxListItems);
        Assert.Equal(100, SpecLimits.MaxListItemChildren);
        Assert.Equal(100, SpecLimits.MaxEmbeddedFonts);
        Assert.Equal(5_000, SpecLimits.MaxWalkedNodes);
        Assert.Equal(20_000, SpecLimits.MaxTotalTextLength);
        Assert.Equal(200, SpecLimits.MinPageDimensionPoints);
        Assert.Equal(20_000, SpecLimits.MaxPageDimensionPoints);
        Assert.Equal(36, SpecLimits.MaxFontSize);
        Assert.Equal(50, SpecLimits.MaxLeadingPoints);
        Assert.Equal(0, SpecLimits.MinHeadingLevel);
        Assert.Equal(5, SpecLimits.MaxHeadingLevel);
        Assert.Equal(10_000, SpecLimits.MaxEdgeInsetPoints);
        Assert.Equal(10_000, SpecLimits.MaxPieChartDiameterPoints);
        Assert.Equal(1_000, SpecLimits.MaxStrokeWidthPoints);
        Assert.Equal(10_000, SpecLimits.MaxIndentPoints);
        Assert.Equal(10_000, SpecLimits.MaxImageDimensionPoints);
        Assert.Equal(1_000, SpecLimits.MaxAngleMagnitudeRadians);
    }
}

/// <summary>
/// The nine collection members were snapshotted at construction, but
/// the <c>byte[]</c> arrays behind three of them were not, so the byte-level
/// validators (magic-byte sniffing, the ICC header check) were bypassable
/// after construction by overwriting the caller's own array in place.
/// </summary>
public class ByteArraySnapshotTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void ImageSpec_Bytes_SnapshotsAliasedArray()
    {
        var aliased = new byte[] { 1, 2, 3 };
        var spec = new ImageSpec { Format = ImageFormat.Png, Bytes = aliased };

        aliased[0] = 99;

        Assert.Equal(1, spec.Bytes[0]);
    }

    [Fact]
    public void PdfAOutputIntentSpec_IccProfile_SnapshotsAliasedArray()
    {
        var aliased = (byte[])DocumentSpecSamples.SrgbIccProfileBytes().Clone();
        var originalFirstByte = aliased[0];
        var spec = new PdfAOutputIntentSpec { IccProfile = aliased, ComponentCount = 3, OutputConditionIdentifier = "x" };

        aliased[0] = unchecked((byte)(aliased[0] + 1));

        Assert.Equal(originalFirstByte, spec.IccProfile[0]);
    }

    [Fact]
    public void DocumentSpec_EmbeddedFontsEntry_SnapshotsAliasedArray()
    {
        var aliased = new byte[] { 1, 2, 3, 4 };
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x" }],
            EmbeddedFonts = [aliased],
        };

        aliased[0] = 99;

        Assert.Equal(1, spec.EmbeddedFonts[0][0]);
    }

    /// <summary>
    /// The concrete security scenario the snapshot exists to close: a
    /// validated PNG's bytes overwritten, after construction, with BMP magic
    /// bytes. Without the copy, <see cref="DocumentSpec.Content"/>'s
    /// magic-byte check would have already passed against the original PNG
    /// bytes, and the renderer would go on to hand BMP bytes to
    /// <c>PngImageLoader</c>, which is exactly what the sniffing exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void DocumentSpec_ImageBytesOverwrittenAfterConstruction_CannotBypassFormatValidation()
    {
        byte[] validPngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        var aliased = (byte[])validPngSignature.Clone();

        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = aliased }],
        };

        // Overwrite the caller's own array with BMP magic bytes after construction.
        aliased[0] = 0x42;
        aliased[1] = 0x4D;

        var image = (ImageSpec)spec.Content[0];
        Assert.Equal(0x89, image.Bytes[0]);
        Assert.Equal(0x50, image.Bytes[1]);
    }
}
