using System.Text.Json;
using VellumPdfShowcase.Web.Catalog;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Holds <c>eng/smoke/routes.json</c> to the capability catalogue, in both
/// directions.
/// </summary>
/// <remarks>
/// The smoke check drives a browser over every route of the published site,
/// which is the only thing in this repository that can see a defect in the page
/// layer at all: the suite runs on desktop .NET while the site runs on browser
/// WebAssembly, and a capability needing AES passed every test while failing in
/// every visitor's tab.
///
/// A hand-written route list would drift out of step with the catalogue the
/// moment a capability was added, and the check would then quietly cover less
/// than it appeared to while still reporting success. That is the same failure
/// this repository has already had twice, in the coverage gate's own roster and
/// in the shipped asset roster, so it is gated the same way.
/// </remarks>
public class RouteManifestTests
{
    private sealed record Route(string Path, string[] Expect, string[] Reject, string? Text);

    private sealed record Manifest(IReadOnlyList<Route> Routes);

    /// <summary>
    /// Read through <see cref="JsonDocument"/> rather than deserialised onto the
    /// records above. Both projects enable the trim analyser with warnings as
    /// errors, and reflection-based deserialisation raises IL2026 and IL3050,
    /// which is the analyser being right: the shapes could not be preserved.
    /// </summary>
    private static Manifest Load()
    {
        var path = Path.Combine(RepositoryRoot(), "eng", "smoke", "routes.json");
        Assert.True(File.Exists(path), $"the smoke route manifest is missing at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        List<Route> routes = [];
        foreach (var element in document.RootElement.GetProperty("routes").EnumerateArray())
        {
            routes.Add(new Route(
                element.GetProperty("path").GetString()!,
                ReadStrings(element, "expect"),
                ReadStrings(element, "reject"),
                element.TryGetProperty("text", out var text) ? text.GetString() : null));
        }

        Assert.NotEmpty(routes);
        return new Manifest(routes);
    }

    private static string[] ReadStrings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array)
            ? [.. array.EnumerateArray().Select(item => item.GetString()!)]
            : [];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NOTICE")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static IReadOnlyList<string> CapabilityRoutePaths(Manifest manifest) =>
        [.. manifest.Routes
            .Select(route => route.Path)
            .Where(path => path.StartsWith("/capability/", StringComparison.Ordinal))];

    /// <summary>
    /// Every capability has a route. A capability added without one would never be
    /// opened in a browser by anything.
    /// </summary>
    [Fact]
    public void EveryCapabilityIsDriven()
    {
        var driven = CapabilityRoutePaths(Load()).ToHashSet(StringComparer.Ordinal);

        foreach (var capability in CapabilityCatalog.All)
        {
            Assert.Contains($"/capability/{capability.Id}", driven);
        }
    }

    /// <summary>
    /// The other direction: a route naming a capability that does not exist would
    /// drive the not-found page and pass, which is a check that proves nothing.
    /// </summary>
    [Fact]
    public void EveryDrivenCapabilityExists()
    {
        foreach (var path in CapabilityRoutePaths(Load()))
        {
            var id = path["/capability/".Length..];
            Assert.True(
                CapabilityCatalog.Find(id) is not null,
                $"{path} is driven by the smoke check but no capability has that identifier");
        }
    }

    /// <summary>
    /// A capability that generates must be asserted to have generated. Without
    /// this, an entry could be listed with no expectations at all and the check
    /// would report success for a page showing nothing.
    /// </summary>
    [Fact]
    public void EveryDemonstrableCapabilityIsAssertedToProduceADocument()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");

            Assert.Contains("iframe", route.Expect);
            Assert.Contains(".code-panel-body", route.Expect);
        }
    }

    /// <summary>
    /// A capability that is withheld must be asserted NOT to produce a document,
    /// and to say why. This is the exact shape of the defect the smoke check was
    /// added for: the page generated anyway and showed a raw platform exception.
    /// </summary>
    [Fact]
    public void EveryWithheldCapabilityIsAssertedToExplainItself()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All.Where(c => !c.IsDemonstrable))
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");

            Assert.Contains("iframe", route.Reject);
            Assert.False(string.IsNullOrWhiteSpace(route.Text), $"{route.Path} asserts no explanation");
        }
    }

    /// <summary>
    /// The pages that are not capabilities are listed too, since the check is
    /// worth nothing if the gallery or the conformance page can break unseen.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/playground")]
    [InlineData("/compliance")]
    [InlineData("/about")]
    public void EveryFixedPageIsDriven(string path) =>
        Assert.Contains(path, Load().Routes.Select(route => route.Path));

    /// <summary>
    /// The conformance page's standing claim about veraPDF is asserted by the
    /// smoke check rather than left to a reader, because removing it would be a
    /// silent change to what this site says about someone else's software.
    /// </summary>
    [Fact]
    public void TheConformancePageIsHeldToItsVeraPdfStatement()
    {
        var route = Load().Routes.Single(r => r.Path == "/compliance");

        Assert.Equal("veraPDF is not running on this page", route.Text);
    }
}
