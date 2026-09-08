using VellumPdf.Layout.Core;
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
