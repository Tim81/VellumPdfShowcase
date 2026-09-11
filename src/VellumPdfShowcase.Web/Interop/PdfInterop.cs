using Microsoft.JSInterop;

namespace VellumPdfShowcase.Web.Interop;

/// <summary>
/// Owns the one JavaScript module this site loads, and the three operations it
/// exports: creating a blob URL for a preview, handing bytes to the browser as a
/// download, and asking whether this browser renders a PDF inline.
/// </summary>
/// <remarks>
/// This exists as a service rather than as a field on each page because more
/// than one page previews a document, and two pages each importing the module
/// would hold two references to it.
///
/// NOTE revocation deliberately does NOT go through the module. <c>revokeObjectURL</c>
/// is a global, and the module reference does not outlive disposal, so revoking
/// through it fails exactly when it matters most: a disposal landing mid-generation
/// finds the module already gone, the call throws, and the URL leaks silently.
/// Revoking through the root runtime is wrapped in its own catch so that teardown
/// cannot mask a failure on the live path.
///
/// NOTE bytes cross the boundary as a <see cref="DotNetStreamReference"/> and
/// never as a byte array. An array is base64-encoded inside a JSON payload, a
/// third larger, with a full encode and decode on the single thread available,
/// on every regeneration.
/// </remarks>
public sealed class PdfInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./js/pdfInterop.js";

    private Task<IJSObjectReference>? _moduleTask;
    private bool _disposed;

    /// <summary>Whether this browser renders a PDF inline in a frame.</summary>
    /// <remarks>
    /// Asked once and remembered. It cannot change for the lifetime of a page,
    /// and asking on every regeneration would cost an interop round trip for an
    /// answer already known.
    /// </remarks>
    public bool? SupportsInlinePdf { get; private set; }

    /// <summary>Creates a blob URL for <paramref name="bytes"/>, or null once disposed.</summary>
    public async Task<string?> CreateBlobUrlAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var module = await GetModuleIfActiveAsync();
        if (module is null)
        {
            return null;
        }

        using var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
        return await module.InvokeAsync<string>("createBlobUrl", streamRef);
    }

    /// <summary>Hands <paramref name="bytes"/> to the browser as a download.</summary>
    public async Task DownloadAsync(byte[] bytes, string fileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var module = await GetModuleIfActiveAsync();
        if (module is null)
        {
            return;
        }

        using var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
        await module.InvokeVoidAsync("downloadBytes", streamRef, fileName);
    }

    /// <summary>Asks the browser whether it renders a PDF inline, once per session.</summary>
    public async Task<bool> DetectInlineSupportAsync()
    {
        if (SupportsInlinePdf is { } known)
        {
            return known;
        }

        var module = await GetModuleIfActiveAsync();
        if (module is null)
        {
            // Assume the conservative answer rather than remembering it: a
            // disposed component's answer must not become the cached one.
            return false;
        }

        var supported = await module.InvokeAsync<bool>("supportsInlinePdf");
        SupportsInlinePdf = supported;
        return supported;
    }

    /// <summary>
    /// Revokes a blob URL through the root runtime, never through the module.
    /// Does nothing for null, so a caller need not test before calling.
    /// </summary>
    public async Task RevokeBlobUrlAsync(string? url)
    {
        if (url is null)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("URL.revokeObjectURL", url);
        }
        catch
        {
            // Best effort. The browsing context may already be gone, or the
            // runtime may be mid-teardown. Either way there is nothing further to
            // clean up and nothing to surface to the visitor.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        var moduleTask = _moduleTask;
        _moduleTask = null;
        if (moduleTask is null)
        {
            return;
        }

        IJSObjectReference module;
        try
        {
            module = await moduleTask;
        }
        catch
        {
            // The import never completed, so there is no reference to dispose.
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The browsing context has gone. Nothing to release.
        }
    }

    private Task<IJSObjectReference> GetModuleAsync() =>
        _moduleTask ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    private async Task<IJSObjectReference?> GetModuleIfActiveAsync()
    {
        if (_disposed)
        {
            return null;
        }

        var moduleTask = GetModuleAsync();

        IJSObjectReference module;
        try
        {
            module = await moduleTask;
        }
        catch
        {
            // A faulted import must not become what every later caller receives.
            // Evict only the task this call awaited, so a caller that has already
            // started a fresh import keeps it.
            //
            // NOTE what this does NOT recover, because the obvious claim for it is
            // false: the browser records a failed dynamic import in its own module
            // map, keyed by specifier, so a second import of the same path fails at
            // once with no further request. Only a new page load clears that. The
            // eviction is for a fault on the .NET side of the call.
            if (ReferenceEquals(_moduleTask, moduleTask))
            {
                _moduleTask = null;
            }

            throw;
        }

        return _disposed ? null : module;
    }
}
