namespace VellumPdfShowcase.Tests;

/// <summary>
/// Shared assertion for two test-discovered rosters of names, used wherever
/// this project pins a fixed set of members (a sample corpus, an adversarial
/// specification corpus) against what reflection actually discovers.
/// </summary>
/// <remarks>
/// <see cref="Assert.Equal{T}(System.Collections.Generic.ISet{T}, System.Collections.Generic.ISet{T})"/>
/// over two <see cref="HashSet{T}"/> instances prints both sides truncated at
/// the same five elements, so two large, almost-identical sets render as two
/// indistinguishable truncated lines and the one entry that actually differs
/// sits inside the ellipsis on both. This computes the set difference in both
/// directions instead, and names exactly what is missing and what is surplus.
/// </remarks>
internal static class RosterAssertions
{
    public static void AssertSameRoster(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);

        var missing = expectedSet.Except(actualSet).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var surplus = actualSet.Except(expectedSet).OrderBy(name => name, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && surplus.Count == 0,
            "The roster does not match the expected set. " +
            $"Missing (expected but not found): [{string.Join(", ", missing)}]. " +
            $"Surplus (found but not expected): [{string.Join(", ", surplus)}].");
    }
}
