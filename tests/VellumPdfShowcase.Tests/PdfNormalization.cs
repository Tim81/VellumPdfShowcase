using System.Text;
using System.Text.RegularExpressions;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// VellumPdf does not produce byte-identical output for the same
/// <c>DocumentSpec</c> across two renders. Rendering the same spec twice and
/// diffing the bytes (see <c>DeterminismTests</c>) isolates the difference to
/// exactly two places: the trailer's <c>/ID</c> entries, and the
/// <c>xmp:CreateDate</c> / <c>xmp:ModifyDate</c> timestamps in the embedded XMP
/// metadata packet. Both are plain ASCII in the file (the XMP packet is not
/// compressed), which is what makes masking them with text regexes safe. Every
/// round-trip assertion in this project compares the <see cref="Normalize"/>
/// form of two PDFs rather than their raw bytes.
/// </summary>
/// <remarks>
/// Measured directly: every OTHER place visitor-supplied text can end up is
/// safe from accidentally matching either pattern below. A heading, paragraph
/// or list item lands in a Flate-compressed content stream, never plain ASCII.
/// A document-info field (<see cref="DocumentMetadataSpec"/>) is
/// written UTF-16BE with a byte-order mark, so an ASCII pattern cannot match
/// it either. Text that reaches the embedded XMP packet (title, keywords, and
/// so on) is XML-escaped there, turning any literal <c>&lt;</c> or <c>&gt;</c>
/// the visitor typed into <c>&amp;lt;</c> or <c>&amp;gt;</c>, so it cannot
/// reconstruct either pattern's literal angle brackets.
/// <para>
/// One vector is real, though: <see cref="TextStyleSpec.LinkUri"/>
/// is written into a plain <c>/URI (...)</c> action string, and
/// <c>Uri.TryCreate</c> accepts a raw space, angle bracket, or even an
/// embedded newline inside an otherwise well-formed <c>http</c>/<c>https</c>
/// URI, so a link whose text happens to spell out <c>/ID [&lt;HEX&gt;
/// &lt;HEX&gt;]</c>, or an XMP date tag pair, could reach the saved bytes
/// verbatim. What stops it from also satisfying either pattern below,
/// measured directly: a PDF literal string still escapes control characters,
/// so a raw newline inside <see cref="TextStyleSpec.LinkUri"/> is
/// written as the two ASCII characters <c>\n</c>, never the actual newline
/// byte. Both patterns require an actual newline immediately after the text
/// they mask, which is exactly how the library always terminates the real
/// trailer's <c>/ID</c> entry and the real XMP date tags, in every
/// combination measured (plain, encrypted, with or without object streams),
/// but which no <c>LinkUri</c> value can ever produce at that position.
/// </para>
/// </remarks>
internal static class PdfNormalization
{
    private static readonly Regex DocumentIdPattern = new(@"/ID \[<[0-9A-Fa-f]+> <[0-9A-Fa-f]+>\](?=\r?\n)", RegexOptions.Compiled);
    private static readonly Regex CreateDatePattern = new(@"<xmp:CreateDate>[^<]*</xmp:CreateDate>(?=\r?\n)", RegexOptions.Compiled);
    private static readonly Regex ModifyDatePattern = new(@"<xmp:ModifyDate>[^<]*</xmp:ModifyDate>(?=\r?\n)", RegexOptions.Compiled);

    /// <summary>
    /// Renders <paramref name="pdf"/> as a string with the document identifier
    /// and the two XMP timestamps masked out, suitable for an equality
    /// assertion between two otherwise-identical PDFs. Latin-1 is used for the
    /// byte-to-character mapping because it is a bijection over every byte
    /// value 0-255, so no compressed or binary stream content is altered by
    /// the round trip through <see cref="string"/>; only the plain-ASCII
    /// regions the two patterns target are ever rewritten.
    /// </summary>
    public static string Normalize(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        text = DocumentIdPattern.Replace(text, "/ID [MASKED]");
        text = CreateDatePattern.Replace(text, "<xmp:CreateDate>MASKED</xmp:CreateDate>");
        text = ModifyDatePattern.Replace(text, "<xmp:ModifyDate>MASKED</xmp:ModifyDate>");
        return text;
    }
}
