using System.Net;
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
internal sealed class CountingHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
{
    public Dictionary<string, int> Requests { get; } = [];

    public HttpStatusCode StatusForMissing { get; init; } = HttpStatusCode.NotFound;

    /// <summary>When set, the response declares this length rather than the real one.</summary>
    public long? DeclaredLengthOverride { get; init; }

    /// <summary>When positive, the first N requests for any path throw before answering.</summary>
    public int FailFirst { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests[path] = Requests.GetValueOrDefault(path) + 1;

        if (FailFirst > 0)
        {
            FailFirst--;
            throw new HttpRequestException("the network is unavailable");
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
    [InlineData(ShowcaseAssets.TestCardPng, ImageFormat.Png)]
    [InlineData(ShowcaseAssets.TestCardJpeg, ImageFormat.Jpeg)]
    [InlineData(ShowcaseAssets.TestCardBmp, ImageFormat.Bmp)]
    [InlineData(ShowcaseAssets.TestCardTiff, ImageFormat.Tiff)]
    public void EverySampleImageRendersThroughItsOwnLoader(string asset, ImageFormat format)
    {
        var spec = new DocumentSpec
        {
            Page = new PageSizeSpec(400, 400),
            DefaultTextStyle = Style(),
            Content = [new ImageSpec { Bytes = Read(asset), Format = format, Width = 150 }],
        };

        Assert.NotEmpty(SpecRenderer.Render(spec));
        Assert.Contains("Loader", SpecCodeEmitter.Emit(spec), StringComparison.Ordinal);
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

    private static string LicenceDocumentPath(string document)
    {
        // Walk up from the test binary to the repository root, which is the only
        // anchor available at run time; the documents are not copied to output.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NOTICE")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return document == "NOTICE"
            ? Path.Combine(directory.FullName, "NOTICE")
            : Path.Combine(directory.FullName, "src", "VellumPdfShowcase.Web", "wwwroot", "assets", "LICENSES.md");
    }

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
