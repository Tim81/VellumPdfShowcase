using DocumentConformance = VellumPdf.Document.PdfConformance;
using PreflightConformance = VellumPdf.Conformance.PdfConformance;

namespace VellumPdfShowcase.Web.Generation;

/// <summary>
/// Maps between the two distinct <c>PdfConformance</c> enumerations described in
/// section 3.4.1 of the plan. <c>Document.Conformance</c> takes
/// <see cref="DocumentConformance"/>; <c>PdfPreflight.Validate</c> takes
/// <see cref="PreflightConformance"/>. The two are spelled differently
/// (<c>PdfA2b</c> against <c>PdfA2B</c>) and, critically, carry different
/// underlying values, so a numeric cast between them is wrong for every single
/// member. This type performs the mapping as one explicit <see langword="switch"/>
/// over named members, with no arithmetic and no cast, and neither enum is ever
/// widened to <see langword="int"/>.
/// </summary>
public static class ConformanceMapping
{
    /// <summary>
    /// Converts a <see cref="DocumentConformance"/> profile to the
    /// <see cref="PreflightConformance"/> profile that validates it, or
    /// <see langword="null"/> for <see cref="DocumentConformance.None"/>,
    /// which has no counterpart in <see cref="PreflightConformance"/> and so
    /// nothing for <c>PdfPreflight.Validate</c> to check.
    /// </summary>
    public static PreflightConformance? ToPreflightProfile(DocumentConformance conformance) => conformance switch
    {
        DocumentConformance.None => null,
        DocumentConformance.PdfA2b => PreflightConformance.PdfA2B,
        DocumentConformance.PdfA2u => PreflightConformance.PdfA2U,
        DocumentConformance.PdfA2a => PreflightConformance.PdfA2A,
        DocumentConformance.PdfUA1 => PreflightConformance.PdfUA1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(conformance),
            conformance,
            "Unrecognised VellumPdf.Document.PdfConformance member."),
    };
}
