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
    [Fact]
    public void EveryPublicSampleFactory_IsDiscovered()
    {
        var declared = typeof(DocumentSpecSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(DocumentSpec))
            .Select(method => method.Name);

        Assert.Equal(declared.ToHashSet(StringComparer.Ordinal), SampleCorpus.Names().ToHashSet(StringComparer.Ordinal));
    }
}
