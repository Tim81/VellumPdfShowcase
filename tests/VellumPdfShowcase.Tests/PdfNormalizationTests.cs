using System.Text;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Cycle 7 review: <see cref="PdfNormalization"/>'s own remark explains, at
/// length, WHY each of its three patterns requires an actual newline
/// immediately after the text it masks (<c>(?=\r?\n)</c>): without it, a
/// visitor-supplied <c>LinkUri</c> spelling out text that happens to match a
/// pattern's literal shape could be masked too, potentially hiding a REAL
/// difference between two renders that <c>DeterminismTests</c> exists to
/// catch. Nothing exercised that reasoning directly: dropping all three
/// lookaheads left the whole suite green, because every sample this project
/// renders produces a real <c>/ID</c> entry and real XMP date tags, always
/// followed by an actual newline, so the lookahead's own presence was never
/// load-bearing for any assertion. These tests exercise <see cref="PdfNormalization.Normalize"/>
/// directly, on constructed byte sequences rather than a rendered PDF, so
/// each lookahead's necessity is proved independently of whether any sample
/// happens to also exercise it.
/// </summary>
public class PdfNormalizationTests
{
    private static byte[] Latin1Bytes(string text) => Encoding.Latin1.GetBytes(text);

    [Fact]
    public void DocumentId_FollowedByNewline_IsMasked()
    {
        var pdf = Latin1Bytes("before /ID [<AABBCC> <DDEEFF>]\nafter");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.Contains("/ID [MASKED]", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("AABBCC", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// The security-relevant case: identical text, but NOT followed by a
    /// newline, must NOT be masked. A real trailer's <c>/ID</c> entry is
    /// always followed by a newline; a visitor-supplied <c>LinkUri</c> that
    /// happens to spell out this exact shape cannot be, because a PDF
    /// literal string escapes a raw newline byte to the two ASCII
    /// characters <c>\n</c> (see the remark on <see cref="PdfNormalization"/>).
    /// Dropping the <c>(?=\r?\n)</c> lookahead from <c>DocumentIdPattern</c>
    /// makes this test fail, which is the proof the lookahead is load-bearing.
    /// </summary>
    [Fact]
    public void DocumentId_NotFollowedByNewline_IsNotMasked()
    {
        var pdf = Latin1Bytes("before /ID [<AABBCC> <DDEEFF>] after, no newline here");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.DoesNotContain("MASKED", normalized, StringComparison.Ordinal);
        Assert.Contains("AABBCC", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDate_FollowedByNewline_IsMasked()
    {
        var pdf = Latin1Bytes("<xmp:CreateDate>2024-01-01T00:00:00Z</xmp:CreateDate>\nafter");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.Contains("<xmp:CreateDate>MASKED</xmp:CreateDate>", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("2024-01-01", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDate_NotFollowedByNewline_IsNotMasked()
    {
        var pdf = Latin1Bytes("<xmp:CreateDate>2024-01-01T00:00:00Z</xmp:CreateDate> no newline here");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.DoesNotContain("MASKED", normalized, StringComparison.Ordinal);
        Assert.Contains("2024-01-01", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ModifyDate_FollowedByNewline_IsMasked()
    {
        var pdf = Latin1Bytes("<xmp:ModifyDate>2024-01-01T00:00:00Z</xmp:ModifyDate>\nafter");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.Contains("<xmp:ModifyDate>MASKED</xmp:ModifyDate>", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("2024-01-01", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ModifyDate_NotFollowedByNewline_IsNotMasked()
    {
        var pdf = Latin1Bytes("<xmp:ModifyDate>2024-01-01T00:00:00Z</xmp:ModifyDate> no newline here");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.DoesNotContain("MASKED", normalized, StringComparison.Ordinal);
        Assert.Contains("2024-01-01", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentId_FollowedByCarriageReturnLineFeed_IsMasked()
    {
        // \r\n, not only \n: the library's own line ending for this entry
        // is not pinned down by this test suite as one or the other, so
        // the pattern accepts both.
        var pdf = Latin1Bytes("before /ID [<AABBCC> <DDEEFF>]\r\nafter");
        var normalized = PdfNormalization.Normalize(pdf);

        Assert.Contains("/ID [MASKED]", normalized, StringComparison.Ordinal);
    }
}
