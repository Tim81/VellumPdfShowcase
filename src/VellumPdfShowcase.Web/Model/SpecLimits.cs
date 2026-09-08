using System.Buffers.Binary;
using System.Text;

namespace VellumPdfShowcase.Web.Model;

/// <summary>
/// The size and length caps <see cref="DocumentSpec"/> and its nested records
/// enforce at construction, per plan section 5.4. Generation runs synchronously
/// on the visitor's own tab, so an unbounded specification is a denial of
/// service the visitor inflicts on themselves; every limit here is generous
/// enough that no sample or real showcase document comes close to it.
/// </summary>
/// <remarks>
/// Two different kinds of cap work together here, and neither is sufficient
/// alone. Every cap other than <see cref="MaxWalkedNodes"/> bounds how many
/// DISTINCT objects a specification may hold in one collection: at most this
/// many table rows, this many list items, this many characters in one string.
/// None of those caps, individually or together, stops the same
/// already-capped object graph from being referenced repeatedly. A handful of
/// distinct objects sharing one deeply nested subtree can still make
/// <see cref="Generation.SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>,
/// both of which walk content by position rather than by object identity,
/// perform work many orders of magnitude larger than the object count alone
/// suggests. <see cref="MaxWalkedNodes"/> is what actually closes that gap: it
/// bounds the total number of nodes the walk itself visits, so no
/// specification, however its objects are shared or arranged, can make either
/// side perform unbounded work.
/// </remarks>
public static class SpecLimits
{
    /// <summary>
    /// Caps <see cref="Model.ImageSpec.Bytes"/>, each entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/>, and
    /// <see cref="Model.PdfAOutputIntentSpec.IccProfile"/>. 20 MB comfortably
    /// exceeds every asset this application ships (the largest bundled font is
    /// under 2 MB) while keeping a single specification's byte-array footprint
    /// bounded. This does NOT bound decode time: a well-formed file far
    /// smaller than this cap can still declare an enormous pixel grid.
    /// Decode time is bounded instead by the decoding library's own
    /// decoded-pixel-count guard, which <see cref="Generation.SpecRenderer.BuildImage"/>'s
    /// try/catch surfaces as a legible message rather than an unhandled
    /// exception.
    /// </summary>
    public const int MaxAssetBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Caps every plain-text string a specification carries: heading text and
    /// bookmark title, paragraph run text, plain text, list item and cell
    /// content, alternative text on images and charts, running header and
    /// footer templates, document metadata fields, output intent identifiers
    /// and info strings, and encryption passwords. 100,000 characters is far
    /// beyond anything a person would type into a demonstration document, but
    /// nowhere near large enough on its own to let a specification's emitted
    /// C# snippet or rendered PDF grow pathologically; <see cref="MaxWalkedNodes"/>
    /// is what bounds how many such strings one specification can multiply
    /// together through shared structure.
    /// </summary>
    public const int MaxTextLength = 100_000;

    /// <summary>
    /// Caps a BCP 47 language tag. RFC 5646's own ABNF sets no length ceiling
    /// on a conforming tag: the grammar permits arbitrarily many variant,
    /// extension and private-use subtags. 35 characters comfortably covers an
    /// ordinary tag (a primary language, script, region and a variant or two)
    /// while remaining short enough that a value here still reads as a
    /// language tag rather than free text; it is a practical showcase limit,
    /// not a bound the standard supplies.
    /// </summary>
    public const int MaxLanguageTagLength = 35;

    /// <summary>
    /// Caps <see cref="Model.TextStyleSpec.LinkUri"/>. 2048 is a conventional
    /// practical ceiling for a URL, historically the Internet Explorer
    /// address-bar limit, carried over here as a generous round number rather
    /// than because it matches any constraint this specific destination
    /// imposes: the value ends up in a PDF <c>/URI</c> action, not a browser
    /// address bar, so no address-bar rule actually applies to it. It remains
    /// far larger than any link a showcase sample uses.
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

    /// <summary>Caps <see cref="Model.TableSpec.ColumnWidths"/>.</summary>
    public const int MaxTableColumnWidths = 100;

    /// <summary>Caps <see cref="Model.PieChartSpec.Slices"/>.</summary>
    public const int MaxChartSlices = 100;

    /// <summary>Caps <see cref="Model.ParagraphSpec.Runs"/>.</summary>
    public const int MaxParagraphRuns = 1_000;

    /// <summary>Caps <see cref="Model.ListSpec.Items"/>.</summary>
    public const int MaxListItems = 2_000;

    /// <summary>
    /// Caps how many direct <see cref="Model.ListItemSpec.Children"/> one
    /// list item may hold. Distinct from <see cref="MaxListNestingDepth"/>,
    /// which caps depth rather than breadth at any one level.
    /// </summary>
    public const int MaxListItemChildren = 100;

    /// <summary>Caps the COUNT of <see cref="Model.DocumentSpec.EmbeddedFonts"/>; each entry's own size is capped separately by <see cref="MaxAssetBytes"/>.</summary>
    public const int MaxEmbeddedFonts = 100;

    /// <summary>
    /// Caps how deeply a <see cref="Model.ListItemSpec"/> tree may nest through
    /// <see cref="Model.ListItemSpec.Children"/>. Measured directly: unbounded
    /// nesting overflows the CLR stack at roughly depth 4000 on the desktop,
    /// lower in the browser, and a stack overflow cannot be caught. 64 levels
    /// is far beyond any real outline while leaving a wide safety margin below
    /// that threshold.
    /// </summary>
    public const int MaxListNestingDepth = 64;

    /// <summary>
    /// Caps the total number of nodes a walk of <see cref="Model.DocumentSpec.Content"/>
    /// visits, counted by <see cref="Model.DocumentSpec"/> exactly as
    /// <see cref="Generation.SpecRenderer"/> and <see cref="Generation.SpecCodeEmitter"/>
    /// walk it: once per position in the tree, not once per distinct object.
    /// Every other cap in this file bounds how many distinct objects one
    /// collection may hold; none of them, alone or combined, stops the same
    /// already-capped object graph from being referenced repeatedly. Five
    /// hundred list-item slots that all point at the one shared, sixty-three
    /// level list-item chain construct only a few hundred distinct objects,
    /// well inside every per-collection cap, but a walk that visits a shared
    /// reference once per slot performs the work of five hundred distinct
    /// chains. This limit is what actually bounds that work: counting stops
    /// the instant the running total would exceed it, so a specification
    /// engineered to make the true total astronomically large is rejected
    /// after doing only this many units of counting work, never after
    /// actually doing the astronomical amount of work itself.
    /// </summary>
    public const int MaxWalkedNodes = 5_000;

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

    /// <summary>
    /// Throws when <paramref name="value"/> is null or longer than
    /// <see cref="MaxAssetBytes"/>; otherwise returns a defensive copy, so the
    /// caller's own array cannot be mutated afterward to change what a fully
    /// constructed record holds. Every one of the three byte-array members
    /// this validates (<see cref="Model.ImageSpec.Bytes"/>,
    /// <see cref="Model.PdfAOutputIntentSpec.IccProfile"/>, and each entry of
    /// <see cref="Model.DocumentSpec.EmbeddedFonts"/>) is validated once
    /// against bytes the caller could still hold a reference to; without this
    /// copy, overwriting a validated array after construction would bypass
    /// that validation entirely, for instance replacing a validated PNG's
    /// bytes with BMP bytes after <see cref="Model.DocumentSpec.Content"/>'s
    /// magic-byte check has already passed.
    /// </summary>
    public static byte[] ValidateAssetBytes(byte[] value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value.Length > MaxAssetBytes
            ? throw new ArgumentException(
                $"{paramName} must not exceed {MaxAssetBytes:N0} bytes ({MaxAssetBytes / (1024 * 1024)} MB); got {value.Length:N0} bytes.",
                paramName)
            : (byte[])value.Clone();
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
    private const int MinimumHeaderLength = 128;

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
