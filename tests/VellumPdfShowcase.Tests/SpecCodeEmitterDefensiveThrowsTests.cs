using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The symmetry gap round nine found HIGH: <see cref="ContentItemSpec"/> is a
/// public, non-sealed hierarchy any assembly may extend, and <see cref="FontSpec.Kind"/>
/// carried no range check, so both an unrecognised content item and an
/// out-of-range <see cref="FontKind"/> could reach a fully constructed
/// <see cref="DocumentSpec"/>. <see cref="Generation.SpecRenderer"/>'s own
/// switches carried no <see langword="default"/> arm at all (silently
/// rendering as though the item were absent, or Helvetica for the font case),
/// while <see cref="Generation.SpecCodeEmitter"/>'s DID, and threw: the two
/// consumers disagreed about the identical <see cref="DocumentSpec"/>, which
/// is exactly the divergence CLAUDE.md's round-trip invariant forbids.
/// <para>
/// The fix moved both checks to construction, in <see cref="DocumentSpec.Content"/>
/// and <see cref="FontSpec.Kind"/> respectively, rather than adding a matching
/// defensive arm to <see cref="Generation.SpecRenderer"/>: a value that cannot
/// reach either consumer's switch needs no arm, on either side, to reject it
/// there. These tests now pin the REJECTION at construction, the narrowest
/// point it can be pinned, rather than at <see cref="Generation.SpecCodeEmitter.Emit"/>,
/// which the offending value can no longer reach at all.
/// </para>
/// </summary>
public class SpecCodeEmitterDefensiveThrowsTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// A <see cref="ContentItemSpec"/> outside the eight types both consumers
    /// recognise. Before the fix, <see cref="DocumentSpec.Content"/>'s own
    /// walk (<c>ContentWalkState.TryVisitNode</c>) fell through it without
    /// rejecting it, so it passed construction cleanly; measured directly, a
    /// document containing one alongside an ordinary <see cref="PlainTextSpec"/>
    /// rendered byte-identical output to one without it, while
    /// <see cref="Generation.SpecCodeEmitter.Emit"/> threw for the same
    /// specification.
    /// </summary>
    private sealed record UnrecognisedContentItemSpec : ContentItemSpec;

    [Fact]
    public void DocumentSpec_UnrecognisedContentItemType_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new UnrecognisedContentItemSpec()],
            });

        Assert.Contains("neither SpecRenderer nor SpecCodeEmitter recognises", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <see langword="null"/> item in <see cref="DocumentSpec.Content"/>.
    /// Before the fix, the same silent fall-through as
    /// <see cref="UnrecognisedContentItemSpec"/> above swallowed it: a
    /// switch on a <see langword="null"/> value matches no
    /// <c>case ContentItemSpec-subtype</c> pattern and falls to whatever the
    /// switch does for an unmatched value, so it passed construction, was
    /// skipped by <see cref="Generation.SpecRenderer.Render"/>, and surfaced
    /// only later and unhelpfully, as "The document has no pages" once every
    /// other item had ALSO been skipped, rather than as a message naming the
    /// actual problem.
    /// </summary>
    [Fact]
    public void DocumentSpec_NullContentItem_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [null!],
            });

        Assert.Contains("Content", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="FontSpec.Kind"/> is a public, <see langword="required"/>
    /// <see langword="init"/> member of a public enumeration with two named
    /// values and, before the fix, no range check at all: reachable directly
    /// through <c>new FontSpec { Kind = (FontKind)99 }</c>, not only through
    /// the two factory methods. Measured directly: a style built this way
    /// rendered byte-identical to Helvetica through <see cref="Generation.SpecRenderer"/>,
    /// while <see cref="Generation.SpecCodeEmitter.Emit"/> threw for the
    /// identical specification.
    /// </summary>
    [Fact]
    public void FontSpec_UnrecognisedKind_ThrowsAtConstruction()
    {
        var exception = Assert.Throws<ArgumentException>(() => new FontSpec { Kind = (FontKind)99 });
        Assert.Contains("Kind", exception.Message, StringComparison.Ordinal);
    }
}
