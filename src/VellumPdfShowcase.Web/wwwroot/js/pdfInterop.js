// Interop between the Blazor WebAssembly runtime and the browser's own PDF
// handling. Kept to the three functions the showcase needs: creating a blob
// URL for the <iframe> preview, triggering a download, and detecting whether
// the current browser renders PDF inline.
//
// Revocation deliberately has no export here. The C# side revokes through the
// root IJSRuntime (URL.revokeObjectURL is a global on window), because a
// module reference does not outlive component disposal and a blob URL must
// still be revocable after the module is gone. Exporting a revokeBlobUrl from
// this module would invite a caller to route revocation through it and
// reintroduce that leak.
//
// Bytes arrive from .NET as a DotNetStreamReference, which the JS side reads
// through its `arrayBuffer()` method. That avoids the base64 round trip a
// `byte[]` parameter would force through the JSON interop channel.

export async function createBlobUrl(streamRef) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: "application/pdf" });
    return URL.createObjectURL(blob);
}

export async function downloadBytes(streamRef, fileName) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: "application/pdf" });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName ?? "document.pdf";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    // Safari needs the blob URL to remain valid while it processes the
    // click, so the revocation is deferred rather than immediate.
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function supportsInlinePdf() {
    const ua = navigator.userAgent;
    const isIOS = /iP(hone|ad|od)/.test(ua)
        || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
    const isAndroidChrome = /Android/.test(ua) && /Chrome/.test(ua);

    return !(isIOS || isAndroidChrome);
}
