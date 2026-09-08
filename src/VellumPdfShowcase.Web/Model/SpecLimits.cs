using System.Buffers.Binary;
using System.Text;

namespace VellumPdfShowcase.Web.Model;

/// <summary>
/// The size and length caps <see cref="DocumentSpec"/> and its nested records
/// enforce at construction, per plan section 5.4. Generation runs synchronously
/// on the visitor's own tab, so an unbounded specification is a denial of
/// service the visitor inflicts on themselves; every limit here is generous
/// enough that no sample or real showcase document comes close to it, and
/// finite so that no single specification can freeze or exhaust the tab.
/// </summary>
public static class SpecLimits
{
    /// <summary>
    /// Caps <see cref="Model.ImageSpec.Bytes"/>, each entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/>, and
    /// <see cref="Model.PdfAOutputIntentSpec.IccProfile"/>. 20 MB comfortably
    /// exceeds every asset this application ships (the largest bundled font is
    /// under 2 MB) while remaining small enough that decoding even a
    /// maximally-sized, well-formed file finishes in bounded time.
    /// </summary>
    public const int MaxAssetBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Caps ordinary visitor-authored text: heading, paragraph, list item and
    /// cell content, metadata fields, and running header or footer templates.
    /// 100,000 characters is far beyond anything a person would type into a
    /// demonstration document, but nowhere near large enough to let a
    /// specification's emitted C# snippet or rendered PDF grow pathologically.
    /// </summary>
    public const int MaxTextLength = 100_000;

    /// <summary>
    /// Caps a BCP 47 language tag. RFC 5646 bounds a conforming tag well under
    /// this; anything claiming to be one beyond it is not a language tag.
    /// </summary>
    public const int MaxLanguageTagLength = 35;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.LinkUri"/>. 2048 characters matches
    /// the practical URL length ceiling most browsers and servers already
    /// enforce, so nothing legitimate is rejected.
    /// </summary>
    public const int MaxUriLength = 2048;

    /// <summary>
    /// Caps <see cref="Model.DocumentSpec.Content"/>. A demonstration document
    /// with thousands of top-level items is already far beyond anything the
    /// showcase samples use, but a specification of unbounded size is not.
    /// </summary>
    public const int MaxContentItems = 2_000;

    /// <summary>Caps <see cref="Model.TableSpec.Rows"/>.</summary>
    public const int MaxTableRows = 2_000;

    /// <summary>Caps <see cref="Model.TableRowSpec.Cells"/>.</summary>
    public const int MaxTableCellsPerRow = 100;

    /// <summary>Caps <see cref="Model.PieChartSpec.Slices"/>.</summary>
    public const int MaxChartSlices = 100;

    /// <summary>
    /// Caps how deeply a <see cref="Model.ListItemSpec"/> tree may nest through
    /// <see cref="Model.ListItemSpec.Children"/>. Measured directly: unbounded
    /// nesting overflows the CLR stack at roughly depth 4000 on the desktop,
    /// lower in the browser, and a stack overflow cannot be caught. 64 levels
    /// is far beyond any real outline while leaving a wide safety margin below
    /// that threshold.
    /// </summary>
    public const int MaxListNestingDepth = 64;

    /// <summary>Throws when <paramref name="value"/> is null or longer than <paramref name="maxLength"/>; returns it otherwise.</summary>
    public static string ValidateString(string value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value.Length > maxLength
            ? throw new ArgumentException($"{paramName} must not exceed {maxLength} characters; got {value.Length}.", paramName)
            : value;
    }

    /// <summary>Same as <see cref="ValidateString(string, int, string)"/>, but passes a null value through unchanged.</summary>
    public static string? ValidateOptionalString(string? value, int maxLength, string paramName) =>
        value is null ? null : ValidateString(value, maxLength, paramName);

    /// <summary>Throws when <paramref name="value"/> is null or longer than <see cref="MaxAssetBytes"/>; returns it otherwise.</summary>
    public static byte[] ValidateAssetBytes(byte[] value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value.Length > MaxAssetBytes
            ? throw new ArgumentException($"{paramName} must not exceed {MaxAssetBytes} bytes; got {value.Length}.", paramName)
            : value;
    }
}

/// <summary>
/// Sniffs the leading bytes of an image against the five formats the Kernel
/// image loaders accept, per plan section 5.4 control 2: the declared
/// <see cref="ImageFormat"/> must agree with the bytes actually supplied,
/// rather than being trusted outright and used to select which clean-room
/// parser attacker-controlled bytes are handed to.
/// </summary>
public static class ImageSignature
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87a = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89a = "GIF89a"u8.ToArray();

    public static bool Matches(ImageFormat format, byte[] bytes) => format switch
    {
        ImageFormat.Png => bytes.AsSpan().StartsWith(PngMagic),
        ImageFormat.Jpeg => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ImageFormat.Bmp => bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D,
        ImageFormat.Gif => bytes.AsSpan().StartsWith(Gif87a) || bytes.AsSpan().StartsWith(Gif89a),
        ImageFormat.Tiff => bytes.Length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognised image format."),
    };
}

/// <summary>
/// Validates the fixed 128-byte ICC profile header (ICC.1:2010 section 7.2)
/// against a value the profile itself is claimed to have, per plan section
/// 5.4: a three-byte junk value must not silently become an output intent on
/// a document claiming PDF/A or PDF/UA conformance.
/// </summary>
public static class IccProfileHeader
{
    /// <summary>The ICC header is 128 bytes; this validates only the three fields the showcase's own claims depend on.</summary>
    private const int MinimumHeaderLength = 132;

    public static void Validate(byte[] profile, int componentCount)
    {
        if (profile.Length < MinimumHeaderLength)
        {
            throw new ArgumentException(
                $"IccProfile is {profile.Length} bytes, too small to hold a valid ICC profile header ({MinimumHeaderLength} bytes).",
                nameof(profile));
        }

        var declaredSize = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(0, 4));
        if (declaredSize != (uint)profile.Length)
        {
            throw new ArgumentException(
                $"IccProfile's own size field says {declaredSize} bytes, but the array is {profile.Length} bytes.",
                nameof(profile));
        }

        var magic = Encoding.ASCII.GetString(profile, 36, 4);
        if (magic != "acsp")
        {
            throw new ArgumentException(
                $"IccProfile is missing the required 'acsp' signature at byte offset 36; found \"{magic}\".",
                nameof(profile));
        }

        var colorSpace = Encoding.ASCII.GetString(profile, 16, 4).TrimEnd();
        var expectedComponentCount = colorSpace switch
        {
            "GRAY" => 1,
            "RGB" => 3,
            "Lab" => 3,
            "CMYK" => 4,
            _ => (int?)null,
        };

        if (expectedComponentCount is { } expected && expected != componentCount)
        {
            throw new ArgumentException(
                $"IccProfile declares a {colorSpace} data colour space, which requires ComponentCount {expected}; got {componentCount}.",
                nameof(componentCount));
        }
    }
}
