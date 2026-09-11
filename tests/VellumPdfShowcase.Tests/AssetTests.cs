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
/// A read-only stream that produces <paramref name="length"/> bytes on demand and
/// counts what was actually taken. It never holds the body, so the count reflects
/// what the consumer read rather than what the harness buffered.
/// </summary>
internal sealed class GeneratedStream(long length) : Stream
{
    public long BytesPulled { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => BytesPulled;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var take = (int)Math.Min(count, length - BytesPulled);
        if (take <= 0)
        {
            return 0;
        }

        Array.Clear(buffer, offset, take);
        BytesPulled += take;
        return take;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Content of a given size that counts the bytes actually pulled out of it and
/// never materialises them. The count is the only way to tell a cap that declines
/// an oversized body from one that reads it whole and objects afterwards, because
/// both raise the same exception.
/// </summary>
/// <remarks>
/// <paramref name="declareLength"/> is the whole point of the type. A body whose
/// length is declared is refused by the header check, which is a different branch
/// from the one that counts the body as it arrives. A review found that every
/// test claiming to cover the counting branch in fact declared a length, so the
/// counting branch could be deleted outright with the suite green.
/// </remarks>
internal sealed class CountingContent(long length, bool declareLength) : HttpContent
{
    private readonly GeneratedStream _stream = new(length);

    public long BytesPulled => _stream.BytesPulled;

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(_stream);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        _stream.CopyToAsync(stream);

    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length;
        return declareLength;
    }
}

/// <summary>
/// Content that yields a few bytes and then fails, standing in for a connection
/// reset part way through a body.
/// </summary>
internal sealed class TornContent : HttpContent
{
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new TornStream());

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        new TornStream().CopyToAsync(stream);

    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = 0;
        return false;
    }

    private sealed class TornStream : Stream
    {
        private bool _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served)
            {
                throw new IOException("the connection was reset");
            }

            _served = true;
            Array.Clear(buffer, offset, 16);
            return 16;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// Answers from a fixed table, and counts how many times each path was actually
/// requested. The count is the whole point: the claim under test is that an asset
/// is fetched once and reused, which cannot be observed from the bytes alone.
/// </summary>
internal sealed class CountingHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
{
    public Dictionary<string, int> Requests { get; } = [];

    public HttpStatusCode StatusForMissing { get; init; } = HttpStatusCode.NotFound;

    /// <summary>When set, the response declares this length rather than the real one.</summary>
    public long? DeclaredLengthOverride { get; init; }

    /// <summary>When positive, the first N requests for any path throw before answering.</summary>
    public int FailFirst { get; set; }

    /// <summary>When set, every path is answered with this content instead of the table.</summary>
    public HttpContent? Body { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests[path] = Requests.GetValueOrDefault(path) + 1;

        if (FailFirst > 0)
        {
            FailFirst--;
            throw new HttpRequestException("the network is unavailable");
        }

        if (Body is not null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Body });
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
    /// The real negative control for the check above: a server that declares
    /// NOTHING is still held to the cap, by the length actually received.
    /// </summary>
    /// <remarks>
    /// A review found that the earlier version of this test used content that
    /// computed its own <c>Content-Length</c>, so the header check refused it
    /// first and the counting branch was never entered. The counting branch could
    /// then be deleted outright with the whole suite green. Undeclared length is
    /// the entire point of this case; do not replace the content with anything
    /// that declares one.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_UndeclaredBodyBeyondTheCap_IsStillRefused()
    {
        var body = new CountingContent(SpecLimits.MaxAssetBytes + 1L, declareLength: false);
        var handler = new CountingHandler([]) { Body = body };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(handler).LoadAsync("assets/x.bin"));

        Assert.Contains("cap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The boundary, on the branch that counts: exactly the cap is admitted, and
    /// one byte more is refused. Without this pair the counting branch could be
    /// off by one in either direction unnoticed.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public async Task LoadAsync_UndeclaredBodyAtTheBoundary(int offset, bool admitted)
    {
        var body = new CountingContent(SpecLimits.MaxAssetBytes + (long)offset, declareLength: false);
        var handler = new CountingHandler([]) { Body = body };
        var loader = Build(handler);

        if (admitted)
        {
            Assert.Equal(SpecLimits.MaxAssetBytes + offset, (await loader.LoadAsync("assets/x.bin")).Length);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync("assets/x.bin"));
        }
    }

    /// <summary>
    /// The cap must DECLINE an oversized asset, not read it whole and object
    /// afterwards. Asserting that an exception is raised cannot tell those apart,
    /// because both raise one. This measures the bytes the loader actually pulled
    /// out of the response, on the undeclared path, which is the only one where
    /// the body is read at all.
    /// </summary>
    /// <remarks>
    /// A review found the original loader buffering 21.1 MB before its own
    /// "refuse before reading the body" check ran, while the test of the day
    /// passed, because that test observed only the exception. The budget below is
    /// the cap plus an allowance for the read buffer; the defect it exists to
    /// catch overshoots by the entire size of the body.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_OversizedBody_IsAbandonedRatherThanBuffered()
    {
        var body = new CountingContent(SpecLimits.MaxAssetBytes * 4L, declareLength: false);
        var handler = new CountingHandler([]) { Body = body };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Build(handler).LoadAsync("assets/huge.bin"));

        Assert.InRange(body.BytesPulled, 0, SpecLimits.MaxAssetBytes + (4 * 1024 * 1024));
    }

    /// <summary>
    /// A body that fails part way through must surface as the documented type,
    /// naming the asset, rather than as whatever the transport raised.
    /// </summary>
    /// <remarks>
    /// Reading the body outside the block that wraps the request is what exposed
    /// this: before the loader streamed, the transfer happened inside that block
    /// and was wrapped by it. A torn body reached the caller as a bare
    /// <c>HttpRequestException</c> saying only "Error while copying content to a
    /// stream", with no indication of which asset had failed.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_BodyTornMidTransfer_NamesTheAsset()
    {
        var handler = new CountingHandler([]) { Body = new TornContent() };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(handler).LoadAsync("assets/torn.bin"));

        Assert.Contains("assets/torn.bin", exception.Message, StringComparison.Ordinal);
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
            // The licence documents themselves are not assets this site hands to
            // a parser. They are skipped by NAME rather than by extension: a
            // review pointed out that skipping every .txt would let a real image
            // named "stray-image.txt" ship unlicensed and unnoticed.
            if (Path.GetFileName(file) is "LICENSES.md" or "LICENSE.txt")
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
