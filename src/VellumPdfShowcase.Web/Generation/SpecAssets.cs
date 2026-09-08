using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Web.Generation;

/// <summary>
/// The assets the C# text produced by <see cref="SpecCodeEmitter"/> assumes are
/// already loaded and in scope, under exactly the names <see cref="EmbeddedFonts"/>,
/// <see cref="Images"/> and <see cref="IccProfile"/>. A capability page fetches
/// these once, the way <c>Components/Pages/Smoke.razor.cs</c> already fetches its
/// own font and ICC profile through <c>HttpClient</c>, and the emitted snippet
/// reads them by name rather than repeating raw bytes as a literal. The round-trip
/// test hosts the same snippet through Roslyn scripting with an instance of this
/// type as the script globals, which is what puts these names into scope there.
/// </summary>
public sealed class SpecAssets
{
    public IReadOnlyList<byte[]> EmbeddedFonts { get; init; } = [];
    public IReadOnlyList<byte[]> Images { get; init; } = [];
    public byte[]? IccProfile { get; init; }

    /// <summary>
    /// Builds the assets a <see cref="DocumentSpec"/> requires, in the same
    /// order <see cref="SpecCodeEmitter"/> assigns them indices: embedded fonts
    /// in <see cref="DocumentSpec.EmbeddedFonts"/> order, and images in the
    /// order their <see cref="ImageSpec"/> items appear in <see cref="DocumentSpec.Content"/>.
    /// </summary>
    public static SpecAssets FromSpec(DocumentSpec spec) => new()
    {
        EmbeddedFonts = spec.EmbeddedFonts,
        Images = [.. spec.Content.OfType<ImageSpec>().Select(image => image.Bytes)],
        IccProfile = spec.OutputIntent is PdfAOutputIntentSpec pdfA ? pdfA.IccProfile : null,
    };
}
