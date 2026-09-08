using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// C4-F-M2: <c>document.SetDefaultFont</c> is emitted only when the
/// specification actually contains a <see cref="PlainTextSpec"/> left
/// unstyled, the one content item whose emitted code reads
/// <see cref="DocumentSpec.DefaultTextStyle"/>. Emitting it unconditionally,
/// as before this fix, produced a fully spelled-out expression that did
/// nothing in eleven of the twelve round-trip samples, sitting directly
/// above unstyled <c>ListItem</c> and <c>Cell</c> constructions it does not
/// affect. A round-trip byte comparison cannot catch this: the call is
/// inert whether or not it is present, so these tests assert on the emitted
/// text directly.
/// </summary>
public class SpecCodeEmitterDefaultFontTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    [Fact]
    public void Emit_NoUnstyledPlainText_OmitsSetDefaultFont()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new HeadingSpec { Text = "Heading", Level = 1 }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.DoesNotContain("SetDefaultFont", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_UnstyledPlainText_EmitsSetDefaultFont()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "Uses the default." }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.Contains("SetDefaultFont", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_StyledPlainTextOnly_OmitsSetDefaultFont()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "Explicitly styled.", Style = Style() }],
        };

        var code = SpecCodeEmitter.Emit(spec);

        Assert.DoesNotContain("SetDefaultFont", code, StringComparison.Ordinal);
    }
}
