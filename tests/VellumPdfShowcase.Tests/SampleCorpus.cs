using System.Reflection;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The one definition of what the sample corpus IS, so that every suite drawing
/// on it draws on the same set.
/// </summary>
/// <remarks>
/// The rule lived in three places before this type existed: once in
/// <see cref="SpecRoundTripTests"/> as a reflective helper, once in
/// <see cref="SymmetryTests"/> as a byte-for-byte copy of that helper, and once
/// as nineteen hand-written call sites naming each sample individually. A new
/// sample was therefore covered by the symmetry guard automatically and by the
/// round trip only if someone remembered to add a nineteenth-plus call site,
/// which is the failure this removes: the round trip is now a theory over this
/// corpus.
/// <para>
/// NOTE: this is a type of its own rather than a member of
/// <see cref="DocumentSpecSamples"/> on purpose. The discovery rule reflects
/// over every public static <see cref="DocumentSpec"/>-returning method of that
/// class, so a helper added there would enrol itself as a sample.
/// </para>
/// <para>
/// NOTE: the theories carry sample NAMES rather than constructed specifications.
/// xUnit theory data must survive discovery and execution separately, which a
/// <see cref="DocumentSpec"/> does not.
/// </para>
/// </remarks>
public static class SampleCorpus
{
    /// <summary>
    /// Every public, static, <see cref="DocumentSpec"/>-returning method on
    /// <see cref="DocumentSpecSamples"/> a factory-style call site can invoke
    /// with no arguments: either genuinely parameterless, or every parameter
    /// optional. A plain parameter-count-zero check silently skipped the latter
    /// shape.
    /// </summary>
    public static IEnumerable<MethodInfo> FactoryMethods() =>
        typeof(DocumentSpecSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec) && method.GetParameters().All(p => p.IsOptional));

    /// <summary>The default value of every parameter of <paramref name="method"/>, in order, for invoking it as a no-argument factory.</summary>
    public static object?[] DefaultArguments(MethodInfo method) =>
        [.. method.GetParameters().Select(p => p.DefaultValue)];

    public static IEnumerable<string> Names() => FactoryMethods().Select(method => method.Name);

    public static TheoryData<string> AllSampleNames()
    {
        TheoryData<string> names = [];
        foreach (var name in Names())
        {
            names.Add(name);
        }

        return names;
    }

    public static DocumentSpec Invoke(string sampleName)
    {
        var method = typeof(DocumentSpecSamples).GetMethod(sampleName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"DocumentSpecSamples has no public static member named {sampleName} returning a DocumentSpec.");

        return (DocumentSpec)method.Invoke(null, DefaultArguments(method))!;
    }
}

/// <summary>
/// Pins that the corpus is discoverable and invocable, which is what the
/// reflective rule can get wrong without any suite noticing: a factory whose
/// shape stops matching the predicate simply vanishes from every theory that
/// draws on it, and those theories then pass with fewer cases.
/// </summary>
public class SampleCorpusTests
{
    [Fact]
    public void Corpus_IsNotEmpty() => Assert.NotEmpty(SampleCorpus.Names());

    /// <summary>
    /// Every discovered factory must actually invoke as a no-argument factory
    /// and return a specification. The predicate admits methods whose
    /// parameters are all optional, so this is what proves
    /// <see cref="SampleCorpus.DefaultArguments"/> matches the predicate.
    /// </summary>
    [Fact]
    public void EveryDiscoveredSample_Invokes()
    {
        foreach (var name in SampleCorpus.Names())
        {
            Assert.NotNull(SampleCorpus.Invoke(name));
        }
    }

    /// <summary>
    /// Every public factory on <see cref="DocumentSpecSamples"/> must be IN the
    /// corpus. A sample added with a shape the predicate does not match is
    /// otherwise covered by nothing at all.
    /// </summary>
    /// <remarks>
    /// NOTE: this used <see cref="Assert.Equal{T}(System.Collections.Generic.ISet{T}, System.Collections.Generic.ISet{T})"/>
    /// directly over two <see cref="HashSet{T}"/> instances, which prints both
    /// sides truncated at the same five elements and so names neither side's
    /// actual difference; <see cref="RosterAssertions.AssertSameRoster"/>
    /// replaces it with the same treatment
    /// <see cref="Corpus_ContainsExactlyTheExpectedRoster"/> uses.
    /// </remarks>
    [Fact]
    public void EveryPublicSampleFactory_IsDiscovered()
    {
        var declared = typeof(DocumentSpecSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec))
            .Select(method => method.Name);

        RosterAssertions.AssertSameRoster(declared, SampleCorpus.Names());
    }

    /// <summary>
    /// The fixed roster of every sample <see cref="DocumentSpecSamples"/> is
    /// expected to declare, named explicitly with <see langword="nameof"/>
    /// rather than derived by the same reflection rule
    /// <see cref="SampleCorpus"/> itself uses. This is the floor
    /// <see cref="EveryPublicSampleFactory_IsDiscovered"/> cannot be, because
    /// both of that test's sides filter with <see cref="BindingFlags.Public"/>
    /// and so move together: demoting a factory to <see langword="internal"/>
    /// removes it from BOTH sides at once, and the equality still holds.
    /// </summary>
    /// <remarks>
    /// A demotion to <see langword="internal"/> is caught here because
    /// <see langword="nameof"/> only requires the member to be accessible from
    /// THIS type, which is in the same assembly as
    /// <see cref="DocumentSpecSamples"/> and so can still name an internal
    /// member; <see cref="SampleCorpus.Names"/> filters with
    /// <see cref="BindingFlags.Public"/> and so drops it, and the two sets
    /// stop matching. A rename or deletion is caught even earlier, as a BUILD
    /// failure: <see langword="nameof"/> stops compiling the moment the name
    /// it names no longer exists, exactly the discipline the nineteen
    /// hand-written call sites this corpus replaced once provided.
    /// <para>
    /// NOTE: adding a legitimate new sample to <see cref="DocumentSpecSamples"/>
    /// means adding its name here too. That is deliberate, not an oversight:
    /// naming every member explicitly, rather than deriving the roster from
    /// any predicate, is the only way this list can notice one going missing
    /// without also being blind to it going missing for the same reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void Corpus_ContainsExactlyTheExpectedRoster()
    {
        string[] expected =
        [
            nameof(DocumentSpecSamples.EveryContentItemTypeFullyCustomised),
            nameof(DocumentSpecSamples.EveryContentItemTypeAtDefault),
            nameof(DocumentSpecSamples.PdfA2bWithOutputIntent),
            nameof(DocumentSpecSamples.Encrypted),
            nameof(DocumentSpecSamples.MultipleListsTablesAndParagraphs),
            nameof(DocumentSpecSamples.PdfA2uWithOutputIntent),
            nameof(DocumentSpecSamples.ControlCharactersAndLineSeparators),
            nameof(DocumentSpecSamples.SharedAndValueEqualRunStyles),
            nameof(DocumentSpecSamples.AdditionalCoverage),
            nameof(DocumentSpecSamples.PdfA2aWithOutputIntent),
            nameof(DocumentSpecSamples.PdfUA1WithOutputIntent),
            nameof(DocumentSpecSamples.EncryptedNoPermissions),
            nameof(DocumentSpecSamples.EncryptedOwnerPasswordOnly),
            nameof(DocumentSpecSamples.EncryptedNoOwnerPasswordUnrestricted),
            nameof(DocumentSpecSamples.PlainTextUsesDocumentDefault),
            nameof(DocumentSpecSamples.CmykOutputIntent),
            nameof(DocumentSpecSamples.RemainingBranchCoverage),
            nameof(DocumentSpecSamples.RemainingEmitterBranchCoverage),
            nameof(DocumentSpecSamples.FinalEmitterBranchCoverage),
        ];

        RosterAssertions.AssertSameRoster(expected, SampleCorpus.Names());
    }
}
