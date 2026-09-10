using System.Diagnostics;
using Microsoft.JSInterop;
using VellumPdf.Conformance;
using VellumPdf.Layout;
using VellumPdf.Layout.Core;
using VellumPdf.Layout.Elements;
using VellumPdf.Layout.Elements.Table;
using DocumentConformance = VellumPdf.Document.PdfConformance;
using PreflightConformance = VellumPdf.Conformance.PdfConformance;

namespace VellumPdfShowcase.Web.Components.Pages;

public partial class Smoke
{
    // VellumPdf.Layout keeps the underlying PdfDocument private, so a PDF/A
    // claim can only be honoured with a fully embedded face. Document.Conformance
    // (VellumPdf.Document.PdfConformance) and PdfPreflight.Validate
    // (VellumPdf.Conformance.PdfConformance) are two distinct enum types with
    // no conversion between them; both are referenced explicitly below.
    private const string LiberationSansPath = "fonts/LiberationSans-Regular.ttf";

    // ICC v2 profile "sRGB2014.icc", published by the International Color
    // Consortium at https://registry.color.org/rgb-registry/srgbprofiles.
    // Licence recorded at wwwroot/icc/LICENSE.txt.
    private const string SrgbIccProfilePath = "icc/sRGB2014.icc";

    private bool _disposed;
    private Task<IJSObjectReference>? _moduleTask;
    private byte[]? _fontBytes;
    private byte[]? _iccProfileBytes;

    private bool _busyHardCoded;
    private byte[]? _hardCodedBytes;
    private string? _hardCodedBlobUrl;
    private bool _hardCodedSupportsInline = true;
    private long _hardCodedElapsedMs;
    private string? _hardCodedError;
    private bool _busyHardCodedDownload;
    private string? _hardCodedDownloadError;

    private bool _busyPdfA;
    private byte[]? _pdfABytes;
    private string? _pdfABlobUrl;
    private bool _pdfASupportsInline = true;
    private long _pdfAGenerationElapsedMs;
    private long _preflightElapsedMs;
    private PreflightResult? _preflightResult;
    private string? _pdfAError;
    private bool _busyPdfADownload;
    private string? _pdfADownloadError;

    /// <summary>
    /// Caches the import as a <see cref="Task{TResult}"/> rather than the
    /// resolved <see cref="IJSObjectReference"/>. The null-coalescing
    /// assignment below is one synchronous statement with no <c>await</c>
    /// between reading <c>_moduleTask</c> and writing it, so two handlers
    /// racing at an earlier await point both observe the same cached task
    /// instead of each starting its own import. Caching the resolved
    /// reference behind a plain <c>_module ??= await ...</c> would not have
    /// this property: the read-await-write spans an await, so a second
    /// caller can still see the field unset and import a second, orphaned
    /// module reference.
    /// </summary>
    private Task<IJSObjectReference> GetModuleAsync() =>
        _moduleTask ??= JS.InvokeAsync<IJSObjectReference>("import", "./js/pdfInterop.js").AsTask();

    /// <summary>
    /// Resolves the interop module, guarding against the component having been
    /// disposed while the import (or an earlier await in the caller) was in
    /// flight. If disposal happened first, <see cref="DisposeAsync"/> has
    /// already awaited the same cached task and disposed the module itself,
    /// so this returns <see langword="null"/> without touching component
    /// state or creating a blob URL.
    /// </summary>
    private async Task<IJSObjectReference?> GetModuleIfActiveAsync()
    {
        var module = await GetModuleAsync();
        return _disposed ? null : module;
    }

    private async Task GenerateHardCodedAsync()
    {
        if (_busyHardCoded)
        {
            return;
        }

        _busyHardCoded = true;
        _hardCodedError = null;
        StateHasChanged();

        try
        {
            await Task.Delay(1);
            if (_disposed)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var bytes = BuildHardCodedDocument();
            stopwatch.Stop();

            var module = await GetModuleIfActiveAsync();
            if (module is null)
            {
                return;
            }

            var supportsInline = await module.InvokeAsync<bool>("supportsInlinePdf");
            if (_disposed)
            {
                return;
            }

            var previousUrl = _hardCodedBlobUrl;
            using var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            var newUrl = await module.InvokeAsync<string>("createBlobUrl", streamRef);

            if (_disposed)
            {
                await RevokeBlobUrlAsync(newUrl);
                return;
            }

            _hardCodedBytes = bytes;
            _hardCodedElapsedMs = stopwatch.ElapsedMilliseconds;
            _hardCodedSupportsInline = supportsInline;
            _hardCodedBlobUrl = newUrl;

            if (previousUrl is not null)
            {
                await RevokeBlobUrlAsync(previousUrl);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _hardCodedError = ex.ToString();

                var staleUrl = _hardCodedBlobUrl;
                _hardCodedBytes = null;
                _hardCodedBlobUrl = null;
                _hardCodedSupportsInline = true;

                await RevokeBlobUrlAsync(staleUrl);
            }
        }
        finally
        {
            if (!_disposed)
            {
                _busyHardCoded = false;
                StateHasChanged();
            }
        }
    }

    private async Task GeneratePdfAAsync()
    {
        if (_busyPdfA)
        {
            return;
        }

        _busyPdfA = true;
        _pdfAError = null;
        StateHasChanged();

        try
        {
            await Task.Delay(1);
            if (_disposed)
            {
                return;
            }

            _fontBytes ??= await Http.GetByteArrayAsync(LiberationSansPath);
            if (_disposed)
            {
                return;
            }

            _iccProfileBytes ??= await Http.GetByteArrayAsync(SrgbIccProfilePath);
            if (_disposed)
            {
                return;
            }

            var generationStopwatch = Stopwatch.StartNew();
            var bytes = BuildPdfA2BDocument(_fontBytes, _iccProfileBytes);
            generationStopwatch.Stop();
            var generationElapsedMs = generationStopwatch.ElapsedMilliseconds;

            var preflightStopwatch = Stopwatch.StartNew();
            var preflightResult = PdfPreflight.Validate(bytes, PreflightConformance.PdfA2B);
            preflightStopwatch.Stop();
            var preflightElapsedMs = preflightStopwatch.ElapsedMilliseconds;

            var module = await GetModuleIfActiveAsync();
            if (module is null)
            {
                return;
            }

            var supportsInline = await module.InvokeAsync<bool>("supportsInlinePdf");
            if (_disposed)
            {
                return;
            }

            var previousUrl = _pdfABlobUrl;
            using var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            var newUrl = await module.InvokeAsync<string>("createBlobUrl", streamRef);

            if (_disposed)
            {
                await RevokeBlobUrlAsync(newUrl);
                return;
            }

            _pdfAGenerationElapsedMs = generationElapsedMs;
            _pdfABytes = bytes;
            _preflightElapsedMs = preflightElapsedMs;
            _preflightResult = preflightResult;
            _pdfASupportsInline = supportsInline;
            _pdfABlobUrl = newUrl;

            if (previousUrl is not null)
            {
                await RevokeBlobUrlAsync(previousUrl);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _pdfAError = ex.ToString();

                var staleUrl = _pdfABlobUrl;
                _pdfABytes = null;
                _pdfABlobUrl = null;
                _pdfASupportsInline = true;
                _preflightResult = null;

                await RevokeBlobUrlAsync(staleUrl);
            }
        }
        finally
        {
            if (!_disposed)
            {
                _busyPdfA = false;
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// Revokes a blob URL through the root <see cref="IJSRuntime"/> rather than the
    /// interop module. <c>URL.revokeObjectURL</c> is a global on <c>window</c>, and the
    /// root runtime outlives the module, so this stays safe to call after the module has
    /// been disposed, whether by a concurrent <see cref="DisposeAsync"/> mid-generation or
    /// during teardown itself. Any failure is swallowed here, in its own try/catch, rather
    /// than left to the caller's broader catch, so it cannot mask an unrelated exception.
    /// </summary>
    private async Task RevokeBlobUrlAsync(string? url)
    {
        if (url is null)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("URL.revokeObjectURL", url);
        }
        catch
        {
            // Best-effort: the browsing context may already be gone (navigation away),
            // or the runtime may be mid-teardown. Either way there is nothing further
            // to clean up and nothing to surface to the visitor.
        }
    }

    private Task DownloadHardCodedAsync() =>
        DownloadAsync(
            _hardCodedBytes,
            "smoke-hardcoded.pdf",
            () => _busyHardCodedDownload,
            busy => _busyHardCodedDownload = busy,
            error => _hardCodedDownloadError = error);

    private Task DownloadPdfAAsync() =>
        DownloadAsync(
            _pdfABytes,
            "smoke-pdfa2b.pdf",
            () => _busyPdfADownload,
            busy => _busyPdfADownload = busy,
            error => _pdfADownloadError = error);

    /// <summary>
    /// Downloads a generated document through the interop module. Wrapped in
    /// the same try/catch/finally shape as <see cref="GenerateHardCodedAsync"/>
    /// and <see cref="GeneratePdfAAsync"/>, so a <see cref="JSException"/> or a
    /// <see cref="JSDisconnectedException"/> from a disposal interleaving at
    /// the await boundary surfaces as a displayed error instead of an
    /// unhandled exception in an event handler, which is what drives the
    /// Blazor error bar. Guarded against re-entrancy with the same busy-flag
    /// pattern as generation, since each click pins another full copy of the
    /// document in a blob URL for ten seconds before revocation.
    /// </summary>
    private async Task DownloadAsync(
        byte[]? bytes,
        string fileName,
        Func<bool> isBusy,
        Action<bool> setBusy,
        Action<string?> setError)
    {
        if (bytes is null || _disposed || isBusy())
        {
            return;
        }

        setBusy(true);
        setError(null);
        StateHasChanged();

        try
        {
            var module = await GetModuleIfActiveAsync();
            if (module is null)
            {
                return;
            }

            using var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            await module.InvokeVoidAsync("downloadBytes", streamRef, fileName);
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                setError(ex.ToString());
            }
        }
        finally
        {
            if (!_disposed)
            {
                setBusy(false);
                StateHasChanged();
            }
        }
    }

    private static byte[] BuildHardCodedDocument()
    {
        using var document = new Document();

        document.Add(new Heading("VellumPdf Showcase Runtime Test") { Level = 0 });
        document.Add(new Paragraph(
            "This document was generated by VellumPdf.Layout, running inside a WebAssembly module in this browser tab. No server received this request."));

        var table = new TableElement();
        var header = table.AddHeaderRow();
        header.AddCell("Element");
        header.AddCell("Source type");

        AddCapabilityRow(table, "Heading", "VellumPdf.Layout.Elements.Heading");
        AddCapabilityRow(table, "Paragraph", "VellumPdf.Layout.Elements.Paragraph");
        AddCapabilityRow(table, "Table", "VellumPdf.Layout.Elements.Table.TableElement");
        AddCapabilityRow(table, "Pie chart", "VellumPdf.Layout.Elements.PieChart");

        document.Add(table);

        document.Add(new PieChart
        {
            Diameter = 140,
            StartAngle = 0,
            Clockwise = true,
            AltText = "A pie chart with two slices: three quarters rendered, one quarter pending.",
            Slices =
            [
                new PieSlice(75, new ColorRgb(0.11, 0.35, 0.62), "Rendered"),
                new PieSlice(25, new ColorRgb(0.78, 0.78, 0.78), "Pending"),
            ],
        });

        document.SetFooter("Page {page} of {pages}");

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void AddCapabilityRow(TableElement table, string element, string source)
    {
        var row = table.AddRow();
        row.AddCell(element);
        row.AddCell(source);
    }

    private static byte[] BuildPdfA2BDocument(byte[] fontBytes, byte[] iccProfileBytes)
    {
        using var document = new Document
        {
            Conformance = DocumentConformance.PdfA2b,
            Tagged = true,
            Language = "en",
        };

        var fontHandle = document.UseTrueTypeFont(fontBytes);
        var headingStyle = new TextStyle { FontRef = fontHandle, FontSize = 18 };
        var bodyStyle = new TextStyle { FontRef = fontHandle, FontSize = 11 };
        document.SetDefaultFont(bodyStyle);

        document.Add(new Heading("PDF/A-2b Conformance Sample", headingStyle) { Level = 0, Language = "en" });
        document.Add(new Paragraph(
            "This document embeds a Liberation Sans face and declares an sRGB output intent. VellumPdf.Conformance validates the result against the PDF/A-2b profile immediately below, in this browser.",
            bodyStyle));

        document.SetFooter("Page {page} of {pages}", bodyStyle);
        document.SetPdfAOutputIntent(iccProfileBytes, 3, "sRGB IEC61966-2.1");

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        // Revoked through the root IJSRuntime, not the module, so this drains both
        // URLs even when a generation still in flight races this disposal and the
        // module ends up disposed first. See RevokeBlobUrlAsync.
        await RevokeBlobUrlAsync(_hardCodedBlobUrl);
        await RevokeBlobUrlAsync(_pdfABlobUrl);

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
            // The import itself never completed; there is no module reference
            // to dispose, and nothing further to clean up.
            return;
        }

        await module.DisposeAsync();
    }
}
