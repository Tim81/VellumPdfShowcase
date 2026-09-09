using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The two <c>default</c> arms of <see cref="SpecCodeEmitter"/> that ARE
/// reachable from a fully validated <see cref="DocumentSpec"/>, unlike the
/// image-format arm documented on the private
/// <c>SpecCodeEmitter.ImageLoaderName</c>. Neither <see cref="ContentItemSpec"/>
/// nor <see cref="FontKind"/> is checked for exhaustiveness anywhere in the
/// model: <see cref="ContentItemSpec"/> is a public, non-sealed hierarchy any
/// assembly may extend, and <see cref="FontSpec.Kind"/> carries no range
/// check the way <see cref="ImageSpec.Format"/> does through
/// <see cref="ImageSignature"/>.
/// <para>
/// A round-trip byte comparison cannot exercise either arm, since both throw
/// before a single byte is produced, so these tests assert the throw and its
/// message directly, following the pattern already established by
/// <see cref="ConformanceMappingTests.ToPreflightProfile_UnrecognisedMember_Throws"/>.
/// </para>
/// </summary>
public class SpecCodeEmitterDefensiveThrowsTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// A <see cref="ContentItemSpec"/> outside the eight types
    /// <see cref="SpecCodeEmitter"/> recognises. <see cref="DocumentSpec.Content"/>'s
    /// own walk (<c>ContentWalkState.TryVisitNode</c>) falls through an
    /// unrecognised type without rejecting it, so this passes construction
    /// cleanly and reaches <c>SpecCodeEmitter.EmitContentItem</c> unchanged.
    /// </summary>
    private sealed record UnrecognisedContentItemSpec : ContentItemSpec;

    [Fact]
    public void Emit_UnrecognisedContentItemType_ThrowsArgumentOutOfRangeException()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new UnrecognisedContentItemSpec()],
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => SpecCodeEmitter.Emit(spec));
        Assert.Contains("Unrecognised content item type", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="FontSpec.Kind"/> is checked only for equality against
    /// <see cref="FontKind.Embedded"/> (to bound-check <see cref="FontSpec.EmbeddedFontIndex"/>),
    /// never for membership in <see cref="FontKind"/>'s two named values, so a
    /// value outside them passes every validation up to
    /// <c>SpecCodeEmitter.BuildTextStyleExpression</c>'s own switch.
    /// </summary>
    [Fact]
    public void Emit_UnrecognisedFontKind_ThrowsArgumentOutOfRangeException()
    {
        var invalidStyle = new TextStyleSpec { Font = new FontSpec { Kind = (FontKind)99 } };
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x", Style = invalidStyle }],
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => SpecCodeEmitter.Emit(spec));
        Assert.Contains("Unrecognised font kind", exception.Message, StringComparison.Ordinal);
    }
}
