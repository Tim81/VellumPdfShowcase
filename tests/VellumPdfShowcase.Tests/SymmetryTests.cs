using System.Reflection;
using VellumPdf.Encryption;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Round nine's central change: the product's invariant is that
/// <see cref="SpecRenderer.Render"/> and <see cref="SpecCodeEmitter.Emit"/>
/// cannot disagree about a <see cref="DocumentSpec"/>. That invariant had been
/// approximated by branch coverage, and the approximation actively caused
/// defects: pressure toward 100% coverage deleted defensive arms from
/// <see cref="SpecRenderer"/> while <see cref="SpecCodeEmitter"/>'s
/// equivalents survived because some test happened to reach them, producing
/// two live divergences (HIGH 1: an unrecognised <see cref="ContentItemSpec"/>
/// rendered as though absent while <see cref="SpecCodeEmitter"/> threw; HIGH
/// 2: an out-of-range <see cref="FontKind"/> rendered as Helvetica while
/// <see cref="SpecCodeEmitter"/> threw) with the branch-coverage gate fully
/// green throughout. This file asserts the invariant DIRECTLY, over a corpus
/// of every existing <see cref="DocumentSpecSamples"/> entry plus adversarial
/// specifications that pass construction, rather than relying on coverage as
/// a proxy for it. It is the PRIMARY guard for this property; the
/// branch-coverage gate remains a secondary check that every line is
/// reachable, not that the two consumers agree about what it produces.
/// </summary>
/// <remarks>
/// The invariant asserted here is narrower than "both succeed or both
/// reject", and deliberately so. <see cref="SpecCodeEmitter.Emit"/>'s own
/// documented contract (see its remark) promises it never throws
/// <see cref="InvalidOperationException"/>: it emits TEXT referencing
/// <see cref="DocumentSpec"/>'s own asset bytes and other values by position,
/// and never decodes, parses, or executes any of them, so a specification
/// that constructs cleanly but trips a library-level refusal only
/// <see cref="SpecRenderer.Render"/> can discover (a well-signed but
/// structurally malformed image or font, an output intent the library itself
/// rejects, <see cref="DocumentSpec.UseObjectStreams"/> combined with
/// <see cref="DocumentSpec.Encryption"/>, a PDF/A <see cref="DocumentSpec.Conformance"/>
/// claim combined with <see cref="DocumentSpec.Encryption"/>) is EXPECTED to
/// make <see cref="SpecRenderer.Render"/> throw <see cref="InvalidOperationException"/>
/// while <see cref="SpecCodeEmitter.Emit"/> succeeds. That asymmetry is
/// documented and deliberate, proven directly by
/// <see cref="SpecRendererErrorHandlingTests"/> and
/// <see cref="SpecRendererOutputIntentErrorHandlingTests"/>, not by this
/// guard.
/// <para>
/// What this guard DOES assert, unconditionally, over every specification in
/// its corpus: if EITHER consumer rejects a specification with an
/// <see cref="ArgumentException"/>-family exception ("a malformed spec the
/// MODEL failed to reject", per both methods' own documented contract), the
/// OTHER must reject it too. A specification that constructs successfully is,
/// by both contracts, supposed to be well-formed enough that neither
/// consumer's own per-content-type dispatch can be surprised by it; an
/// ArgumentException from one side and silent success (or an unrelated
/// exception) from the other means one consumer's switch tolerates a shape
/// the other's cannot handle, which is EXACTLY the shape of HIGH 1 and HIGH
/// 2. This is asserted by exception FAMILY (does it derive from
/// ArgumentException), not exact subtype: both contracts only promise the
/// family, and the two consumers are not required to throw the identical
/// ArgumentException subtype for the identical reason.
/// </para>
/// <para>
/// PROOF this guard actually protects the property, rather than passing
/// vacuously: with <c>DocumentSpec.IsRecognisedContentItemType</c>'s call
/// site in <c>ValidateContent</c> commented out (reintroducing HIGH 1) and,
/// separately, with <c>FontSpec.Kind</c>'s validation reverted to a bare
/// auto-property (reintroducing HIGH 2), each of the two shapes below
/// constructs successfully again, and feeding the result to
/// <see cref="RenderAndEmitAgreeOnArgumentRejection"/> fails immediately,
/// naming the exact disagreement (Render succeeds silently; Emit throws
/// ArgumentOutOfRangeException). Both reproductions were run by hand during
/// this fix and are not committed as failing tests, since the fix they
/// prove is already in place; <see cref="UnrecognisedContentItem_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt"/>
/// and <see cref="OutOfRangeFontKind_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt"/>
/// below are the permanent regression guard: as long as construction itself
/// rejects both shapes, neither can ever reach <see cref="SpecRenderer.Render"/>
/// or <see cref="SpecCodeEmitter.Emit"/> for the two to disagree about.
/// </para>
/// </remarks>
public class SymmetryTests
{
    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(VellumPdf.Fonts.Standard14.Helvetica) };

    /// <summary>
    /// Runs <paramref name="action"/>, reporting whether it threw and, if so,
    /// what it threw, without letting the exception propagate.
    /// </summary>
    private static (bool Threw, Exception? Exception) TryRun(Action action)
    {
        try
        {
            action();
            return (false, null);
        }
        catch (Exception ex)
        {
            return (true, ex);
        }
    }

    /// <summary>
    /// The primary assertion this file exists to make. See the class remark
    /// for exactly what is, and is not, required to agree.
    /// </summary>
    internal static void RenderAndEmitAgreeOnArgumentRejection(DocumentSpec spec)
    {
        var (renderThrew, renderException) = TryRun(() => SpecRenderer.Render(spec));
        var (emitThrew, emitException) = TryRun(() => SpecCodeEmitter.Emit(spec));

        var renderRejectedAsMalformed = renderThrew && renderException is ArgumentException;
        var emitRejectedAsMalformed = emitThrew && emitException is ArgumentException;

        Assert.True(
            renderRejectedAsMalformed == emitRejectedAsMalformed,
            "SpecRenderer.Render and SpecCodeEmitter.Emit disagree about whether this specification is " +
            $"malformed. Render {Describe(renderThrew, renderException)}; Emit {Describe(emitThrew, emitException)}. " +
            "A specification the model let through construction must not look well-formed to one consumer's " +
            "own dispatch and malformed to the other's.");

        // Emit's own documented contract: it may throw ArgumentException, but
        // never InvalidOperationException, since it never executes anything.
        if (emitThrew)
        {
            Assert.False(
                emitException is InvalidOperationException,
                $"SpecCodeEmitter.Emit threw InvalidOperationException ({emitException!.Message}), which its own " +
                "documented exception contract promises never happens.");
        }
    }

    private static string Describe(bool threw, Exception? exception) =>
        threw ? $"threw {exception!.GetType().Name}: {exception.Message}" : "succeeded";

    /// <summary>
    /// Every public, parameterless, <see cref="DocumentSpec"/>-returning
    /// factory on <see cref="DocumentSpecSamples"/>, the same reflection
    /// <see cref="SpecRoundTripTests"/> uses, reused here rather than
    /// duplicated so a new sample is automatically added to this corpus too.
    /// </summary>
    public static TheoryData<string> AllSampleNames()
    {
        TheoryData<string> names = [];
        foreach (var method in typeof(DocumentSpecSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec) && method.GetParameters().All(p => p.IsOptional)))
        {
            names.Add(method.Name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(AllSampleNames))]
    public void Sample_RenderAndEmitAgree(string sampleName)
    {
        var method = typeof(DocumentSpecSamples).GetMethod(sampleName, BindingFlags.Public | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, [.. method.GetParameters().Select(p => p.DefaultValue)])!;

        RenderAndEmitAgreeOnArgumentRejection(spec);
    }

    /// <summary>
    /// Adversarial specifications: each constructs successfully (passes
    /// every model-level check) but is chosen specifically to be a shape a
    /// per-content-type or per-feature dispatch could plausibly handle
    /// differently on the two sides. Named rather than anonymous, so a
    /// failure names the scenario directly.
    /// </summary>
    public static TheoryData<string> AdversarialSpecNames()
    {
        TheoryData<string> names = [];
        foreach (var method in typeof(SymmetryTests)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec) && method.Name.EndsWith("Specification", StringComparison.Ordinal)))
        {
            names.Add(method.Name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(AdversarialSpecNames))]
    public void AdversarialSpecification_RenderAndEmitAgree(string specName)
    {
        var method = typeof(SymmetryTests).GetMethod(specName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, null)!;

        RenderAndEmitAgreeOnArgumentRejection(spec);
    }

    /// <summary>
    /// A well-signed but structurally malformed PNG. Deliberately asymmetric
    /// (Render throws InvalidOperationException; Emit succeeds): included
    /// here anyway, to prove the guard does NOT misfire on the documented
    /// asymmetry it explicitly carves out.
    /// </summary>
    private static DocumentSpec MalformedButWellSignedImageSpecification()
    {
        byte[] truncatedPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF];
        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = truncatedPng }],
        };
    }

    /// <summary>Same shape as <see cref="MalformedButWellSignedImageSpecification"/>, for an embedded font instead of an image.</summary>
    private static DocumentSpec MalformedEmbeddedFontSpecification()
    {
        byte[] tooShortToBeAFont = [0x00, 0x01, 0x00, 0x00];
        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            EmbeddedFonts = [tooShortToBeAFont],
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        };
    }

    /// <summary>
    /// An empty (not null) OwnerPassword beside a non-empty UserPassword and
    /// unrestricted Permissions: passes DocumentSpec.Encryption's own
    /// construction-time check (which only fires when Permissions restricts
    /// anything), but Document.Encrypt itself refuses it unconditionally.
    /// Deliberately asymmetric for the same reason as the two above.
    /// </summary>
    private static DocumentSpec EmptyOwnerPasswordSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        Encryption = new EncryptionSpec { UserPassword = "user-secret", OwnerPassword = "", Permissions = PdfPermissions.All },
    };

    /// <summary>
    /// An ICC output intent declaring a colour space <see cref="IccProfileHeader"/>
    /// does not recognise, with a ComponentCount the library itself rejects.
    /// Passes DocumentSpec.OutputIntent's own construction-time check (which
    /// only cross-validates a recognised colour space) but
    /// Document.SetPdfAOutputIntent refuses it. Deliberately asymmetric for
    /// the same reason as the three above.
    /// </summary>
    private static DocumentSpec OutputIntentRejectedByLibrarySpecification()
    {
        var profile = (byte[])DocumentSpecSamples.SrgbIccProfileBytes().Clone();
        profile[16] = (byte)'2';
        profile[17] = (byte)'C';
        profile[18] = (byte)'L';
        profile[19] = (byte)'R';

        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Conformance = VellumPdf.Document.PdfConformance.PdfA2b,
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
            OutputIntent = new PdfAOutputIntentSpec { IccProfile = profile, ComponentCount = 2, OutputConditionIdentifier = "x" },
        };
    }

    /// <summary>
    /// UseObjectStreams combined with Encryption: DocumentSpec does not guard
    /// this combination at construction (see its own remark), and
    /// Document.Save refuses it with NotSupportedException, wrapped by
    /// SpecRenderer into InvalidOperationException. Deliberately asymmetric
    /// for the same reason as the specifications above: Emit never calls
    /// Document.Save, so it cannot discover this.
    /// </summary>
    private static DocumentSpec ObjectStreamsWithEncryptionSpecification() => new()
    {
        Page = new PageSizeSpec(200, 200),
        DefaultTextStyle = Style(),
        UseObjectStreams = true,
        Content = [new PlainTextSpec { Text = "x", Style = Style() }],
        Encryption = new EncryptionSpec { UserPassword = "user-secret" },
    };

    /// <summary>An unrecognised <see cref="ContentItemSpec"/> subtype, private to this file, fed directly to <see cref="DocumentSpec.Content"/> below rather than reaching into another test file's own nested type.</summary>
    private sealed record UnrecognisedContentItemSpecForSymmetryProof : ContentItemSpec;

    /// <summary>
    /// HIGH 1's exact shape, proven here rather than left implicit: an
    /// unrecognised <see cref="ContentItemSpec"/> subtype cannot even be
    /// CONSTRUCTED into a <see cref="DocumentSpec"/> any more
    /// (<see cref="DocumentSpec.Content"/> rejects it), which is what
    /// guarantees <see cref="SpecRenderer.Render"/> and
    /// <see cref="SpecCodeEmitter.Emit"/> agree about it: neither can ever be
    /// called with it at all. Before the fix, this assertion failed (the
    /// specification constructed successfully), and feeding the resulting
    /// spec to <see cref="RenderAndEmitAgreeOnArgumentRejection"/> failed
    /// too: Render succeeded silently while Emit threw
    /// <see cref="ArgumentOutOfRangeException"/>, exactly the disagreement
    /// this guard exists to catch. Both failures were reproduced by hand
    /// during this fix (see the class remark's PROOF paragraph) and are not
    /// committed as failing tests, since the fix is already in place.
    /// </summary>
    [Fact]
    public void UnrecognisedContentItem_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DocumentSpec
            {
                Page = new PageSizeSpec(200, 200),
                DefaultTextStyle = Style(),
                Content = [new UnrecognisedContentItemSpecForSymmetryProof()],
            });

        Assert.Contains("neither SpecRenderer nor SpecCodeEmitter recognises", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Mirrors <see cref="UnrecognisedContentItem_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt"/> for HIGH 2: an out-of-range <see cref="FontKind"/>, also now rejected at construction, for the identical reason.</summary>
    [Fact]
    public void OutOfRangeFontKind_CannotBeConstructed_SoRenderAndEmitCannotDisagreeAboutIt()
    {
        var exception = Assert.Throws<ArgumentException>(() => new FontSpec { Kind = (FontKind)99 });
        Assert.Contains("Kind", exception.Message, StringComparison.Ordinal);
    }
}
