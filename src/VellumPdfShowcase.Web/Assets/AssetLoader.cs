using System.Net;
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
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(path);
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

            // Refuse on the declared length before reading a body, where the
            // server declares one. This is the only check that can decline an
            // oversized asset without first holding all of it in the tab.
            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > SpecLimits.MaxAssetBytes)
            {
                throw new InvalidOperationException(
                    $"The asset '{path}' declares {declaredLength:N0} bytes, beyond the {SpecLimits.MaxAssetBytes:N0} byte cap.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();

            // A server need not declare a length, and need not tell the truth
            // when it does, so the real length is checked as well.
            return bytes.Length > SpecLimits.MaxAssetBytes
                ? throw new InvalidOperationException(
                    $"The asset '{path}' is {bytes.Length:N0} bytes, beyond the {SpecLimits.MaxAssetBytes:N0} byte cap.")
                : bytes;
        }
    }
}
