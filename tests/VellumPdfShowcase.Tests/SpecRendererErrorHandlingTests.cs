using System.Reflection;
using VellumPdf.Encryption;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// A malformed asset must surface as a legible
/// message rather than an unhandled exception from deep inside a Kernel
/// parser. Both byte arrays below pass the magic-byte sniff
/// <see cref="DocumentSpec.Content"/> already performs (and
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

    /// <summary>
    /// Proves the asymmetry <see cref="SpecCodeEmitter.Emit"/>'s
    /// own exception contract now documents. <see cref="SpecCodeEmitter.Emit"/>
    /// emits TEXT referencing <c>Images[0]</c> by position; it never decodes
    /// the bytes themselves, so the SAME specification that makes
    /// <see cref="SpecRenderer.Render"/> throw <see cref="InvalidOperationException"/>
    /// (<see cref="Render_WellSignedButMalformedPng_ThrowsLegibleInvalidOperationException"/>
    /// above) makes <see cref="SpecCodeEmitter.Emit"/> succeed instead, with
    /// code that correctly references the same bad bytes.
    /// </summary>
    [Fact]
    public void Emit_WellSignedButMalformedPng_SucceedsWhereRenderThrows()
    {
        byte[] truncatedPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF];
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Format = ImageFormat.Png, Bytes = truncatedPng }],
        };

        Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));

        var code = SpecCodeEmitter.Emit(spec);
        Assert.Contains("PngImageLoader.Load(Images[0])", code, StringComparison.Ordinal);
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

    /// <summary>
    /// <see cref="DocumentSpec.Encryption"/>'s own
    /// construction-time check only compares <see cref="EncryptionSpec.OwnerPassword"/>
    /// against <see cref="EncryptionSpec.UserPassword"/> when
    /// <see cref="EncryptionSpec.Permissions"/> restricts anything; it returns
    /// early, without looking at either password at all, when
    /// <see cref="EncryptionSpec.Permissions"/> is <see cref="PdfPermissions.All"/>.
    /// The shipped library's own <c>Document.Encrypt</c> enforces a stricter,
    /// unconditional rule regardless of <see cref="EncryptionSpec.Permissions"/>:
    /// measured directly, an EMPTY (not null) <see cref="EncryptionSpec.OwnerPassword"/>
    /// beside a non-empty <see cref="EncryptionSpec.UserPassword"/> throws
    /// <see cref="ArgumentException"/> from <c>Document.Encrypt</c> itself,
    /// which is exactly the try/catch <see cref="SpecRenderer.Render"/> wraps
    /// that call in. This is therefore a genuinely reachable path through
    /// this model's own public API, not a defensive arm nothing can hit.
    /// </summary>
    [Fact]
    public void Render_EmptyOwnerPasswordBesideNonEmptyUserPassword_ThrowsLegibleInvalidOperationException()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = [new PlainTextSpec { Text = "x", Style = Style() }],
            Encryption = new EncryptionSpec
            {
                UserPassword = "user-secret",
                OwnerPassword = "",
                Permissions = PdfPermissions.All,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => SpecRenderer.Render(spec));
        Assert.Contains("encrypt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Makes <see cref="SpecRenderer.Render"/>'s exception contract executable
/// instead of a promise in prose: over the whole corpus, when Render throws,
/// the type must be <see cref="ArgumentException"/> (or a subtype) or
/// <see cref="InvalidOperationException"/>, and nothing else.
/// </summary>
/// <remarks>
/// The contract was stated in that remark for several rounds and was false for
/// most of them, each time because a member the model did not validate reached
/// an unwrapped library call: a null run style, three further null required
/// members, and an undefined <c>Standard14</c> face that produced a raw
/// <see cref="IndexOutOfRangeException"/>. Each was found by reading rather
/// than by any test. This is the test.
/// <para>
/// NOTE: what this can prove is bounded by the corpus. It asserts the contract
/// for the specifications this repository builds, not for every specification
/// the model admits. It is a regression guard, not a proof.
/// </para>
/// </remarks>
public class RenderExceptionContractTests
{
    [Theory]
    [MemberData(nameof(SymmetryTests.AllSampleNames), MemberType = typeof(SymmetryTests))]
    public void Sample_RenderThrowsOnlyContractedTypes(string sampleName)
    {
        var method = typeof(DocumentSpecSamples).GetMethod(sampleName, BindingFlags.Public | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, [.. method.GetParameters().Select(p => p.DefaultValue)])!;

        AssertContracted(spec);
    }

    [Theory]
    [MemberData(nameof(SymmetryTests.AdversarialSpecNames), MemberType = typeof(SymmetryTests))]
    public void AdversarialSpecification_RenderThrowsOnlyContractedTypes(string specName)
    {
        var method = typeof(SymmetryTests).GetMethod(specName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, null)!;

        AssertContracted(spec);
    }

    private static void AssertContracted(DocumentSpec spec)
    {
        try
        {
            SpecRenderer.Render(spec);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Contracted.
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"SpecRenderer.Render threw {ex.GetType().FullName} ({ex.Message}), which is outside its documented " +
                "exception contract of ArgumentException or InvalidOperationException.");
        }
    }
}

/// <summary>
/// The <see cref="Generation.SpecCodeEmitter.Emit"/> counterpart to
/// <see cref="RenderExceptionContractTests"/>. <see cref="Generation.SpecCodeEmitter.Emit"/>
/// has its own documented exception contract (see its remark), narrower than
/// <see cref="SpecRenderer.Render"/>'s own: it may throw
/// <see cref="ArgumentException"/> for a malformed specification the model
/// failed to reject, but NEVER <see cref="InvalidOperationException"/>, since
/// it emits text referencing asset bytes by position and never decodes,
/// parses, or executes any of them.
/// </summary>
/// <remarks>
/// Before this class existed, nothing made an unexpected exception from
/// <see cref="Generation.SpecCodeEmitter.Emit"/> fail the suite over either
/// corpus. <c>AdversarialCorpusTests</c> computes a <c>Crashed</c> verdict
/// that distinguishes a genuine crash from a documented refusal, but nothing
/// asserted against it: its own theories check only that SOME corpus member
/// matches a pattern (both consumers reject; Render refuses at run time while
/// Emit accepts), never that NO member crashed. Demonstrated directly:
/// injecting a <see cref="NullReferenceException"/> into
/// <see cref="Generation.SpecCodeEmitter.Emit"/> for a shape unique to one
/// adversarial corpus member left the suite green, with that member's
/// verdict merely moving from "refused as malformed" to "crashed" while the
/// corpus-level theories stayed satisfied by other members, and
/// <see cref="SymmetryTests"/>'s own agreement rule saw neither side
/// rejecting as malformed, so it saw no disagreement to report either. This
/// class closes that gap the same way <see cref="RenderExceptionContractTests"/>
/// closes it for <see cref="SpecRenderer.Render"/>: it asserts the contract
/// directly, over the same sample and adversarial corpora, so a crash in
/// <see cref="Generation.SpecCodeEmitter.Emit"/> on any corpus member fails
/// here regardless of what any other member's verdict happens to be.
/// <para>
/// NOTE: what this can prove is bounded by the corpus, exactly as
/// <see cref="RenderExceptionContractTests"/>'s own note states. It is a
/// regression guard, not a proof.
/// </para>
/// </remarks>
public class EmitExceptionContractTests
{
    [Theory]
    [MemberData(nameof(SymmetryTests.AllSampleNames), MemberType = typeof(SymmetryTests))]
    public void Sample_EmitThrowsOnlyContractedTypes(string sampleName)
    {
        var method = typeof(DocumentSpecSamples).GetMethod(sampleName, BindingFlags.Public | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, [.. method.GetParameters().Select(p => p.DefaultValue)])!;

        AssertContracted(spec);
    }

    [Theory]
    [MemberData(nameof(SymmetryTests.AdversarialSpecNames), MemberType = typeof(SymmetryTests))]
    public void AdversarialSpecification_EmitThrowsOnlyContractedTypes(string specName)
    {
        var method = typeof(SymmetryTests).GetMethod(specName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var spec = (DocumentSpec)method.Invoke(null, null)!;

        AssertContracted(spec);
    }

    private static void AssertContracted(DocumentSpec spec)
    {
        try
        {
            SpecCodeEmitter.Emit(spec);
        }
        catch (ArgumentException)
        {
            // Contracted.
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"SpecCodeEmitter.Emit threw {ex.GetType().FullName} ({ex.Message}), which is outside its " +
                "documented exception contract of ArgumentException only. Its contract promises it never throws " +
                "InvalidOperationException, since it never decodes, parses, or executes any asset bytes, and any " +
                "other exception type is not a documented rejection of any kind.");
        }
    }
}
