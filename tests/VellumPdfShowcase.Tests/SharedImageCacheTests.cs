using System.IO.Compression;
using System.Text;
using VellumPdf.Fonts;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// <see cref="Generation.SpecRenderer"/> decodes and embeds an
/// <see cref="ImageSpec"/> INSTANCE once per <see cref="Generation.SpecRenderer.Render"/>
/// call, however many times <see cref="DocumentSpec.Content"/> repeats that
/// same instance, rather than decoding and embedding a fresh copy at every
/// occurrence. Without that, nothing bounds the product of one image's
/// decoded size and its occurrence count, which is memory exhaustion in a
/// browser tab rather than a mere slowdown. Measured through
/// <see cref="Generation.SpecRenderer.Render"/> before this cache existed, a
/// shared 512 by 512 PNG placed 500 times produced 138 MB of output; a
/// shared 4096 by 4096 PNG placed 500 times produced 1.30 GB.
/// <see cref="Generation.SpecRenderer"/>'s (private) per-render image cache
/// closes that trigger.
/// </summary>
/// <remarks>
/// These tests assert on OUTPUT SIZE, not on elapsed time, since a timing
/// assertion flakes. <see cref="SharedInstance_RepeatedManyTimes_StaysSmall"/>
/// is the one that fails if the cache is removed: without it, 200 occurrences
/// of even a tiny synthetic image push the output size well past the ceiling
/// asserted there (measured directly, uncached: about 2.4 MB against the
/// 49,077 bytes this test's threshold allows for).
/// <see cref="DistinctInstances_IdenticalBytes_CacheDoesNotHelp"/> is the
/// companion measurement: distinct <see cref="ImageSpec"/> instances sharing
/// byte-for-byte identical content are NOT deduplicated, deliberately,
/// because the cache is keyed by reference identity, not by
/// <see cref="ImageSpec"/>'s own record equality; <see cref="Model.SpecLimits.MaxTotalAssetBytes"/>
/// is the cap this measurement justifies, bounding the sum of every such
/// distinct instance's bytes rather than relying on the cache to shrink it.
/// </remarks>
public class SharedImageCacheTests
{
    private const int Occurrences = 200;

    private static TextStyleSpec Style() => new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    private static DocumentSpec BuildSpec(byte[] png, bool shareInstance)
    {
        List<ContentItemSpec> content = [];
        var shared = shareInstance ? new ImageSpec { Format = ImageFormat.Png, Bytes = png, Width = 20, Height = 20 } : null;

        for (var i = 0; i < Occurrences; i++)
        {
            content.Add(shareInstance
                ? shared!
                : new ImageSpec { Format = ImageFormat.Png, Bytes = png, Width = 20, Height = 20 });
        }

        return new DocumentSpec
        {
            Page = new PageSizeSpec(200, 200),
            DefaultTextStyle = Style(),
            Content = content,
        };
    }

    /// <summary>
    /// 200 occurrences of ONE shared <see cref="ImageSpec"/> instance. With the
    /// cache in place, the decoded image is embedded once and every occurrence
    /// costs only a lightweight placement, so output stays close to the
    /// single source image's own size. Measured directly at these parameters:
    /// 49,077 bytes. Remove <see cref="Generation.SpecRenderer"/>'s image cache
    /// (decode unconditionally, on every occurrence, the way it did before
    /// the fix) and this assertion fails: measured directly, the same
    /// specification then renders about 2.4 MB.
    /// </summary>
    [Fact]
    public void SharedInstance_RepeatedManyTimes_StaysSmall()
    {
        var spec = BuildSpec(SyntheticPng.CreateVaryingRgb(width: 64, height: 64), shareInstance: true);

        var bytes = SpecRenderer.Render(spec);

        Assert.True(
            bytes.Length < 300_000,
            $"Expected a shared-instance render of {Occurrences} occurrences to stay under 300,000 bytes " +
            $"(one decode, embedded once); got {bytes.Length:N0} bytes. This is the assertion that fails if " +
            "SpecRenderer's image cache is removed or bypassed.");
    }

    /// <summary>
    /// The companion measurement: the identical picture, byte for byte, but
    /// as <see cref="Occurrences"/> DISTINCT <see cref="ImageSpec"/> instances
    /// rather than one shared one. The cache is keyed by reference identity
    /// (see the remark on <see cref="Generation.SpecRenderer"/>'s <c>BuildImage</c>
    /// for why), so it cannot and does not help here: each instance is
    /// decoded and embedded separately, exactly as every occurrence was
    /// before this fix. This pins that this test suite would notice a change
    /// that widened the cache to value equality (which would silently start
    /// deduplicating these too, changing the displayed byte count here) as
    /// much as it would notice the cache being removed.
    /// </summary>
    [Fact]
    public void DistinctInstances_IdenticalBytes_CacheDoesNotHelp()
    {
        var png = SyntheticPng.CreateVaryingRgb(width: 64, height: 64);
        var shared = SpecRenderer.Render(BuildSpec(png, shareInstance: true));
        var distinct = SpecRenderer.Render(BuildSpec(png, shareInstance: false));

        Assert.True(
            distinct.Length > shared.Length * 10,
            $"Expected {Occurrences} distinct instances of an identical image to render substantially larger " +
            $"than {Occurrences} occurrences of one shared instance (shared: {shared.Length:N0} bytes, " +
            $"distinct: {distinct.Length:N0} bytes). This is the measurement that justifies " +
            "SpecLimits.MaxTotalAssetBytes, the aggregate cap that bounds distinct instances by their summed " +
            "byte size instead of relying on the reference-keyed cache to shrink them.");
    }
}

/// <summary>
/// A minimal, dependency-free 8-bit RGB PNG encoder, used only to build a
/// well-formed test asset: <see cref="DocumentSpec.Content"/> sniffs an
/// <see cref="ImageSpec"/>'s magic bytes against its declared
/// <see cref="ImageFormat"/>, and <see cref="Generation.SpecRenderer"/> then
/// hands the bytes to the real Kernel PNG loader, so a placeholder that only
/// satisfies the eight-byte signature is not enough here: the file must
/// actually decode. Produces one IDAT chunk holding a genuine zlib
/// (<c>RFC 1950</c>) stream over "none"-filtered scanlines, through
/// <see cref="ZLibStream"/>, which is part of the base class library and
/// available in this desktop test host regardless of what the published
/// Blazor WebAssembly bundle trims.
/// </summary>
internal static class SyntheticPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// An 8-bit RGB PNG of <paramref name="width"/> by <paramref name="height"/>
    /// pixels, filled with pixel data that varies by position rather than a
    /// solid colour, so the encoded bytes resemble a real photograph rather
    /// than a pathological best case for the compressor.
    /// </summary>
    public static byte[] CreateVaryingRgb(int width, int height)
    {
        var raw = BuildRawScanlines(width, height);
        var idat = Deflate(raw);

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", BuildIhdr(width, height));
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BuildRawScanlines(int width, int height)
    {
        var raw = new byte[(width * 3 + 1) * height];
        var pos = 0;

        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0; // filter type: none
            for (var x = 0; x < width; x++)
            {
                raw[pos++] = (byte)((x * 3 + y) & 0xFF);
                raw[pos++] = (byte)((x + y * 5) & 0xFF);
                raw[pos++] = (byte)((x ^ y) & 0xFF);
            }
        }

        return raw;
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        return stream.ToArray();
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        var ihdr = new byte[13];
        WriteUInt32BigEndian(ihdr, 0, (uint)width);
        WriteUInt32BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // color type: truecolor (RGB)
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace method
        return ihdr;
    }

    private static void WriteChunk(Stream destination, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        WriteUInt32BigEndian(destination, (uint)data.Length);
        destination.Write(typeBytes);
        destination.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        WriteUInt32BigEndian(destination, Crc32.Compute(crcInput));
    }

    private static void WriteUInt32BigEndian(Stream destination, uint value)
    {
        destination.WriteByte((byte)(value >> 24));
        destination.WriteByte((byte)(value >> 16));
        destination.WriteByte((byte)(value >> 8));
        destination.WriteByte((byte)value);
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>The standard PNG chunk CRC32 (ISO 3309 / ITU-T V.42 polynomial), computed table-free since it runs only three times per image.</summary>
    private static class Crc32
    {
        public static uint Compute(byte[] data)
        {
            var crc = 0xFFFFFFFFu;

            foreach (var b in data)
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
