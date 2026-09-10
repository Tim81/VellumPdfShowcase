using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using VellumPdfShowcase.Web.Generation;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The branch-coverage gate instruments one namespace,
/// <c>VellumPdfShowcase.Web.Generation</c>, and holds a roster of the types it
/// expects to find there. That roster is now checked in both directions, so a
/// type leaving or arriving inside the namespace is caught. What the script
/// structurally cannot see is a helper extracted OUT of the namespace
/// altogether: the coverage report then simply does not mention it, and every
/// check the script performs keeps passing over whatever remains.
/// </summary>
/// <remarks>
/// This test closes that half, from inside the assembly rather than from the
/// report. It reads the roster out of the script itself rather than repeating
/// it, so the two cannot drift: a type added to one and not the other fails
/// here.
/// <para>
/// NOTE: the failure this guards against is not hypothetical in shape. Round
/// nine of this project's review recorded the opposite direction of the same
/// problem, where pressure toward a coverage number deleted defensive arms
/// from <see cref="SpecRenderer"/> while the emitter's equivalents survived,
/// producing live divergences with the suite and the gate fully green. A gate
/// that silently stops covering the code it exists for is worth a test of its
/// own.
/// </para>
/// <para>
/// NOTE: <see cref="EveryApplicationType_LivesInAKnownNamespace"/> below does
/// NOT close the whole case its own summary once claimed. It catches a type
/// extracted into a namespace NONE of the known namespaces name; it does not
/// catch one extracted into a DIFFERENT known namespace, such as
/// <c>VellumPdfShowcase.Web.Model</c>, which is the likeliest destination for
/// a helper pulled out of <see cref="SpecRenderer"/>'s own namespace and
/// leaves both that test and the gate green. See that test's own remark for
/// what closes the rest of the gap, and what does not.
/// </para>
/// </remarks>
public class CoverageScopeTests
{
    private static readonly string[] KnownNamespaces =
    [
        "VellumPdfShowcase.Web",
        "VellumPdfShowcase.Web.Model",
        "VellumPdfShowcase.Web.Generation",
        "VellumPdfShowcase.Web.Components",
        "VellumPdfShowcase.Web.Components.Layout",
        "VellumPdfShowcase.Web.Components.Pages",
        "VellumPdfShowcase.Web.Components.Shared",
    ];

    private static string GateScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "eng", "check-emitter-branch-coverage.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("eng/check-emitter-branch-coverage.ps1 was not found above the test output directory.");
    }

    private static HashSet<string> RosterFromGateScript()
    {
        var script = File.ReadAllText(GateScriptPath());

        var start = script.IndexOf("$expectedTypes = @(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The gate script no longer declares $expectedTypes, which this test reads its roster from.");

        var end = script.IndexOf(')', start);
        Assert.True(end > start, "The gate script's $expectedTypes declaration is not closed.");

        return script[start..end]
            .Split('\n')
            .Select(line => line.Trim().Trim(',').Trim('\'', '"'))
            .Where(line => line.StartsWith("VellumPdfShowcase.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether <paramref name="type"/> was emitted by the compiler or the SDK
    /// rather than written in this repository: the top-level-statement
    /// <c>Program</c> class the entry point compiles to, anything marked
    /// compiler-generated, and the attribute types the framework injects into
    /// every assembly. None of them is code this gate is meant to cover.
    /// </summary>
    private static bool IsGenerated(Type type) =>
        type.Name == "Program" ||
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
        (type.Namespace?.StartsWith("Microsoft.", StringComparison.Ordinal) ?? false);

    /// <summary>
    /// Every top-level type in the instrumented namespace must be on the gate's
    /// roster, and every name on the roster must exist. Extracting a helper
    /// into that namespace without listing it fails here; renaming or deleting
    /// a listed type fails here too.
    /// </summary>
    [Fact]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Test-only reflection over this repository's own assembly; the test project is never " +
            "trimmed or published, so Assembly.GetTypes cannot observe a type removed by the linker.")]
    public void GateRoster_MatchesTheInstrumentedNamespace()
    {
        var declared = typeof(SpecRenderer).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "VellumPdfShowcase.Web.Generation" && !type.IsNested && type.DeclaringType is null)
            .Where(type => !type.Name.StartsWith('<'))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(declared, RosterFromGateScript());
    }

    /// <summary>
    /// Catches a type in the application assembly that belongs to no
    /// namespace the gate or this test knows about, which is what an
    /// extraction OUT of every known namespace produces.
    /// </summary>
    /// <remarks>
    /// NOTE: this closes only PART of "extracted out of the instrumented
    /// namespace", not the whole of it, despite what this method's summary
    /// once claimed. <see cref="KnownNamespaces"/> lists several namespaces,
    /// not only <c>VellumPdfShowcase.Web.Generation</c>, so a type moved (or
    /// newly written) directly into another one of them, most plausibly
    /// <c>VellumPdfShowcase.Web.Model</c>, still lives in a KNOWN namespace
    /// and passes this check even though it now lives outside the one the
    /// branch-coverage gate instruments. Demonstrated directly: adding a
    /// public helper to <c>VellumPdfShowcase.Web.Model</c> leaves both this
    /// test and the gate green.
    /// <para>
    /// Relocating an EXISTING <c>VellumPdfShowcase.Web.Generation</c> type
    /// elsewhere is still caught, but by
    /// <see cref="GateRoster_MatchesTheInstrumentedNamespace"/> above, not by
    /// this test: that type disappears from the roster comparison's
    /// "declared" side the moment its namespace changes, regardless of where
    /// it goes. What neither test catches is a helper authored directly in
    /// another known namespace, never having lived in
    /// <c>VellumPdfShowcase.Web.Generation</c> at all. Closing that would
    /// require knowing, for a given type, whether its logic BELONGS to the
    /// generation path, which is a judgement this reflection-based pair of
    /// tests has no way to make.
    /// </para>
    /// </remarks>
    [Fact]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Test-only reflection over this repository's own assembly; the test project is never " +
            "trimmed or published, so Assembly.GetTypes cannot observe a type removed by the linker.")]
    public void EveryApplicationType_LivesInAKnownNamespace()
    {
        var strays = typeof(SpecRenderer).Assembly
            .GetTypes()
            .Where(type => !type.IsNested && !type.Name.StartsWith('<'))
            .Where(type => !IsGenerated(type))
            .Where(type => type.Namespace is null || !KnownNamespaces.Contains(type.Namespace))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(
            strays.Count == 0,
            $"These application types live outside every namespace this project knows about: {string.Join(", ", strays)}. " +
            "A type outside VellumPdfShowcase.Web.Generation is not instrumented by the branch-coverage gate, so a " +
            "helper extracted there leaves the gate silently covering less than it did.");
    }
}
