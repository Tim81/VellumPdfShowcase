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
    private IJSObjectReference? _module;
    private byte[]? _fontBytes;
    private byte[]? _iccProfileBytes;

    private bool _busyHardCoded;
    private byte[]? _hardCodedBytes;
    private string? _hardCodedBlobUrl;
    private bool _hardCodedSupportsInline = true;
    private long _hardCodedElapsedMs;
    private string? _hardCodedError;

    private bool _busyPdfA;
    private byte[]? _pdfABytes;
    private string? _pdfABlobUrl;
    private bool _pdfASupportsInline = true;
    private long _pdfAGenerationElapsedMs;
    private long _preflightElapsedMs;
    private PreflightResult? _preflightResult;
    private string? _pdfAError;

    private async Task<IJSObjectReference> GetModuleAsync() =>
        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/pdfInterop.js");

    /// <summary>
    /// Resolves the interop module, guarding against the component having been
    /// disposed while the import (or an earlier await in the caller) was in
    /// flight. If disposal happened first, any module reference this call
    /// itself just imported is disposed here rather than left dangling on the
    /// disposed component, and <see langword="null"/> is returned so the
    /// caller stops without touching component state or creating a blob URL.
    /// </summary>
    private async Task<IJSObjectReference?> GetModuleIfActiveAsync()
    {
        var module = await GetModuleAsync();

        if (!_disposed)
        {
            return module;
        }

        if (_module is not null)
        {
            var leaked = _module;
            _module = null;
            await leaked.DisposeAsync();
        }

        return null;
    }

    private async Task GenerateHardCodedAsync()
    {
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
            var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            var newUrl = await module.InvokeAsync<string>("createBlobUrl", streamRef);

            if (_disposed)
            {
                await module.InvokeVoidAsync("revokeBlobUrl", newUrl);
                return;
            }

            _hardCodedBytes = bytes;
            _hardCodedElapsedMs = stopwatch.ElapsedMilliseconds;
            _hardCodedSupportsInline = supportsInline;
            _hardCodedBlobUrl = newUrl;

            if (previousUrl is not null)
            {
                await module.InvokeVoidAsync("revokeBlobUrl", previousUrl);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _hardCodedError = ex.ToString();
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
            var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            var newUrl = await module.InvokeAsync<string>("createBlobUrl", streamRef);

            if (_disposed)
            {
                await module.InvokeVoidAsync("revokeBlobUrl", newUrl);
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
                await module.InvokeVoidAsync("revokeBlobUrl", previousUrl);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _pdfAError = ex.ToString();
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

    private async Task DownloadAsync(byte[]? bytes, string fileName)
    {
        if (bytes is null || _disposed)
        {
            return;
        }

        var module = await GetModuleIfActiveAsync();
        if (module is null)
        {
            return;
        }

        var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
        await module.InvokeVoidAsync("downloadBytes", streamRef, fileName);
    }

    private static byte[] BuildHardCodedDocument()
    {
        using var document = new Document();

        document.Add(new Heading("VellumPdf Showcase Runtime Test") { Level = 1 });
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

        document.Add(new Heading("PDF/A-2b Conformance Sample", headingStyle) { Level = 1, Language = "en" });
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

        if (_module is null)
        {
            return;
        }

        var module = _module;
        _module = null;

        if (_hardCodedBlobUrl is not null)
        {
            await module.InvokeVoidAsync("revokeBlobUrl", _hardCodedBlobUrl);
        }

        if (_pdfABlobUrl is not null)
        {
            await module.InvokeVoidAsync("revokeBlobUrl", _pdfABlobUrl);
        }

        await module.DisposeAsync();
    }
}
