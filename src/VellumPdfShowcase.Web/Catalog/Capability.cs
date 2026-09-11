using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Web.Catalog;

/// <summary>Where a capability stands in the library, which decides how its card reads.</summary>
public enum CapabilityStatus
{
    /// <summary>Shipped in the pinned package and demonstrated here by a real document.</summary>
    Available,

    /// <summary>Not in the pinned package. The card states the milestone and links to the tracking issue.</summary>
    Planned,
}

/// <summary>The grouping a capability appears under in the gallery.</summary>
public enum CapabilityCategory
{
    Text,
    Layout,
    Graphics,
    Documents,
    Conformance,
}

/// <summary>
/// The assets a capability asked for, already fetched and keyed by the path it
/// named.
/// </summary>
/// <remarks>
/// A capability declares what it needs and is handed exactly that, so nothing is
/// fetched for a page that does not demonstrate it. The indexer throws rather
/// than returning null for an undeclared path, because a capability reading an
/// asset it did not declare is a defect in the capability, not a runtime
/// condition to be handled.
/// </remarks>
public sealed class CapabilityAssets(IReadOnlyDictionary<string, byte[]> bytes)
{
    /// <summary>An empty bundle, for a capability that needs no asset.</summary>
    public static CapabilityAssets None { get; } = new(new Dictionary<string, byte[]>());

    /// <summary>The bytes of an asset this capability declared.</summary>
    /// <exception cref="KeyNotFoundException">The capability did not declare this path.</exception>
    public byte[] this[string path] =>
        bytes.TryGetValue(path, out var value)
            ? value
            : throw new KeyNotFoundException(
                $"'{path}' was not declared by this capability, so it was never fetched. Add it to RequiredAssets.");
}

/// <summary>
/// One entry in the catalogue: a capability of the library, with the document
/// that demonstrates it.
/// </summary>
/// <remarks>
/// NOTE the demonstration is a <see cref="DocumentSpec"/> and never bytes or a
/// snippet. Both of those are derived from it, by the two consumers that must
/// never disagree, so a capability that carried either directly could show one
/// while producing the other.
/// </remarks>
public sealed record Capability
{
    /// <summary>
    /// The identifier in the route <c>/capability/{id}</c>. Lower case, digits and
    /// hyphens only, so it needs no escaping and reads as itself in a URL.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The heading on the card and on the detail page.</summary>
    public required string Title { get; init; }

    /// <summary>One sentence, shown on the card.</summary>
    public required string Summary { get; init; }

    /// <summary>The gallery grouping.</summary>
    public required CapabilityCategory Category { get; init; }

    /// <summary>Whether the pinned package can demonstrate this.</summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Available;

    /// <summary>The release a planned capability is expected in. Null when available.</summary>
    public string? Milestone { get; init; }

    /// <summary>Where a planned capability is tracked. Null when available.</summary>
    public string? TrackingUri { get; init; }

    /// <summary>
    /// The assets <see cref="Build"/> will read, fetched before it is called and
    /// only then.
    /// </summary>
    public IReadOnlyList<string> RequiredAssets { get; init; } = [];

    /// <summary>
    /// Builds the demonstration. Null exactly when <see cref="Status"/> is
    /// <see cref="CapabilityStatus.Planned"/>, since there is nothing to build.
    /// </summary>
    public Func<CapabilityAssets, DocumentSpec>? Build { get; init; }

    /// <summary>Whether this entry can produce a document at all.</summary>
    public bool IsDemonstrable => Status == CapabilityStatus.Available && Build is not null;
}
