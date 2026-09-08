using VellumPdfShowcase.Web.Generation;
using DocumentConformance = VellumPdf.Document.PdfConformance;
using PreflightConformance = VellumPdf.Conformance.PdfConformance;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Covers all five members of <see cref="DocumentConformance"/>, including
/// <see cref="DocumentConformance.None"/>, against the explicit mapping in
/// <see cref="ConformanceMapping"/>. A numeric cast between the two
/// <c>PdfConformance</c> enumerations is wrong for every profile (plan section
/// 3.4.1): casting <c>PdfA2b</c> (1) yields <c>PdfA2U</c> (1), <c>PdfA2u</c> (2)
/// yields <c>PdfA2A</c> (2), <c>PdfA2a</c> (3) yields <c>PdfUA1</c> (3), and
/// <c>PdfUA1</c> (4) falls outside the target enum's range altogether. These
/// assertions pin the correct, unrelated-by-arithmetic value for each member.
/// </summary>
public class ConformanceMappingTests
{
    [Fact]
    public void ToPreflightProfile_None_ReturnsNull() =>
        Assert.Null(ConformanceMapping.ToPreflightProfile(DocumentConformance.None));

    [Fact]
    public void ToPreflightProfile_PdfA2b_ReturnsPdfA2B() =>
        Assert.Equal(PreflightConformance.PdfA2B, ConformanceMapping.ToPreflightProfile(DocumentConformance.PdfA2b));

    [Fact]
    public void ToPreflightProfile_PdfA2u_ReturnsPdfA2U() =>
        Assert.Equal(PreflightConformance.PdfA2U, ConformanceMapping.ToPreflightProfile(DocumentConformance.PdfA2u));

    [Fact]
    public void ToPreflightProfile_PdfA2a_ReturnsPdfA2A() =>
        Assert.Equal(PreflightConformance.PdfA2A, ConformanceMapping.ToPreflightProfile(DocumentConformance.PdfA2a));

    [Fact]
    public void ToPreflightProfile_PdfUA1_ReturnsPdfUA1() =>
        Assert.Equal(PreflightConformance.PdfUA1, ConformanceMapping.ToPreflightProfile(DocumentConformance.PdfUA1));

    [Fact]
    public void ToPreflightProfile_UnrecognisedMember_Throws()
    {
        const DocumentConformance invalid = (DocumentConformance)99;
        Assert.Throws<ArgumentOutOfRangeException>(() => ConformanceMapping.ToPreflightProfile(invalid));
    }
}
