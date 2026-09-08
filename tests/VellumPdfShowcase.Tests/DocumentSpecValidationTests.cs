using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// S4-M7: four ways to build a <see cref="DocumentSpec"/> the library cannot
/// render. Three are rejected at construction, with a message naming the
/// actual problem, because <see cref="PieChartSpec.Slices"/>,
/// <see cref="TableSpec.Rows"/> and <see cref="TableRowSpec.Cells"/> are
/// <see langword="required"/> properties with no sensible empty value. The
/// fourth, <see cref="DocumentSpec.Content"/>, defaults to empty, so it
/// remains constructible; <see cref="SpecRenderer"/> and
/// <see cref="SpecCodeEmitter"/> both reject it explicitly, before calling
/// into the library, with their own legible message rather than the
/// library's "The document has no pages."
/// </summary>
public class DocumentSpecValidationTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

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

    [Fact]
    public void Render_EmptyContent_ThrowsLegibleMessageRatherThanLibraryException()
    {
        var spec = new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style() };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
        Assert.Contains("Content", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no pages", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emit_EmptyContent_ThrowsLegibleMessageRatherThanLibraryException()
    {
        var spec = new DocumentSpec { Page = new PageSizeSpec(200, 200), DefaultTextStyle = Style() };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecCodeEmitter.Emit(spec));
        Assert.Contains("Content", exception.Message, StringComparison.Ordinal);
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
}
