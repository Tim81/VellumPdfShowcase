using System.Net;
using System.Reflection;
using System.Text;
using VellumPdf.Fonts;
using VellumPdfShowcase.Web.Assets;
using VellumPdfShowcase.Web.Generation;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Tests;

/// <summary>
/// Answers a serving handler from a fixed table, and counts how many times each
/// path was actually requested. The count is the whole point: the claim under
/// test is that an asset is fetched once and reused, which cannot be observed
/// from the returned bytes alone.
/// </summary>
/// <summary>
/// Content of a stated size that counts the bytes actually pulled out of it, and
/// never materialises them. The count is the only way to tell a cap that
/// declines an oversized body from one that reads it whole and objects
/// afterwards; both raise the same exception.
/// </summary>
internal sealed class CountingContent(long length) : HttpContent
{
    public long BytesPulled { get; private set; }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var chunk = new byte[64 * 1024];
        while (BytesPulled < length)
        {
            var take = (int)Math.Min(chunk.Length, length - BytesPulled);
            await stream.WriteAsync(chunk.AsMemory(0, take));
            BytesPulled += take;
        }
    }

    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length;
        return true;
    }
}

internal sealed class CountingHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
{
    public Dictionary<string, int> Requests { get; } = [];

    public HttpStatusCode StatusForMissing { get; init; } = HttpStatusCode.NotFound;

    /// <summary>When set, the response declares this length rather than the real one.</summary>
    public long? DeclaredLengthOverride { get; init; }

    /// <summary>When positive, the first N requests for any path throw before answering.</summary>
    public int FailFirst { get; set; }

    /// <summary>When set, every path is answered with a body of this size that counts what is read.</summary>
    public CountingContent? Oversized { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests[path] = Requests.GetValueOrDefault(path) + 1;

        if (FailFirst > 0)
        {
            FailFirst--;
            throw new HttpRequestException("the network is unavailable");
        }

        if (Oversized is not null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Oversized });
        }

        if (!files.TryGetValue(path, out var bytes))
        {
            return Task.FromResult(new HttpResponseMessage(StatusForMissing) { Content = new ByteArrayContent([]) });
        }

        var content = new ByteArrayContent(bytes);
        if (DeclaredLengthOverride is { } declared)
        {
            content.Headers.ContentLength = declared;
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}

public class AssetLoaderTests
{
    private static AssetLoader Build(CountingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") });

    [Fact]
    public async Task LoadAsync_ReturnsTheBytesTheServerSent()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = [1, 2, 3, 4] });

        Assert.Equal<byte[]>([1, 2, 3, 4], await Build(handler).LoadAsync("assets/x.bin"));
    }

    /// <summary>
    /// The caching claim, observed at the only place it is visible: the number
    /// of requests that reached the server.
    /// </summary>
    [Fact]
    public async Task LoadAsync_CalledRepeatedly_FetchesOnce()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = [7] });
        var loader = Build(handler);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal<byte[]>([7], await loader.LoadAsync("assets/x.bin"));
        }

        Assert.Equal(1, handler.Requests["assets/x.bin"]);
    }

    /// <summary>
    /// Two callers that start before either has finished must share one request
    /// rather than each issuing their own, which is the reason the cache holds a
    /// task rather than the bytes.
    /// </summary>
    [Fact]
    public async Task LoadAsync_TwoConcurrentCallers_ShareOneRequest()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = [9] });
        var loader = Build(handler);

        var first = loader.LoadAsync("assets/x.bin");
        var second = loader.LoadAsync("assets/x.bin");
        await Task.WhenAll(first, second);

        Assert.Equal(1, handler.Requests["assets/x.bin"]);
    }

    /// <summary>
    /// A caller that mutates what it was given must not change what the next
    /// caller receives, which is why the loader hands out a copy.
    /// </summary>
    [Fact]
    public async Task LoadAsync_CallerMutatesTheResult_DoesNotPoisonTheCache()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = [1, 2, 3] });
        var loader = Build(handler);

        var first = await loader.LoadAsync("assets/x.bin");
        first[0] = 99;

        Assert.Equal<byte[]>([1, 2, 3], await loader.LoadAsync("assets/x.bin"));
    }

    /// <summary>
    /// A task caches its failure as durably as its result. An HTTP request, unlike
    /// a JavaScript module import, genuinely can succeed on a retry, so a failed
    /// attempt must not be what every later caller receives.
    /// </summary>
    [Fact]
    public async Task LoadAsync_AfterATransientFailure_RetriesAndSucceeds()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = [5] }) { FailFirst = 1 };
        var loader = Build(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync("assets/x.bin"));

        Assert.Equal<byte[]>([5], await loader.LoadAsync("assets/x.bin"));
        Assert.Equal(2, handler.Requests["assets/x.bin"]);
    }

    [Fact]
    public async Task LoadAsync_MissingAsset_NamesThePath()
    {
        var handler = new CountingHandler([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(handler).LoadAsync("assets/absent.bin"));

        Assert.Contains("assets/absent.bin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ServerError_NamesTheStatus()
    {
        var handler = new CountingHandler([]) { StatusForMissing = HttpStatusCode.InternalServerError };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(handler).LoadAsync("assets/x.bin"));

        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A declared length beyond the cap is refused before the body is read, which
    /// is the only check able to decline an oversized asset without first holding
    /// all of it in the tab.
    /// </summary>
    [Fact]
    public async Task LoadAsync_DeclaredLengthBeyondTheCap_IsRefused()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = [1] })
        {
            DeclaredLengthOverride = SpecLimits.MaxAssetBytes + 1L,
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(handler).LoadAsync("assets/x.bin"));

        Assert.Contains("cap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The negative control for the check above: a server that declares nothing,
    /// or lies, is still held to the cap by the length actually received.
    /// </summary>
    [Fact]
    public async Task LoadAsync_UndeclaredBodyBeyondTheCap_IsStillRefused()
    {
        var handler = new CountingHandler(new() { ["assets/x.bin"] = new byte[SpecLimits.MaxAssetBytes + 1] });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(handler).LoadAsync("assets/x.bin"));

        Assert.Contains("cap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The cap must DECLINE an oversized asset, not read it whole and object
    /// afterwards. Asserting that an exception is raised cannot tell those apart,
    /// because both raise one. This measures the bytes the loader actually pulled
    /// out of the response.
    /// </summary>
    /// <remarks>
    /// A review found the original loader buffering 21.1 MB before its own
    /// "refuse before reading the body" check ran, while the test of the day
    /// passed, because the test observed only the exception. The budget below is
    /// the cap plus a small allowance for the read buffer and for however much a
    /// transport hands over in one go; the defect it exists to catch overshoots
    /// by the entire size of the body, not by a buffer.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_OversizedBody_IsAbandonedRatherThanBuffered()
    {
        var oversized = new CountingContent(SpecLimits.MaxAssetBytes * 4L);
        var handler = new CountingHandler([]) { Oversized = oversized };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Build(handler).LoadAsync("assets/huge.bin"));

        Assert.InRange(oversized.BytesPulled, 0, SpecLimits.MaxAssetBytes + (4 * 1024 * 1024));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LoadAsync_BlankPath_Throws(string path) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Build(new CountingHandler([])).LoadAsync(path));
}

/// <summary>
/// The assets this site ships are not merely present; each must survive the
/// model's own validation and the library's loader. A deployment that drops one,
/// or a regenerated image the library cannot decode, fails here rather than in
/// the visitor's tab.
/// </summary>
public class ShowcaseAssetTests
{
    private static readonly string AssetRoot = Path.Combine(
        AppContext.BaseDirectory, "TestAssets");

    private static byte[] Read(string assetPath) =>
        File.ReadAllBytes(Path.Combine(AssetRoot, Path.GetFileName(assetPath)));

    private static TextStyleSpec Style() =>
        new() { Font = FontSpec.FromStandard14(Standard14.Helvetica) };

    [Fact]
    public void EveryDeclaredAssetIsDeployedAndWithinTheByteCap()
    {
        Assert.NotEmpty(ShowcaseAssets.All);

        foreach (var asset in ShowcaseAssets.All)
        {
            var path = Path.Combine(AssetRoot, Path.GetFileName(asset));
            Assert.True(File.Exists(path), $"{asset} is declared but not deployed");
            Assert.InRange(new FileInfo(path).Length, 1, SpecLimits.MaxAssetBytes);
        }
    }

    /// <summary>
    /// Every sample image must reach a rendered PDF through the real loader for
    /// its format. This is the test that would have caught the GIF the library
    /// cannot decode, before it reached a capability page.
    /// </summary>
    [Theory]
    [InlineData(ShowcaseAssets.TestCardPng, ImageFormat.Png, "PngImageLoader")]
    [InlineData(ShowcaseAssets.TestCardJpeg, ImageFormat.Jpeg, "JpegImageLoader")]
    [InlineData(ShowcaseAssets.TestCardBmp, ImageFormat.Bmp, "BmpImageLoader")]
    [InlineData(ShowcaseAssets.TestCardTiff, ImageFormat.Tiff, "TiffImageLoader")]
    public void EverySampleImageRendersThroughItsOwnLoader(string asset, ImageFormat format, string loaderName)
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(400, 400),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Bytes = Read(asset), Format = format, Width = 150 }],
        };

        Assert.NotEmpty(SpecRenderer.Render(spec));

        // The loader named must be the one for THIS format. Asserting merely that
        // some loader appears would pass for any of the five, which would not
        // establish that the emitted snippet decodes the same bytes the renderer
        // decoded.
        Assert.Contains(loaderName, SpecCodeEmitter.Emit(spec), StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative control for the theory above: the magic-byte sniff, not the
    /// declared format, decides which clean-room parser receives the bytes.
    /// </summary>
    [Fact]
    public void ASampleImageDeclaredAsAnotherFormatIsRefused()
    {
        var exception = Assert.Throws<ArgumentException>(() => new DocumentSpec
        {
            Page = new PageSizeSpec(400, 400),
            DefaultTextStyle = Style(),
            Content =
            [
                new ImageSpec
                {
                    Bytes = Read(ShowcaseAssets.TestCardPng),
                    Format = ImageFormat.Jpeg,
                    Width = 150,
                },
            ],
        });

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The DIRECTORY component of every declared path must be real. Nothing else
    /// in this file observes it: assets are read from the test project's linked
    /// copies, which are flattened, and the licence records match on file name.
    /// </summary>
    /// <remarks>
    /// A review demonstrated the gap by rewriting two paths to
    /// <c>assets/bogus-directory/...</c> and <c>totally/wrong/...</c>. The whole
    /// suite stayed green while both assets 404 in the browser. That is the exact
    /// class of regression the commit introducing this file performed, since it
    /// moved every asset into a new directory. This test resolves each declared
    /// path against the real <c>wwwroot</c>, so the prefix is load-bearing.
    /// </remarks>
    [Fact]
    public void EveryDeclaredPathResolvesInsideWwwroot()
    {
        var wwwroot = Path.Combine(RepositoryRoot(), "src", "VellumPdfShowcase.Web", "wwwroot");

        foreach (var asset in ShowcaseAssets.All)
        {
            var full = Path.Combine(wwwroot, asset.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"{asset} does not exist at that path under wwwroot");
        }
    }

    /// <summary>
    /// Every binary under <c>wwwroot/assets</c> must be declared. Without this,
    /// a file dropped into that directory ships, is published, is served, and is
    /// named in no licence record, with the suite green, which is precisely what
    /// LICENSES.md claims cannot happen.
    /// </summary>
    [Fact]
    public void EveryBinaryUnderAssetsIsDeclared()
    {
        var wwwroot = Path.Combine(RepositoryRoot(), "src", "VellumPdfShowcase.Web", "wwwroot");
        var assets = Path.Combine(wwwroot, "assets");

        var declared = ShowcaseAssets.All.ToHashSet(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
        {
            // The licence documents themselves are text, not assets this site
            // hands to a parser, and each states its own terms by existing.
            if (Path.GetExtension(file) is ".md" or ".txt")
            {
                continue;
            }

            var relative = Path.GetRelativePath(wwwroot, file).Replace(Path.DirectorySeparatorChar, '/');
            Assert.Contains(relative, declared);
        }
    }

    /// <summary>
    /// <see cref="ShowcaseAssets.All"/> must list every path the type declares.
    /// It is written by hand, and every other test in this file trusts it, so a
    /// constant added and left out of the list escapes all of them at once.
    /// </summary>
    /// <remarks>
    /// A review demonstrated this by deleting one entry from the list while
    /// leaving the constant, the shipped file and both licence records intact.
    /// The suite stayed green and that asset escaped four separate checks.
    /// </remarks>
    [Fact]
    public void AllListsEveryDeclaredConstant()
    {
        var constants = typeof(ShowcaseAssets)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(constants);
        Assert.Equal(
            constants.OrderBy(value => value, StringComparer.Ordinal),
            ShowcaseAssets.All.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NOTICE")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// A licence record that can fall silently out of date is not a licence
    /// record. Every asset this site ships must be named in both
    /// <c>wwwroot/assets/LICENSES.md</c> and the repository <c>NOTICE</c>, so
    /// that adding a file without stating its terms fails here.
    /// </summary>
    /// <remarks>
    /// The match is on the file name rather than the full path, because the two
    /// documents refer to the same file by different paths: one is written from
    /// inside <c>wwwroot/assets</c> and the other from the repository root.
    /// </remarks>
    [Theory]
    [InlineData("LICENSES.md")]
    [InlineData("NOTICE")]
    public void EveryShippedAssetIsNamedInTheLicenceRecord(string document)
    {
        var text = File.ReadAllText(LicenceDocumentPath(document));

        foreach (var asset in ShowcaseAssets.All)
        {
            var fileName = Path.GetFileName(asset);
            Assert.True(
                text.Contains(fileName, StringComparison.Ordinal),
                $"{fileName} is shipped but is not named in {document}");
        }
    }

    private static string LicenceDocumentPath(string document) =>
        document == "NOTICE"
            ? Path.Combine(RepositoryRoot(), "NOTICE")
            : Path.Combine(RepositoryRoot(), "src", "VellumPdfShowcase.Web", "wwwroot", "assets", "LICENSES.md");

    /// <summary>
    /// The shipped face must be the one the embedded-font samples expect. A
    /// replacement that is not a TrueType file would otherwise surface as a
    /// parser exception at generation time.
    /// </summary>
    [Fact]
    public void TheEmbeddedFaceIsATrueTypeFile()
    {
        var bytes = Read(ShowcaseAssets.LiberationSansRegular);

        // 0x00010000 is the TrueType outline version tag; "true" and "ttcf" are
        // the other tags a TrueType face may legally carry.
        var tag = Encoding.Latin1.GetString(bytes, 0, 4);
        Assert.True(
            (bytes[0] == 0x00 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
                || tag is "true" or "ttcf",
            $"unexpected leading bytes: {Convert.ToHexString(bytes.AsSpan(0, 4))}");
    }

    /// <summary>
    /// The ICC profile must pass the same header check the model applies before
    /// it will accept the profile as an output intent.
    /// </summary>
    [Fact]
    public void TheIccProfileIsAThreeComponentProfile()
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(400, 400),
            DefaultTextStyle = Style(),
            Conformance = VellumPdf.Document.PdfConformance.PdfA2b,
            Content = [new PlainTextSpec { Text = "conformance" }],
            OutputIntent = new PdfAOutputIntentSpec
            {
                IccProfile = Read(ShowcaseAssets.SrgbIccProfile),
                ComponentCount = 3,
                OutputConditionIdentifier = "sRGB IEC61966-2.1",
            },
        };

        Assert.NotEmpty(SpecRenderer.Render(spec));
    }
}
