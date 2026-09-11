using System.Buffers;
using System.Net;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using VellumPdfShowcase.Web.Model;

namespace VellumPdfShowcase.Web.Assets;

/// <summary>
/// Fetches a binary asset from <c>wwwroot</c> once, on first use, and keeps it
/// for the lifetime of the service.
/// </summary>
/// <remarks>
/// A capability page needs a font only when it demonstrates an embedded font,
/// and an image only when it demonstrates an image, so nothing is fetched
/// ahead of time. Fetching each asset once matters for more than bandwidth: a
/// page that regenerates on every control change would otherwise re-download
/// the same face on every keystroke.
///
/// NOTE: the cache holds a <see cref="Task{TResult}"/> rather than the bytes,
/// which is what makes it safe against two handlers racing. The assignment is
/// one expression with no <c>await</c> between reading the dictionary and
/// writing it, so the second handler joins the first request instead of
/// starting its own. A task caches its FAILURE just as durably, so a faulted
/// entry is evicted before the exception reaches the caller and the next
/// attempt starts a fresh request. Unlike a failed JavaScript module import,
/// an HTTP request genuinely can succeed on a retry, so that eviction is worth
/// having here.
/// </remarks>
public sealed class AssetLoader(HttpClient http)
{
    /// <summary>
    /// How much is read at a time while the body is being counted against the
    /// cap. It bounds the overshoot: the read stops within one buffer of the cap
    /// rather than after the whole of an oversized response has arrived.
    /// </summary>
    /// <remarks>
    /// NOTE the accumulating buffer is a <see cref="MemoryStream"/>, which
    /// doubles as it grows, so the peak is a small multiple of the cap rather
    /// than the cap exactly. That is bounded, which is the property this exists
    /// for, but it is not the same claim.
    /// </remarks>
    private const int ReadChunkBytes = 64 * 1024;

    private readonly Dictionary<string, Task<byte[]>> _cache = [];

    /// <summary>
    /// Returns the bytes of <paramref name="path"/>, fetching it on first use.
    /// </summary>
    /// <param name="path">
    /// A path relative to the host base address, normally a member of
    /// <see cref="ShowcaseAssets"/>.
    /// </param>
    /// <returns>
    /// A fresh copy on every call. The cached array is never handed out
    /// directly, because a caller that mutated it would corrupt what every
    /// later caller receives. The largest asset this site ships is under half
    /// a megabyte, so the copy costs far less than that class of defect.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The asset is missing, the server refused it, or it exceeds
    /// <see cref="SpecLimits.MaxAssetBytes"/>. The message names the path,
    /// because a deployment that drops one file should say which.
    /// </exception>
    public async Task<byte[]> LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_cache.TryGetValue(path, out var pending))
        {
            pending = FetchAsync(path);
            _cache[path] = pending;
        }

        byte[] bytes;
        try
        {
            bytes = await pending;
        }
        catch
        {
            // Only evict the entry this call was actually waiting on. A handler
            // that has already started a fresh request must keep it.
            if (_cache.TryGetValue(path, out var current) && ReferenceEquals(current, pending))
            {
                _cache.Remove(path);
            }

            throw;
        }

        return [.. bytes];
    }

    private async Task<byte[]> FetchAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        // Both of these are required for the cap below to bound anything, and
        // each addresses a different layer. Without ResponseHeadersRead,
        // HttpClient buffers the whole body before the call returns. Without
        // response streaming, the browser handler materialises it inside
        // SendAsync regardless of what HttpClient was asked for, so on the
        // runtime this application actually ships to, omitting either one makes
        // every check below post-hoc: the tab already holds the bytes.
        request.SetBrowserResponseStreamingEnabled(true);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"The asset '{path}' could not be fetched: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    $"The asset '{path}' is missing from the deployed site. Confirm it is present under wwwroot and was published.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The asset '{path}' could not be fetched: the server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            // Refuse on the declared length first, which costs nothing and
            // avoids transferring a body that is already known to be too large.
            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > SpecLimits.MaxAssetBytes)
            {
                throw new InvalidOperationException(
                    $"The asset '{path}' declares {declaredLength:N0} bytes, beyond the {SpecLimits.MaxAssetBytes:N0} byte cap.");
            }

            // NOTE: a server need not declare a length, and need not tell the
            // truth when it does, so the declared length cannot be the only
            // check. The body is counted as it arrives and abandoned the moment
            // it passes the cap, rather than being read whole and measured
            // afterwards, which would let an undeclared body of any size into
            // the tab before anything objected.
            return await ReadCappedAsync(path, response);
        }
    }

    private static async Task<byte[]> ReadCappedAsync(string path, HttpResponseMessage response)
    {
        try
        {
            return await ReadCappedCoreAsync(path, response);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // NOTE: reading the body no longer happens inside the block that
            // wraps the request, because the request now returns as soon as the
            // headers arrive. A transfer that fails part way through would
            // otherwise reach the caller as a bare transport exception naming no
            // asset, outside the contract this class documents.
            throw new InvalidOperationException(
                $"The asset '{path}' could not be fetched: the transfer failed part way through. {ex.Message}", ex);
        }
    }

    private static async Task<byte[]> ReadCappedCoreAsync(string path, HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();

        // Sized from the declared length when there is one, so the ordinary case
        // does not grow its buffer repeatedly, and clamped so that a false
        // declaration cannot make this allocation the denial of service the cap
        // exists to prevent.
        var expected = (int)Math.Clamp(response.Content.Headers.ContentLength ?? 0, 0, SpecLimits.MaxAssetBytes);
        using var accumulated = new MemoryStream(expected);

        var buffer = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, ReadChunkBytes))) > 0)
            {
                if (accumulated.Length + read > SpecLimits.MaxAssetBytes)
                {
                    throw new InvalidOperationException(
                        $"The asset '{path}' exceeds the {SpecLimits.MaxAssetBytes:N0} byte cap, and was abandoned before it was read in full.");
                }

                accumulated.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return accumulated.ToArray();
    }
}
