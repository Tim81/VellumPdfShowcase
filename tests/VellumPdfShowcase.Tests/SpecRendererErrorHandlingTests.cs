using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Plan section 5.4 control 5: a malformed asset must surface as a legible
/// message rather than an unhandled exception from deep inside a Kernel
/// parser. Both byte arrays below pass the magic-byte sniff
/// <see cref="DocumentSpec.Content"/> already performs (control 2, and
/// there is no such sniff for a font) but are structurally invalid past
/// their signature, so only the try/catch in <see cref="SpecRenderer"/>
/// stands between them and a raw, undifferentiated exception from the
/// least-exercised code in the dependency chain.
/// </summary>
public class SpecRendererErrorHandlingTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void Render_WellSignedButMalformedPng_ThrowsLegibleInvalidOperationException()
    {
        byte[] truncatedPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF];
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = truncatedPng }],
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
        Assert.Contains("image", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_MalformedEmbeddedFont_ThrowsLegibleInvalidOperationException()
    {
        byte[] tooShortToBeAFont = [0x00, 0x01, 0x00, 0x00];
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            EmbeddedFonts = [tooShortToBeAFont],
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
        Assert.Contains("font", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
