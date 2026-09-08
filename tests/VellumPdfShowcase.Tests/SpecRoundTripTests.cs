using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using VellumPdf.Reader;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The test described in plan section 5.2: compile the C# that
/// <see cref="SpecCodeEmitter"/> produces with Roslyn, execute it, and assert
/// that the resulting PDF matches what <see cref="SpecRenderer"/> produced from
/// the same <see cref="DocumentSpec"/>. This is what keeps the two from
/// silently drifting apart; neither reads the other; this test is the only
/// thing that reads both.
/// </summary>
/// <remarks>
/// Roslyn scripting lives only in this test project. <c>src/VellumPdfShowcase.Web</c>
/// never references <c>Microsoft.CodeAnalysis.*</c>, so none of it reaches the
/// published Blazor bundle; the publish check in the verification pipeline
/// confirms this directly by inspecting <c>publish/wwwroot/_framework</c>.
/// </remarks>
public class SpecRoundTripTests
{
    [Fact]
    public async Task RoundTrip_EveryContentItemTypeFullyCustomised_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.EveryContentItemTypeFullyCustomised());

    [Fact]
    public async Task RoundTrip_EveryContentItemTypeAtDefault_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.EveryContentItemTypeAtDefault());

    [Fact]
    public async Task RoundTrip_PdfA2bWithOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PdfA2bWithOutputIntent());

    [Fact]
    public async Task RoundTrip_PdfA2bWithOutputIntent_IsReportedCompliantByPreflight()
    {
        var spec = DocumentSpecSamples.PdfA2bWithOutputIntent();
        var bytes = SpecRenderer.Render(spec);

        var profile = ConformanceMapping.ToPreflightProfile(spec.Conformance);
        Assert.NotNull(profile);

        var result = VellumPdf.Conformance.PdfPreflight.Validate(bytes, profile.Value);

        Assert.True(result.IsCompliant, string.Join('\n', result.Assertions.Select(a => a.ToString())));
    }

    /// <summary>
    /// Encryption introduces its own nondeterminism beyond the document
    /// identifier and the XMP timestamps: rendering the identical
    /// <see cref="DocumentSpec"/> through <see cref="SpecRenderer"/> twice
    /// produces two different encrypted byte streams, because the standard
    /// security handler derives its key material in part from the (already
    /// nondeterministic) document identifier. A plain
    /// <see cref="PdfNormalization.Normalize"/> comparison of the raw output
    /// therefore cannot pass here even when the two documents are logically
    /// identical, so this test decrypts both sides with the known password
    /// first and compares the plaintext that comes back.
    /// </summary>
    [Fact]
    public async Task RoundTrip_Encrypted_DecryptsToMatchingContent()
    {
        var spec = DocumentSpecSamples.Encrypted();
        var ownerPassword = spec.Encryption!.OwnerPassword!;

        var rendered = SpecRenderer.Render(spec);
        var scripted = await RunEmittedCodeAsync(spec);

        var decryptedRendered = DecryptWithPassword(rendered, ownerPassword);
        var decryptedScripted = DecryptWithPassword(scripted, ownerPassword);

        Assert.Equal(PdfNormalization.Normalize(decryptedRendered), PdfNormalization.Normalize(decryptedScripted));
    }

    private static async Task AssertRoundTripAsync(DocumentSpec spec)
    {
        var rendered = SpecRenderer.Render(spec);
        var scripted = await RunEmittedCodeAsync(spec);

        Assert.Equal(PdfNormalization.Normalize(rendered), PdfNormalization.Normalize(scripted));
    }

    private static async Task<byte[]> RunEmittedCodeAsync(DocumentSpec spec)
    {
        var code = SpecCodeEmitter.Emit(spec);
        var assets = SpecAssets.FromSpec(spec);

        var references = new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(MemoryStream).Assembly,
            typeof(VellumPdf.Layout.Document).Assembly,
            Assembly.Load("VellumPdf.Kernel"),
            Assembly.Load("System.Runtime"),
            Assembly.Load("System.Collections"),
        };

        var options = ScriptOptions.Default.WithReferences(references);
        var script = CSharpScript.Create<byte[]>(code, options, globalsType: typeof(SpecAssets));

        var diagnostics = script.Compile();
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, $"Emitted code failed to compile:\n{string.Join('\n', errors)}\n\n{code}");

        var state = await script.RunAsync(assets);
        return state.ReturnValue ?? throw new InvalidOperationException("The emitted script did not return a value.");
    }

    private static byte[] DecryptWithPassword(byte[] pdf, string password)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = password });
        using var stream = new MemoryStream();
        reader.SaveDecrypted(stream);
        return stream.ToArray();
    }
}
