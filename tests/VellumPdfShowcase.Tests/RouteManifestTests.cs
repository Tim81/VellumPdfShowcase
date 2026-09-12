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
    private sealed record Route(
        string Path,
        string Heading,
        string[] Expect,
        string[] Reject,
        string? Text,
        IReadOnlyDictionary<string, int> ExpectAtLeast,
        bool Distinct);

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
                element.TryGetProperty("heading", out var h) ? h.GetString() ?? string.Empty : string.Empty,
                ReadStrings(element, "expect"),
                ReadStrings(element, "reject"),
                element.TryGetProperty("text", out var text) ? text.GetString() : null,
                ReadCounts(element),
                !element.TryGetProperty("distinct", out var distinct) || distinct.GetBoolean()));
        }

        Assert.NotEmpty(routes);
        return new Manifest(routes);
    }

    private static IReadOnlyDictionary<string, int> ReadCounts(JsonElement element)
    {
        if (!element.TryGetProperty("expectAtLeast", out var counts))
        {
            return new Dictionary<string, int>();
        }

        return counts.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetInt32());
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
    [InlineData("/smoke")]
    public void EveryFixedPageIsDriven(string path) =>
        Assert.Contains(path, Load().Routes.Select(route => route.Path));

    /// <summary>
    /// Every route must identify which page answered it.
    /// </summary>
    /// <remarks>
    /// A review redirected one capability route to another and the check reported
    /// success, because every capability route carries identical expectations and
    /// nothing compared the heading to the route. The heading is what tells them
    /// apart.
    /// </remarks>
    [Fact]
    public void EveryRouteAssertsItsOwnHeading()
    {
        foreach (var route in Load().Routes)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(route.Heading),
                $"{route.Path} asserts no heading, so it cannot tell which page answered");
        }
    }

    /// <summary>
    /// Every route must assert something beyond its heading.
    /// </summary>
    /// <remarks>
    /// The route list was gated from the beginning; the assertions on it were not.
    /// A review reduced five routes to bare paths and every one of the 492 tests
    /// stayed green, which left the gallery, the playground, the about page and
    /// the runtime smoke page able to be stripped to "has a heading and no error
    /// bar" unnoticed.
    /// </remarks>
    [Fact]
    public void EveryRouteAssertsSomethingBeyondItsHeading()
    {
        foreach (var route in Load().Routes)
        {
            Assert.True(
                route.Expect.Length + route.Reject.Length > 0 || !string.IsNullOrWhiteSpace(route.Text),
                $"{route.Path} asserts nothing at all beyond rendering a heading");
        }
    }

    /// <summary>
    /// Each page is held to the specific things it exists to show, rather than to
    /// "something".
    /// </summary>
    /// <remarks>
    /// A review reduced the playground to one selector, the gallery to one card
    /// class, the about page to a one-letter substring and the runtime smoke page
    /// to another, deleted the not-found route outright, and dropped the object
    /// tree from every capability. All 498 tests stayed green and the harness then
    /// drove sixteen routes and exited 0. Asserting that a route asserts SOMETHING
    /// is close to vacuous; this names what.
    /// </remarks>
    [Theory]
    [InlineData("/", ".card", ".card-planned", ".card-unavailable")]
    [InlineData("/playground", "iframe", ".code-panel-body", "select")]
    [InlineData("/compliance", ".preflight-pass", ".coverage-table tbody tr", ".provenance")]
    [InlineData("/about", "table")]
    [InlineData("/smoke", "button:has-text('Generate')", "section")]
    [InlineData("/no-such-page")]
    public void EveryFixedPageAssertsWhatItExistsToShow(string path, params string[] required)
    {
        var route = Load().Routes.Single(r => r.Path == path);

        foreach (var selector in required)
        {
            Assert.Contains(selector, route.Expect);
        }
    }

    /// <summary>
    /// A capability route must assert the object tree as well as the preview and
    /// the snippet, since those are the three panels the page exists to show.
    /// </summary>
    [Fact]
    public void EveryDemonstrableCapabilityAssertsAllThreePanels()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");

            Assert.Contains("iframe", route.Expect);
            Assert.Contains(".code-panel-body", route.Expect);
            Assert.Contains(".api-tree li", route.Expect);
        }
    }

    /// <summary>
    /// The gallery must be held to a card for every catalogue entry, so that a
    /// gallery rendering three cards where eleven belong is caught.
    /// </summary>
    /// <remarks>
    /// The count lives in the manifest so the harness can read it, and is held to
    /// the catalogue here so it cannot fall behind. A review removed all but one
    /// card of each class and the harness reported success, because presence was
    /// asserted and quantity was not.
    /// </remarks>
    [Fact]
    public void TheGalleryIsHeldToOneCardPerCapability()
    {
        var route = Load().Routes.Single(r => r.Path == "/");

        Assert.True(route.ExpectAtLeast.TryGetValue(".card", out var cards), "the gallery asserts no card count");
        Assert.Equal(CapabilityCatalog.All.Count, cards);

        Assert.True(route.ExpectAtLeast.TryGetValue(".card-planned", out var planned));
        Assert.Equal(CapabilityCatalog.All.Count(c => c.Status == CapabilityStatus.Planned), planned);

        Assert.True(route.ExpectAtLeast.TryGetValue(".card-unavailable", out var unavailable));
        Assert.Equal(CapabilityCatalog.All.Count(c => c.Status == CapabilityStatus.UnavailableInBrowser), unavailable);
    }

    /// <summary>
    /// A capability route's heading must be that capability's own title.
    /// </summary>
    /// <remarks>
    /// The heading check exists to stop one route serving another's document, and
    /// a review defeated it by simply writing the other capability's title into
    /// the manifest. Requiring it to equal the catalogue's own title closes that:
    /// the manifest can no longer disagree with the page it is checking.
    /// </remarks>
    [Fact]
    public void EveryCapabilityRouteAssertsTheCatalogueTitle()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All)
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");
            Assert.Equal(capability.Title, route.Heading);
        }
    }

    /// <summary>
    /// The distinctness exemption must be spent only where it is earned.
    /// </summary>
    /// <remarks>
    /// Nothing read the flag, so setting it on every route disabled the whole
    /// distinctness gate with the suite green. The playground earns it because it
    /// shows whichever capability is selected first, which is by construction a
    /// document another route also shows. No other route has that excuse.
    /// </remarks>
    [Fact]
    public void OnlyThePlaygroundIsExemptFromShowingSomethingOfItsOwn()
    {
        foreach (var route in Load().Routes)
        {
            if (route.Path == "/playground")
            {
                Assert.False(route.Distinct, "the playground shows another route's document and must be exempt");
            }
            else
            {
                Assert.True(route.Distinct, $"{route.Path} claims an exemption it has not earned");
            }
        }
    }

    /// <summary>
    /// The conformance routes must assert a PASSING verdict, not merely a verdict.
    /// </summary>
    /// <remarks>
    /// The verdict element carries preflight-pass, preflight-fail and
    /// preflight-unreadable alike, so asserting the verdict class asserted nothing
    /// about conformance: a document failing its own profile, or one the validator
    /// could not read, would have passed. This is the site's headline claim about
    /// the library, so it is the last thing that should go unchecked.
    /// </remarks>
    [Theory]
    [InlineData("/compliance")]
    [InlineData("/capability/pdfa-conformance")]
    public void EveryConformanceRouteAssertsAPass(string path)
    {
        var route = Load().Routes.Single(r => r.Path == path);

        Assert.Contains(".preflight-pass", route.Expect);
        Assert.DoesNotContain(".preflight-verdict", route.Expect);
    }

    /// <summary>
    /// A demonstrable capability must be asserted to show its preview, not merely
    /// to have one. The empty-state element being absent is what says the preview
    /// is the document rather than a placeholder.
    /// </summary>
    [Fact]
    public void EveryDemonstrableCapabilityRejectsTheEmptyState()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");
            Assert.Contains(".pdf-preview-empty", route.Reject);
        }
    }

    /// <summary>
    /// The conformance page's standing claim about veraPDF is asserted by the
    /// smoke check rather than left to a reader, because removing it would be a
    /// silent change to what this site says about someone else's software.
    /// </summary>
    /// <summary>
    /// The not-found route asserts no selector, so the only thing distinguishing
    /// it from any other page is its text, which is therefore pinned.
    /// </summary>
    [Fact]
    public void TheNotFoundRouteIsHeldToItsMessage()
    {
        var route = Load().Routes.Single(r => r.Path == "/no-such-page");

        Assert.Equal("Not Found", route.Text);
    }

    [Fact]
    public void TheConformancePageIsHeldToItsVeraPdfStatement()
    {
        var route = Load().Routes.Single(r => r.Path == "/compliance");

        Assert.Equal("veraPDF is not running on this page", route.Text);
    }
}
