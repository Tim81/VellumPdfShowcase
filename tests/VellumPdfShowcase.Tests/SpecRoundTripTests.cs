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
    /// Two lists (in fact all four list styles), two tables and two
    /// multi-run paragraphs in the same document. The emitted code names
    /// each with a document-wide counter, so a second <c>list</c>,
    /// <c>table</c> or <c>runs</c> local sharing the name of the first is a
    /// compile error this test would catch the moment it existed.
    /// </summary>
    [Fact]
    public async Task RoundTrip_MultipleListsTablesAndParagraphs_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.MultipleListsTablesAndParagraphs());

    /// <summary>
    /// A string holding the whole C0 control range plus NEL, LINE SEPARATOR
    /// and PARAGRAPH SEPARATOR, alongside a quote, a backslash and a brace
    /// pair. U+0085, U+2028 or U+2029 anywhere in visitor-supplied text
    /// terminates a naively emitted string literal mid-string, so this test
    /// fails to compile if <see cref="Generation.SpecCodeEmitter.Emit"/> ever
    /// stops escaping them.
    /// </summary>
    [Fact]
    public async Task RoundTrip_ControlCharactersAndLineSeparators_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.ControlCharactersAndLineSeparators());

    /// <summary>
    /// See the doc comment on <see cref="DocumentSpecSamples.SharedAndValueEqualRunStyles"/>.
    /// A two-run paragraph sharing one <c>TextStyleSpec</c> instance must
    /// render identically to one whose two runs are distinct but value-equal
    /// instances, since both <see cref="Generation.SpecRenderer"/> and
    /// <see cref="Generation.SpecCodeEmitter"/> merge on value equality.
    /// </summary>
    [Fact]
    public async Task RoundTrip_SharedAndValueEqualRunStyles_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.SharedAndValueEqualRunStyles());

    [Fact]
    public async Task RoundTrip_AdditionalCoverage_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.AdditionalCoverage());

    [Fact]
    public async Task RoundTrip_PdfA2aWithOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PdfA2aWithOutputIntent());

    [Fact]
    public async Task RoundTrip_PdfA2aWithOutputIntent_IsReportedCompliantByPreflight() =>
        await AssertPreflightCompliantAsync(DocumentSpecSamples.PdfA2aWithOutputIntent());

    /// <summary>PDF/UA-1, the fourth and last of the four conformance profiles plan section 6.2 requires.</summary>
    [Fact]
    public async Task RoundTrip_PdfUA1WithOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PdfUA1WithOutputIntent());

    /// <summary>
    /// A <see cref="PlainTextSpec"/> with no explicit style. This is the one
    /// content item that reads <see cref="DocumentSpec.DefaultTextStyle"/>
    /// through the library's <c>Document.Add(string, TextStyle?)</c>
    /// overload, so this test is what makes that property load-bearing: a
    /// renderer or emitter that stopped consulting it, or that stopped
    /// calling <c>Document.SetDefaultFont</c> with it, would produce a
    /// visibly different PDF and fail this comparison.
    /// </summary>
    [Fact]
    public async Task RoundTrip_PlainTextUsesDocumentDefault_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PlainTextUsesDocumentDefault());

    /// <summary>Covers the <see cref="CmykOutputIntentSpec"/> branch of both <see cref="SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>, which no other sample reaches.</summary>
    [Fact]
    public async Task RoundTrip_CmykOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.CmykOutputIntent());

    /// <summary>See the doc comment on <see cref="DocumentSpecSamples.RemainingBranchCoverage"/>.</summary>
    [Fact]
    public async Task RoundTrip_RemainingBranchCoverage_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.RemainingBranchCoverage());

    /// <summary>See the doc comment on <see cref="DocumentSpecSamples.RemainingEmitterBranchCoverage"/> (C4-C-M6).</summary>
    [Fact]
    public async Task RoundTrip_RemainingEmitterBranchCoverage_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.RemainingEmitterBranchCoverage());

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
    /// Decrypting first is not enough on its own.
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
    public async Task RoundTrip_Encrypted_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.Encrypted());

    /// <summary>
    /// <see cref="EncryptionSpec"/>'s own defaults: full permissions and
    /// metadata encryption left on. <see cref="Encrypted"/> above restricts
    /// permissions and disables metadata encryption, which routes around
    /// <c>SpecCodeEmitter.EmitPermissions</c>'s <c>PdfPermissions.All</c> fast
    /// path and its omit-when-default <c>EncryptMetadata</c> branch; this
    /// sample is what exercises both.
    /// </summary>
    [Fact]
    public async Task RoundTrip_EncryptedWithDefaults_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedWithDefaults());

    /// <summary>
    /// See the doc comment on <see cref="DocumentSpecSamples.EncryptedOwnerPasswordOnly"/>
    /// (C4-C-M5). Opening with an empty user password must succeed and must
    /// authenticate as the user, not the owner, since only the owner password
    /// was actually set.
    /// </summary>
    [Fact]
    public async Task RoundTrip_EncryptedOwnerPasswordOnly_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedOwnerPasswordOnly());

    /// <summary>
    /// See the doc comment on <see cref="DocumentSpecSamples.EncryptedNoOwnerPasswordUnrestricted"/>
    /// (C4-C-M5). This is the one sample that proves the recorded behaviour
    /// directly: opening with the user password authenticates as OWNER,
    /// because no distinct owner password was ever set.
    /// </summary>
    [Fact]
    public async Task RoundTrip_EncryptedNoOwnerPasswordUnrestricted_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedNoOwnerPasswordUnrestricted());

    /// <summary>
    /// Per the remark on <see cref="EncryptionSpec"/> (C4-C-M5): the password
    /// that actually authenticates full (owner) access is
    /// <see cref="EncryptionSpec.OwnerPassword"/> when set, otherwise
    /// <see cref="EncryptionSpec.UserPassword"/>; and the password that opens
    /// the document as the (non-owner) user is <see cref="EncryptionSpec.UserPassword"/>
    /// when set, otherwise the empty string. Both branches were previously
    /// unreachable because every sample set both passwords, and the helper
    /// dereferenced both with the null-forgiving operator.
    /// </summary>
    private static async Task AssertEncryptedRoundTripAsync(DocumentSpec spec)
    {
        var encryption = spec.Encryption!;
        var ownerAuthPassword = encryption.OwnerPassword ?? encryption.UserPassword ?? "";
        var userOpenPassword = encryption.UserPassword ?? "";
        var userPasswordGrantsOwnerAccess = encryption.OwnerPassword is null;

        var rendered = SpecRenderer.Render(spec);
        var scripted = await RunEmittedCodeAsync(spec);

        AssertEncryptionMatchesSpec(rendered, encryption, ownerAuthPassword);
        AssertEncryptionMatchesSpec(scripted, encryption, ownerAuthPassword);
        AssertPasswordAuthenticates(rendered, userOpenPassword, userPasswordGrantsOwnerAccess);
        AssertPasswordAuthenticates(scripted, userOpenPassword, userPasswordGrantsOwnerAccess);

        var decryptedRendered = DecryptWithPassword(rendered, ownerAuthPassword);
        var decryptedScripted = DecryptWithPassword(scripted, ownerAuthPassword);

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
    /// through <paramref name="ownerAuthPassword"/>, and asserts its
    /// <c>Permissions</c> and <c>EncryptMetadata</c> match what
    /// <paramref name="encryption"/> claims, and that the password used to
    /// open it actually authenticated as the owner. Opening with the
    /// password that authenticates as owner (rather than the user password)
    /// guarantees full access regardless of which permissions are in force.
    /// </summary>
    /// <remarks>
    /// Transposing <c>UserPassword</c> and <c>OwnerPassword</c> in the spec
    /// would leave every assertion here green if neither this method nor
    /// <see cref="AssertPasswordAuthenticates"/> asked which role actually
    /// authenticated; <c>Permissions</c> and <c>EncryptMetadata</c>
    /// are document-level and unaffected by which password is which.
    /// <see cref="VellumPdf.Encryption.PdfEncryptionInfo.IsOwnerAccess"/> pins that: it is
    /// <see langword="true"/> here because <paramref name="ownerAuthPassword"/>
    /// is, by construction, whichever password actually grants owner access
    /// (see the remark on <see cref="AssertEncryptedRoundTripAsync"/>). A
    /// negative assertion (this password does not also authenticate as the
    /// user password) is deliberately not made: at R&lt;=4 an owner password
    /// always also authenticates as the user password by specification, so
    /// that assertion would be false generally and would pass today only
    /// because <c>PdfEncryptionSettings</c> is fixed at AES-256 V5/R6.
    /// </remarks>
    private static void AssertEncryptionMatchesSpec(byte[] pdf, EncryptionSpec encryption, string ownerAuthPassword)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = ownerAuthPassword });
        var info = reader.Encryption;

        Assert.NotNull(info);
        Assert.True(info!.IsOwnerAccess);
        Assert.Equal(encryption.Permissions, info.Permissions);
        Assert.Equal(encryption.EncryptMetadata, info.EncryptMetadata);
    }

    /// <summary>
    /// Opens <paramref name="pdf"/> with <paramref name="password"/> and
    /// asserts whether it authenticates as owner matches
    /// <paramref name="expectOwnerAccess"/>. Neither <c>/U</c> nor <c>/UE</c>
    /// stores the plaintext user password, so this is the only way to
    /// confirm the password actually baked into the PDF matches the one the
    /// spec claims, short of decrypting: authenticating with a wrong
    /// password throws <see cref="VellumPdf.Reader.PdfPasswordException"/>.
    /// </summary>
    /// <remarks>
    /// See the remark on <see cref="AssertEncryptedRoundTripAsync"/>:
    /// <paramref name="expectOwnerAccess"/> is <see langword="true"/> only
    /// when <see cref="EncryptionSpec.OwnerPassword"/> is unset, which is
    /// exactly when the library authenticates the user password as owner.
    /// Otherwise this method catches the user and owner passwords being
    /// transposed, which the mere fact of authenticating does not.
    /// </remarks>
    private static void AssertPasswordAuthenticates(byte[] pdf, string password, bool expectOwnerAccess)
    {
        using var reader = PdfReader.Open(pdf, new PdfReaderOptions { Password = password });
        Assert.NotNull(reader.Encryption);
        Assert.Equal(expectOwnerAccess, reader.Encryption!.IsOwnerAccess);
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
