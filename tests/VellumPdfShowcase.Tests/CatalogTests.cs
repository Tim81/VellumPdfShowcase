using System.Text.RegularExpressions;
using VellumPdfShowcase.Web.Assets;
using VellumPdfShowcase.Web.Catalog;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// The catalogue is the content of every page. A malformed entry is not a
/// cosmetic problem: an entry that cannot build its document produces a page
/// that fails in the visitor's tab, and this project has no user interface test
/// harness to notice that.
/// </summary>
public partial class CapabilityCatalogTests
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex RouteSafeId();

    private static readonly string AssetRoot = Path.Combine(AppContext.BaseDirectory, "TestAssets");

    /// <summary>
    /// Stands in for the loader, reading the same files from the test project's
    /// linked copies. The capability cannot tell the difference, which is the
    /// point: what is exercised here is the capability, not the transport.
    /// </summary>
    private static CapabilityAssets Load(Capability capability) =>
        new(capability.RequiredAssets.ToDictionary(
            path => path,
            path => File.ReadAllBytes(Path.Combine(AssetRoot, Path.GetFileName(path)))));

    [Fact]
    public void TheCatalogueIsNotEmpty() => Assert.NotEmpty(CapabilityCatalog.All);

    [Fact]
    public void EveryIdentifierIsUniqueAndRouteSafe()
    {
        var ids = CapabilityCatalog.All.Select(capability => capability.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        foreach (var id in ids)
        {
            // The identifier appears in /capability/{id}. Restricting it to lower
            // case, digits and single hyphens means it needs no escaping and reads
            // as itself, and it keeps two entries from differing only by case.
            Assert.Matches(RouteSafeId(), id);
        }
    }

    [Fact]
    public void EveryEntryCarriesItsProse()
    {
        foreach (var capability in CapabilityCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.Title), $"{capability.Id} has no title");
            Assert.False(string.IsNullOrWhiteSpace(capability.Summary), $"{capability.Id} has no summary");
        }
    }

    /// <summary>
    /// A planned card makes two promises a visitor can act on: which release, and
    /// where it is tracked. An entry that makes neither is just an absence with a
    /// heading.
    /// </summary>
    [Fact]
    public void EveryPlannedEntryNamesAMilestoneAndATracker()
    {
        foreach (var capability in CapabilityCatalog.All.Where(c => c.Status == CapabilityStatus.Planned))
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.Milestone), $"{capability.Id} names no milestone");
            Assert.False(string.IsNullOrWhiteSpace(capability.TrackingUri), $"{capability.Id} names no tracker");
            Assert.StartsWith("https://", capability.TrackingUri, StringComparison.Ordinal);
            Assert.Null(capability.Build);
            Assert.False(capability.IsDemonstrable);
        }
    }

    [Fact]
    public void EveryAvailableEntryCanBuildSomething()
    {
        foreach (var capability in CapabilityCatalog.All.Where(c => c.Status == CapabilityStatus.Available))
        {
            Assert.NotNull(capability.Build);
            Assert.True(capability.IsDemonstrable);
            Assert.Null(capability.Milestone);
        }
    }

    /// <summary>
    /// An asset a capability names must be one the site actually ships, or the
    /// page fetches a path that does not exist and fails at generation time.
    /// </summary>
    [Fact]
    public void EveryRequiredAssetIsOneTheSiteShips()
    {
        foreach (var capability in CapabilityCatalog.All)
        {
            foreach (var asset in capability.RequiredAssets)
            {
                Assert.Contains(asset, ShowcaseAssets.All);
            }
        }
    }

    /// <summary>
    /// The one that matters: every demonstrable capability produces a document,
    /// through both consumers, from exactly the assets it declared. This is the
    /// test that fails when an entry is added with a specification the library
    /// refuses.
    /// </summary>
    [Theory]
    [MemberData(nameof(DemonstrableCapabilities))]
    public void EveryDemonstrableCapabilityRendersAndEmits(string id)
    {
        var capability = CapabilityCatalog.Find(id);
        Assert.NotNull(capability);

        var spec = capability.Build!(Load(capability));

        Assert.NotEmpty(SpecRenderer.Render(spec));
        Assert.NotEmpty(SpecCodeEmitter.Emit(spec));
    }

    public static TheoryData<string> DemonstrableCapabilities()
    {
        var data = new TheoryData<string>();
        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            data.Add(capability.Id);
        }

        return data;
    }

    /// <summary>
    /// A capability that claims a conformance profile must satisfy it. A card
    /// saying PDF/A-2b beside a document the library's own validator rejects is
    /// the site lying about the library.
    /// </summary>
    [Fact]
    public void EveryConformanceClaimSurvivesPreflight()
    {
        foreach (var capability in CapabilityCatalog.All.Where(c => c.IsDemonstrable))
        {
            var spec = capability.Build!(Load(capability));
            if (spec.Conformance == VellumPdf.Document.PdfConformance.None)
            {
                continue;
            }

            var report = PreflightRunner.Run(SpecRenderer.Render(spec), spec.Conformance);

            Assert.True(report.Completed, $"{capability.Id}: preflight did not complete: {report.Error}");
            Assert.True(
                report.IsCompliant,
                $"{capability.Id} claims {spec.Conformance} but fails it: "
                    + string.Join("; ", report.Assertions.Select(a => $"{a.RuleId} {a.Message}")));
        }
    }

    [Fact]
    public void FindReturnsNullForAnUnknownIdentifier()
    {
        Assert.Null(CapabilityCatalog.Find("no-such-capability"));
        Assert.Null(CapabilityCatalog.Find(null));
    }

    [Fact]
    public void CategoriesAreThoseActuallyUsed()
    {
        Assert.NotEmpty(CapabilityCatalog.Categories);

        foreach (var category in CapabilityCatalog.Categories)
        {
            Assert.Contains(CapabilityCatalog.All, capability => capability.Category == category);
        }
    }

    /// <summary>
    /// Reading an asset a capability did not declare must fail loudly. Otherwise
    /// a capability could quietly depend on whatever another one happened to have
    /// fetched, and work only when displayed in a particular order.
    /// </summary>
    [Fact]
    public void ReadingAnUndeclaredAssetThrows()
    {
        var assets = new CapabilityAssets(new Dictionary<string, byte[]> { ["declared"] = [1] });

        Assert.Equal<byte[]>([1], assets["declared"]);

        var exception = Assert.Throws<KeyNotFoundException>(() => assets["undeclared"]);
        Assert.Contains("RequiredAssets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmptyBundleDeclaresNothing() =>
        Assert.Throws<KeyNotFoundException>(() => CapabilityAssets.None["anything"]);
}

/// <summary>
/// The preflight runner is what the compliance page reports through, so its
/// distinction between "the document is not compliant" and "the validator could
/// not read it" has to survive, and the profile it validates against has to be
/// the one that was asked for.
/// </summary>
public class PreflightRunnerTests
{
    private static byte[] CompliantPdfA2bBytes()
    {
        var capability = CapabilityCatalog.Find("pdfa-conformance");
        Assert.NotNull(capability);

        var assets = new CapabilityAssets(capability.RequiredAssets.ToDictionary(
            path => path,
            path => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", Path.GetFileName(path)))));

        return SpecRenderer.Render(capability.Build!(assets));
    }

    [Fact]
    public void ACompliantDocumentReportsCompliant()
    {
        var report = PreflightRunner.Run(CompliantPdfA2bBytes(), VellumPdf.Document.PdfConformance.PdfA2b);

        Assert.True(report.Completed);
        Assert.True(report.IsCompliant);
        Assert.Null(report.Error);
        Assert.Equal(VellumPdf.Conformance.PdfConformance.PdfA2B, report.Profile);
    }

    /// <summary>
    /// Bytes that are not a PDF at all must be reported as unreadable rather than
    /// as non-compliant. Those are different findings and the page presents them
    /// differently.
    /// </summary>
    [Fact]
    public void UnreadableBytesReportAnErrorRatherThanAVerdict()
    {
        var report = PreflightRunner.Run("not a pdf"u8.ToArray(), VellumPdf.Conformance.PdfConformance.PdfA2B);

        Assert.False(report.Completed);
        Assert.NotNull(report.Error);
        Assert.Empty(report.Assertions);
        Assert.False(report.IsCompliant);
    }

    /// <summary>
    /// A document may be validated against a profile it does not claim, which is
    /// what the compliance page's profile selector offers.
    /// </summary>
    [Fact]
    public void AProfileMayBeChosenIndependentlyOfTheClaim()
    {
        var report = PreflightRunner.Run(CompliantPdfA2bBytes(), VellumPdf.Conformance.PdfConformance.PdfUA1);

        Assert.True(report.Completed);
        Assert.Equal(VellumPdf.Conformance.PdfConformance.PdfUA1, report.Profile);
    }

    [Fact]
    public void AClaimOfNoneIsRefusedRatherThanGuessedAt()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PreflightRunner.Run([1, 2, 3], VellumPdf.Document.PdfConformance.None));

        Assert.Contains("no conformance profile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullBytesAreRefused() =>
        Assert.Throws<ArgumentNullException>(
            () => PreflightRunner.Run(null!, VellumPdf.Conformance.PdfConformance.PdfA2B));

    /// <summary>
    /// Coverage is a statement about the rule engine, not about any document, and
    /// every selectable profile must have one or the compliance page shows a blank
    /// matrix.
    /// </summary>
    [Fact]
    public void EverySelectableProfileHasCoverageAndChecks()
    {
        Assert.NotEmpty(PreflightRunner.SelectableProfiles);

        foreach (var profile in PreflightRunner.SelectableProfiles)
        {
            var coverage = PreflightRunner.Coverage(profile);
            Assert.True(coverage.Total > 0, $"{profile} declares no checks at all");

            var checks = PreflightRunner.Checks(profile);
            Assert.NotEmpty(checks);
            Assert.All(checks, check => Assert.Contains(profile, check.Profiles));
        }
    }
}
