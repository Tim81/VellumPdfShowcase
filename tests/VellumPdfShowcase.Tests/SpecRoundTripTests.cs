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
    public async Task RoundTrip_PdfA2bWithOutputIntent_IsReportedCompliantByPreflight() =>
        await AssertPreflightCompliantAsync(DocumentSpecSamples.PdfA2bWithOutputIntent());

    [Fact]
    public async Task RoundTrip_PdfA2uWithOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PdfA2uWithOutputIntent());

    [Fact]
    public async Task RoundTrip_PdfA2uWithOutputIntent_IsReportedCompliantByPreflight() =>
        await AssertPreflightCompliantAsync(DocumentSpecSamples.PdfA2uWithOutputIntent());

    /// <summary>
    /// The regression test for S4-H1: two lists (in fact all four list
    /// styles), two tables and two multi-run paragraphs in the same document.
    /// Before that fix, the emitted code declared a second <c>list</c>,
    /// <c>table</c> or <c>runs</c> local with the name of the first, which
    /// this test would have failed to compile the moment it existed.
    /// </summary>
    [Fact]
    public async Task RoundTrip_MultipleListsTablesAndParagraphs_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.MultipleListsTablesAndParagraphs());

    /// <summary>
    /// The regression test for S4-H2: a string holding the whole C0 control
    /// range plus NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR, alongside a
    /// quote, a backslash and a brace pair. Before that fix, U+0085, U+2028
    /// or U+2029 anywhere in visitor-supplied text terminated the emitted
    /// string literal mid-string and the script failed to compile.
    /// </summary>
    [Fact]
    public async Task RoundTrip_ControlCharactersAndLineSeparators_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.ControlCharactersAndLineSeparators());

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
    /// <remarks>
    /// S4-H3: decrypting first is not enough on its own.
    /// <see cref="PdfReader.SaveDecrypted(System.IO.Stream)"/> strips the
    /// whole <c>/Encrypt</c> dictionary, which is the only place
    /// <c>UserPassword</c>, <c>Permissions</c> and <c>EncryptMetadata</c> live,
    /// so a decrypted-content-only comparison would pass even if the PDF the
    /// visitor downloads carries a different password or permission set than
    /// the code displayed next to it. <see cref="AssertEncryptionMatchesSpec"/>
    /// and <see cref="AssertPasswordAuthenticates"/> read the <c>/Encrypt</c>
    /// dictionary itself, on both outputs, before either is decrypted.
    /// </remarks>
    [Fact]
    public async Task RoundTrip_Encrypted_DecryptsToMatchingContent()
    {
        var spec = DocumentSpecSamples.Encrypted();
        var encryption = spec.Encryption!;
        var ownerPassword = encryption.OwnerPassword!;
        var userPassword = encryption.UserPassword!;

        var rendered = SpecRenderer.Render(spec);
        var scripted = await RunEmittedCodeAsync(spec);

        AssertEncryptionMatchesSpec(rendered, encryption);
        AssertEncryptionMatchesSpec(scripted, encryption);
        AssertPasswordAuthenticates(rendered, userPassword);
        AssertPasswordAuthenticates(scripted, userPassword);

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

    private static async Task AssertPreflightCompliantAsync(DocumentSpec spec)
    {
        var bytes = SpecRenderer.Render(spec);

        var profile = ConformanceMapping.ToPreflightProfile(spec.Conformance);
        Assert.NotNull(profile);

        var result = VellumPdf.Conformance.PdfPreflight.Validate(bytes, profile.Value);

        Assert.True(result.IsCompliant, string.Join('\n', result.Assertions.Select(a => a.ToString())));
    }

    /// <summary>
    /// Reads the <c>/Encrypt</c> dictionary of <paramref name="pdf"/> directly,
    /// through the owner password, and asserts its <c>Permissions</c> and
    /// <c>EncryptMetadata</c> match what <paramref name="encryption"/> claims.
    /// Opening with the owner password (rather than the user password)
    /// guarantees full access regardless of which permissions are in force.
    /// </summary>
    private static void AssertEncryptionMatchesSpec(byte[] pdf, EncryptionSpec encryption)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = encryption.OwnerPassword! });
        var info = reader.Encryption;

        Assert.NotNull(info);
        Assert.Equal(encryption.Permissions, info!.Permissions);
        Assert.Equal(encryption.EncryptMetadata, info.EncryptMetadata);
    }

    /// <summary>
    /// Opens <paramref name="pdf"/> with <paramref name="password"/> and
    /// asserts that it authenticates. Neither <c>/U</c> nor <c>/UE</c> stores
    /// the plaintext user password, so this is the only way to confirm the
    /// password actually baked into the PDF matches the one the spec claims,
    /// short of decrypting: authenticating with a wrong password throws
    /// <see cref="VellumPdf.Reader.PdfPasswordException"/>.
    /// </summary>
    private static void AssertPasswordAuthenticates(byte[] pdf, string password)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = password });
        Assert.NotNull(reader.Encryption);
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
