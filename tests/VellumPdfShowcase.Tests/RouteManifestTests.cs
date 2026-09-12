using System.Text.Json;
using VellumPdfShowcase.Web.Catalog;
using VellumPdfShowcase.Web.Generation;

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
        bool Distinct,
        int? Pages);

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
                !element.TryGetProperty("distinct", out var distinct) || distinct.GetBoolean(),
                element.TryGetProperty("pages", out var pages) ? pages.GetInt32() : null));
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
    /// <summary>
    /// A fixed page must assert its own identity, not merely some heading.
    /// </summary>
    /// <remarks>
    /// Capability routes were pinned to their catalogue title, and the five fixed
    /// pages were left as free text. A review set the about page's heading and
    /// text to the conformance page's, served the conformance page at /about, and
    /// both the suite and the harness reported success. The manifest is the gate,
    /// so the manifest is the thing that must not be able to lie.
    /// </remarks>
    [Theory]
    [InlineData("/", "VellumPdf")]
    [InlineData("/playground", "Playground")]
    [InlineData("/compliance", "Conformance")]
    [InlineData("/about", "About this site")]
    [InlineData("/smoke", "Runtime smoke test")]
    [InlineData("/no-such-page", "Not Found")]
    public void EveryFixedPageAssertsItsOwnHeading(string path, string heading) =>
        Assert.Equal(heading, Load().Routes.Single(route => route.Path == path).Heading);

    [Fact]
    public void TheNotFoundRouteIsHeldToItsMessage()
    {
        var route = Load().Routes.Single(r => r.Path == "/no-such-page");

        Assert.Equal("the content you are looking for does not exist", route.Text);
    }

    /// <summary>
    /// Every capability that generates must declare how many pages it produces.
    /// </summary>
    /// <remarks>
    /// Requiring every page to draw does not notice a page that is no longer
    /// there. A review reduced the running-bands capability from two pages to one
    /// and every route passed, on the one capability whose whole subject is
    /// content repeating across pages.
    /// </remarks>
    [Fact]
    public void EveryDemonstrableCapabilityDeclaresItsPageCount()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");

            Assert.True(route.Pages is > 0, $"{route.Path} declares no page count");
        }
    }

    /// <summary>
    /// The declared page count, against the page count the document actually
    /// renders. The test above asserts only that a number is present, which is
    /// half a guard: the number can be present and wrong.
    /// </summary>
    /// <remarks>
    /// This gap was found by review, by changing the <c>lists</c> route's
    /// declared count from 1 to 9 and watching the whole suite stay green. The
    /// browser harness would have caught it, but the browser harness needs a
    /// browser and a published site, so nothing in the suite was looking. The
    /// change that exposed it had altered the shape of that very document,
    /// which is exactly the edit that moves a page count; the declared value
    /// stayed correct by luck rather than by a guard.
    /// <para>
    /// NOTE this is the sixth roster in this repository found to hold in one
    /// direction only. The others were the coverage gate's own roster, the
    /// shipped asset roster, the route manifest's route list, the capability
    /// roster and the painting-operator list. Any number this repository
    /// declares about a document elsewhere needs a test that renders the
    /// document and compares.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryDeclaredPageCountMatchesWhatTheDocumentRenders()
    {
        var manifest = Load();

        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            var route = manifest.Routes.Single(r => r.Path == $"/capability/{capability.Id}");
            var spec = capability.Build!(CapabilityCatalogTests.Load(capability));
            var rendered = DeepPaginationTests.PageCount(SpecRenderer.Render(spec));

            Assert.Equal(route.Pages, rendered);
        }
    }

    /// <summary>
    /// A route's text assertion must be substantial enough to mean something.
    /// </summary>
    /// <remarks>
    /// A review reduced two routes' text to a single letter and the whole suite
    /// stayed green. One of them was a withheld capability, whose text is the
    /// only thing asserting that the page explains itself at all, and the letter
    /// chosen appeared in its own pinned heading.
    /// </remarks>
    [Fact]
    public void EveryTextAssertionSaysSomething()
    {
        // A character floor rather than a word count. "Not Found" is legitimately
        // the whole of the message on its route, and a word rule would refuse it
        // while a single letter is what actually needs refusing.
        foreach (var route in Load().Routes.Where(route => route.Text is not null))
        {
            var text = route.Text!.Trim();

            Assert.True(text.Length >= 8, $"{route.Path} asserts the text {text}, which is too short to distinguish anything");

            // NOTE and it must not simply repeat the heading. A review defeated
            // the length floor by using eight characters OF THE HEADING, which
            // asserts nothing the heading check does not already assert, and on a
            // withheld capability the text is the only thing requiring the page to
            // explain itself at all.
            Assert.False(
                route.Heading.Contains(text, StringComparison.OrdinalIgnoreCase),
                $"{route.Path} asserts text that is part of its own heading, so it adds nothing");
        }
    }

    /// <summary>
    /// The about page carries its own statement about veraPDF, distinct from the
    /// conformance page's, and pinned for the same reason.
    /// </summary>
    [Fact]
    public void TheAboutPageIsHeldToItsOwnVeraPdfStatement() =>
        Assert.Equal(
            "veraPDF is not executing on this site",
            Load().Routes.Single(route => route.Path == "/about").Text);

    [Fact]
    public void TheConformancePageIsHeldToItsVeraPdfStatement()
    {
        var route = Load().Routes.Single(r => r.Path == "/compliance");

        Assert.Equal("veraPDF is not running on this page", route.Text);
    }
}
