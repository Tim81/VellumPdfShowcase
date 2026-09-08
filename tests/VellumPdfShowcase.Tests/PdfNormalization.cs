using System.Text;
using System.Text.RegularExpressions;

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
internal static class PdfNormalization
{
    private static readonly Regex DocumentIdPattern = new(@"/ID \[<[0-9A-Fa-f]+> <[0-9A-Fa-f]+>\]", RegexOptions.Compiled);
    private static readonly Regex CreateDatePattern = new(@"<xmp:CreateDate>[^<]*</xmp:CreateDate>", RegexOptions.Compiled);
    private static readonly Regex ModifyDatePattern = new(@"<xmp:ModifyDate>[^<]*</xmp:ModifyDate>", RegexOptions.Compiled);

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
