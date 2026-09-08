using VellumPdfShowcase.Web.Generation;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Answers plan section 16 item 6 before any round-trip assertion is written:
/// is VellumPdf's output deterministic? It is not. Rendering the same
/// <see cref="Web.Model.DocumentSpec"/> twice, a second apart, produces two
/// different byte arrays, differing in exactly two places once compared as
/// text: the trailer's <c>/ID</c> entries, and the <c>xmp:CreateDate</c> /
/// <c>xmp:ModifyDate</c> timestamps in the embedded XMP packet. Both are a
/// function of wall-clock time read during <c>Document.Save</c>. Everywhere
/// else, the two renders are byte-identical. This is why every round-trip
/// assertion in <see cref="SpecRoundTripTests"/> compares
/// <see cref="PdfNormalization.Normalize"/> of the two PDFs rather than their
/// raw bytes.
/// </summary>
public class DeterminismTests
{
    [Fact]
    public void Render_CalledTwiceWithSameSpec_ProducesDifferentRawBytesButIdenticalNormalizedContent()
    {
        var spec = DocumentSpecSamples.EveryContentItemTypeFullyCustomised();

        var first = SpecRenderer.Render(spec);

        // VellumPdf's document identifier and XMP timestamps carry one-second
        // resolution; without this delay two renders issued back-to-back can
        // land in the same second and coincidentally produce identical bytes,
        // which would make this test pass for the wrong reason.
        Thread.Sleep(TimeSpan.FromSeconds(1.1));

        var second = SpecRenderer.Render(spec);

        Assert.NotEqual(first, second);
        Assert.Equal(PdfNormalization.Normalize(first), PdfNormalization.Normalize(second));
    }
}
