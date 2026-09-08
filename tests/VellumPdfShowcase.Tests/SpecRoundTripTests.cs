using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using VellumPdf.Reader;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;
using DocumentConformance = VellumPdf.Document.PdfConformance;
using PreflightConformance = VellumPdf.Conformance.PdfConformance;

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
    public async Task RoundTrip_PdfA2uWithOutputIntent_MatchesSpecRenderer() =>
        await AssertRoundTripAsync(DocumentSpecSamples.PdfA2uWithOutputIntent());

    /// <summary>
    /// The structural guard: every <see cref="DocumentSpecSamples"/> member
    /// that returns a <see cref="DocumentSpec"/> claiming a conformance
    /// profile is preflighted, found by reflection rather than by a list of
    /// call sites someone has to remember to extend. Before this test
    /// existed, five samples claimed a profile
    /// (<see cref="DocumentSpecSamples.PdfA2bWithOutputIntent"/>,
    /// <see cref="DocumentSpecSamples.PdfA2uWithOutputIntent"/>,
    /// <see cref="DocumentSpecSamples.PdfA2aWithOutputIntent"/>,
    /// <see cref="DocumentSpecSamples.PdfUA1WithOutputIntent"/> and
    /// <see cref="DocumentSpecSamples.CmykOutputIntent"/>) but only the
    /// first three were ever preflighted, at three separate named
    /// <c>[Fact]</c> call sites; the last two claimed a profile and were
    /// never checked at all. A future sample that claims a profile is
    /// automatically included here the moment it is added to
    /// <see cref="DocumentSpecSamples"/>, with no second call site to
    /// remember. Those three original named facts have since been removed:
    /// every sample they covered claims a profile, so this theory already
    /// preflights each of them, and the separate facts were doing the same
    /// work a second time.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleNamesClaimingConformance))]
    public async Task Sample_ClaimingConformance_IsPreflightCompliant(string sampleName) =>
        await AssertPreflightCompliantAsync(InvokeSample(sampleName));

    /// <summary>
    /// Every sample claiming a conformance profile, checked directly against
    /// its own <see cref="HeadingSpec.Level"/> values rather than against a
    /// preflight verdict. Measured directly: raising the heading level in the
    /// PDF/UA-1 sample, or deleting that sample's <see cref="DocumentMetadataSpec.Title"/>,
    /// turns <see cref="Sample_ClaimingConformance_IsPreflightCompliant"/> red,
    /// because ISO 14289-1:2014 clauses 7.4.2 and 7.1 are genuinely enforced
    /// by preflight against PDF/UA-1. The IDENTICAL heading change against a
    /// PDF/A-2a or PDF/A-2b sample leaves that same theory fully green,
    /// because PDF/A preflight carries no heading-hierarchy rule at all (see
    /// the remark on <see cref="DocumentSpecSamples.PdfUA1WithOutputIntent"/>).
    /// That is not a defect in the library; ISO 19005-2:2011 clause 6.7.3.3
    /// carries only a requirement that the structure hierarchy exist and be
    /// rooted, plus a recommendation about granularity, so a sub-heading with
    /// no top-level heading above it passing PDF/A-2a is plausibly correct.
    /// The defect was on this side: every sample claiming PDF/A carried no
    /// guard on its own heading order, so it could regress silently, which is
    /// exactly how a sub-heading came to serve as a document's sole heading
    /// in several sample sites and survive review. This test closes that gap
    /// independently of what any profile's preflight rules happen to check,
    /// for every sample claiming any profile, present or future.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleNamesClaimingConformance))]
    public void Sample_ClaimingConformance_HasValidHeadingHierarchy(string sampleName)
    {
        var levels = InvokeSample(sampleName).Content.OfType<HeadingSpec>().Select(heading => heading.Level).ToList();

        if (levels.Count == 0)
        {
            return;
        }

        Assert.Equal(SpecLimits.MinHeadingLevel, levels[0]);

        var deepestSeen = levels[0];
        foreach (var level in levels.Skip(1))
        {
            Assert.True(
                level <= deepestSeen + 1,
                $"{sampleName} jumps from a deepest heading level of {deepestSeen} to {level}, skipping a level.");
            deepestSeen = Math.Max(deepestSeen, level);
        }
    }

    /// <summary>
    /// Every public, parameterless, <see cref="DocumentSpec"/>-returning
    /// method on <see cref="DocumentSpecSamples"/> whose result claims a
    /// conformance profile other than <see cref="DocumentConformance.None"/>.
    /// Returns names rather than constructed specs: xUnit theory data must be
    /// serialisable across discovery and execution, which a plain
    /// <see cref="DocumentSpec"/> is not.
    /// </summary>
    public static TheoryData<string> SampleNamesClaimingConformance()
    {
        TheoryData<string> names = [];

        foreach (var name in SampleFactoryMethods()
            .Where(method => ((DocumentSpec)method.Invoke(null, DefaultArguments(method))!).Conformance != DocumentConformance.None)
            .Select(method => method.Name))
        {
            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Every public, static, <see cref="DocumentSpec"/>-returning method on
    /// <see cref="DocumentSpecSamples"/> that a factory-style call site can
    /// invoke with no arguments: either genuinely parameterless, or every
    /// parameter optional. A plain parameter-count-zero check silently
    /// skipped the latter shape, so a sample factory taking an all-optional
    /// parameter would neither be preflighted here nor reachable by
    /// <see cref="InvokeSample"/>, with no error to say so.
    /// </summary>
    private static IEnumerable<MethodInfo> SampleFactoryMethods() =>
        typeof(DocumentSpecSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec) && method.GetParameters().All(p => p.IsOptional));

    /// <summary>The default value of every parameter of <paramref name="method"/>, in order, for invoking it as a no-argument factory.</summary>
    private static object?[] DefaultArguments(MethodInfo method) =>
        [.. method.GetParameters().Select(p => p.DefaultValue)];

    private static DocumentSpec InvokeSample(string sampleName)
    {
        var method = typeof(DocumentSpecSamples).GetMethod(sampleName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"DocumentSpecSamples has no public static member named {sampleName} returning a DocumentSpec.");
        return (DocumentSpec)method.Invoke(null, DefaultArguments(method))!;
    }

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
    /// The isolated-surrogate arm of <c>SpecCodeEmitter.Literal</c> cannot be
    /// proven by the round-trip byte comparison above: measured directly,
    /// Roslyn compiles a raw isolated surrogate sitting unescaped inside an
    /// ordinary string literal without complaint, and the resulting runtime
    /// string is byte-identical to what escaping it would have produced, so
    /// <see cref="RoundTrip_ControlCharactersAndLineSeparators_MatchesSpecRenderer"/>
    /// stays green whether or not that arm runs. What the arm actually
    /// guards, per the remark on <c>Literal</c>, is that the DISPLAYED
    /// snippet remains valid text once re-encoded as UTF-8, which an isolated
    /// surrogate cannot survive. This test checks that directly: the emitted
    /// snippet must decode back to itself after a UTF-8 round trip, which
    /// fails the instant a raw isolated surrogate reaches the output, since
    /// <see cref="Encoding.UTF8"/> substitutes U+FFFD for one on encoding.
    /// </summary>
    [Fact]
    public void Emit_ControlCharactersAndLineSeparators_TextSurvivesUtf8RoundTrip()
    {
        var code = SpecCodeEmitter.Emit(DocumentSpecSamples.ControlCharactersAndLineSeparators());
        var roundTripped = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(code));

        Assert.Equal(code, roundTripped);
    }

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

    /// <summary>See the doc comment on <see cref="DocumentSpecSamples.RemainingEmitterBranchCoverage"/>.</summary>
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

    /// <summary>See the doc comment on <see cref="DocumentSpecSamples.EncryptedWithDefaults"/>.</summary>
    [Fact]
    public async Task RoundTrip_EncryptedWithDefaults_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedWithDefaults());

    /// <summary>See the doc comment on <see cref="DocumentSpecSamples.EncryptedNoPermissions"/>.</summary>
    [Fact]
    public async Task RoundTrip_EncryptedNoPermissions_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedNoPermissions());

    /// <summary>
    /// See the doc comment on <see cref="DocumentSpecSamples.EncryptedOwnerPasswordOnly"/>.
    /// Opening with an empty user password must succeed and must
    /// authenticate as the user, not the owner, since only the owner password
    /// was actually set.
    /// </summary>
    [Fact]
    public async Task RoundTrip_EncryptedOwnerPasswordOnly_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedOwnerPasswordOnly());

    /// <summary>
    /// See the doc comment on <see cref="DocumentSpecSamples.EncryptedNoOwnerPasswordUnrestricted"/>.
    /// This is the one sample that proves the recorded behaviour
    /// directly: opening with the user password authenticates as OWNER,
    /// because no distinct owner password was ever set.
    /// </summary>
    [Fact]
    public async Task RoundTrip_EncryptedNoOwnerPasswordUnrestricted_DecryptsToMatchingContent() =>
        await AssertEncryptedRoundTripAsync(DocumentSpecSamples.EncryptedNoOwnerPasswordUnrestricted());

    /// <summary>
    /// Per the remark on <see cref="EncryptionSpec"/>: the password
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

    /// <summary>
    /// Preflights BOTH the rendered bytes and the scripted (emitted-and-executed)
    /// bytes for <paramref name="spec"/>, against the profile
    /// <see cref="DocumentSpec.Conformance"/> claims. The site's whole claim is
    /// that the displayed code produces the document shown beside it, so both
    /// must be conformant, not only the one the preview shows: a divergence
    /// between the two would previously surface only as a byte-comparison
    /// failure in <see cref="AssertRoundTripAsync"/>, which says nothing about
    /// whether either side is actually valid PDF/A or PDF/UA.
    /// </summary>
    private static async Task AssertPreflightCompliantAsync(DocumentSpec spec)
    {
        var profile = ConformanceMapping.ToPreflightProfile(spec.Conformance);
        Assert.NotNull(profile);

        var rendered = SpecRenderer.Render(spec);
        AssertPreflightCompliant(rendered, profile.Value, "Rendered");

        var scripted = await RunEmittedCodeAsync(spec);
        AssertPreflightCompliant(scripted, profile.Value, "Scripted");
    }

    private static void AssertPreflightCompliant(byte[] bytes, PreflightConformance profile, string label)
    {
        var result = VellumPdf.Conformance.PdfPreflight.Validate(bytes, profile);
        Assert.True(result.IsCompliant, $"{label} bytes were not preflight compliant:\n" + string.Join('\n', result.Assertions.Select(a => a.ToString())));
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
