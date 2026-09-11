namespace VellumPdfShowcase.Web.Assets;

/// <summary>
/// The paths, relative to the host base address, of every binary asset this
/// site ships and later hands to the library as a <see cref="byte"/> array.
/// </summary>
/// <remarks>
/// These are the only paths <see cref="AssetLoader"/> is ever asked for by the
/// application itself. Naming them in one place keeps the set auditable: every
/// byte array that reaches a clean-room parser originates from a path on this
/// list, or from a file the visitor chose, and from nowhere else.
///
/// NOTE: the licence of each file is recorded in
/// <c>wwwroot/assets/LICENSES.md</c> and in the repository NOTICE. A new entry
/// here needs an entry there.
/// </remarks>
public static class ShowcaseAssets
{
    /// <summary>The embedded TrueType face, under the SIL Open Font License 1.1.</summary>
    public const string LiberationSansRegular = "assets/fonts/LiberationSans-Regular.ttf";

    /// <summary>The sRGB v2 ICC profile an output intent embeds.</summary>
    public const string SrgbIccProfile = "assets/icc/sRGB2014.icc";

    /// <summary>The sample image, as PNG.</summary>
    public const string TestCardPng = "assets/images/testcard.png";

    /// <summary>The sample image, as JPEG.</summary>
    public const string TestCardJpeg = "assets/images/testcard.jpg";

    /// <summary>The sample image, as Windows bitmap.</summary>
    public const string TestCardBmp = "assets/images/testcard.bmp";

    /// <summary>The sample image, as TIFF.</summary>
    public const string TestCardTiff = "assets/images/testcard.tif";

    /// <summary>
    /// Every asset above, in one list, so a test can assert that each is
    /// actually deployed and each is within the model's own byte cap.
    /// </summary>
    /// <remarks>
    /// NOTE: there is deliberately no GIF entry, although
    /// <c>ImageFormat.Gif</c> exists in the model and the library advertises
    /// GIF support. VellumPdf.Layout 2.3.1 refuses ordinary GIF files with
    /// "Invalid GIF LZW code"; only trivially compressible content decodes.
    /// The sample is withheld rather than shipped in a state that cannot
    /// render. See <c>eng/generate-sample-images.py</c>.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        LiberationSansRegular,
        SrgbIccProfile,
        TestCardPng,
        TestCardJpeg,
        TestCardBmp,
        TestCardTiff,
    ];
}
