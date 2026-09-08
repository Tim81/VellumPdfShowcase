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
    /// The regression test for C2-H1 and A3-M3: see the doc comment on
    /// <see cref="DocumentSpecSamples.SharedAndValueEqualRunStyles"/>. Before
    /// the fix, a two-run paragraph sharing one <c>TextStyleSpec</c> instance
    /// rendered as two merged runs on one side and two separate runs on the
    /// other, because only <see cref="Generation.SpecCodeEmitter"/> merged on
    /// value equality; <see cref="Generation.SpecRenderer"/> gave every run a
    /// fresh <c>TextStyle</c> instance regardless.
    /// </summary>
    [Fact]
    public async Task RoundTrip_SharedAndValueEqualRunStyles_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.SharedAndValueEqualRunStyles());

    /// <summary>The S4-M8 residual: see <see cref="DocumentSpecSamples.AdditionalCoverage"/>.</summary>
    [Fact]
    public async Task RoundTrip_AdditionalCoverage_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.AdditionalCoverage());

    /// <summary>The S4-M8 residual: PDF/A-2a, the one conformance profile left uncovered beyond PdfA2b and PdfA2u.</summary>
    [Fact]
    public async Task RoundTrip_PdfA2aWithOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PdfA2aWithOutputIntent());

    [Fact]
    public async Task RoundTrip_PdfA2aWithOutputIntent_IsReportedCompliantByPreflight() =>
        await AssertPreflightCompliantAsync(DocumentSpecSamples.PdfA2aWithOutputIntent());

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
    /// <c>EncryptMetadata</c> match what <paramref name="encryption"/> claims,
    /// and that the password used to open it actually authenticated as the
    /// owner. Opening with the owner password (rather than the user
    /// password) guarantees full access regardless of which permissions are
    /// in force.
    /// </summary>
    /// <remarks>
    /// C2-H2: transposing <c>UserPassword</c> and <c>OwnerPassword</c> in the
    /// spec used to leave every assertion here green, because neither this
    /// method nor <see cref="AssertPasswordAuthenticates"/> asked which role
    /// actually authenticated; <c>Permissions</c> and <c>EncryptMetadata</c>
    /// are document-level and unaffected by which password is which.
    /// <see cref="VellumPdf.Encryption.PdfEncryptionInfo.IsOwnerAccess"/> pins that: it is
    /// <see langword="true"/> here because this method always opens with
    /// <see cref="EncryptionSpec.OwnerPassword"/>. A negative assertion (this
    /// password does not also authenticate as the user password) is
    /// deliberately not made: at R&lt;=4 an owner password always also
    /// authenticates as the user password by specification, so that
    /// assertion would be false generally and would pass today only because
    /// <c>PdfEncryptionSettings</c> is fixed at AES-256 V5/R6.
    /// </remarks>
    private static void AssertEncryptionMatchesSpec(byte[] pdf, EncryptionSpec encryption)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = encryption.OwnerPassword! });
        var info = reader.Encryption;

        Assert.NotNull(info);
        Assert.True(info!.IsOwnerAccess);
        Assert.Equal(encryption.Permissions, info.Permissions);
        Assert.Equal(encryption.EncryptMetadata, info.EncryptMetadata);
    }

    /// <summary>
    /// Opens <paramref name="pdf"/> with <paramref name="password"/> and
    /// asserts that it authenticates as the user, not the owner. Neither
    /// <c>/U</c> nor <c>/UE</c> stores the plaintext user password, so this
    /// is the only way to confirm the password actually baked into the PDF
    /// matches the one the spec claims, short of decrypting: authenticating
    /// with a wrong password throws <see cref="VellumPdf.Reader.PdfPasswordException"/>.
    /// </summary>
    /// <remarks>
    /// See the C2-H2 remark on <see cref="AssertEncryptionMatchesSpec"/>:
    /// this method always opens with <see cref="EncryptionSpec.UserPassword"/>,
    /// so asserting <see cref="VellumPdf.Encryption.PdfEncryptionInfo.IsOwnerAccess"/> is
    /// <see langword="false"/> here is what catches the user and owner
    /// passwords being transposed, which the mere fact of authenticating
    /// does not.
    /// </remarks>
    private static void AssertPasswordAuthenticates(byte[] pdf, string password)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = password });
        Assert.NotNull(reader.Encryption);
        Assert.False(reader.Encryption!.IsOwnerAccess);
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
