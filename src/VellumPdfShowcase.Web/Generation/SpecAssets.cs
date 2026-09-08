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
    /// <remarks>
    /// Every array below is a fresh defensive copy, not the array already
    /// stored inside <paramref name="spec"/>. <see cref="SpecLimits.ValidateAssetBytes"/>
    /// clones once at <see cref="DocumentSpec"/> construction so a caller's own
    /// array cannot reach in and change what a fully constructed record holds;
    /// handing out that same cloned array again here would reopen the identical
    /// hole from the other direction, since <see cref="byte"/>[] has no
    /// read-only view and a caller holding one of these arrays could otherwise
    /// mutate the bytes <see cref="Generation.SpecRenderer"/> goes on to render.
    /// </remarks>
    public static SpecAssets FromSpec(DocumentSpec spec) => new()
    {
        EmbeddedFonts = [.. spec.EmbeddedFonts.Select(Clone)],
        Images = [.. spec.Content.OfType<ImageSpec>().Select(image => Clone(image.Bytes))],
        IccProfile = spec.OutputIntent is PdfAOutputIntentSpec pdfA ? Clone(pdfA.IccProfile) : null,
    };

    private static byte[] Clone(byte[] bytes) => (byte[])bytes.Clone();
}
