// Interop between the Blazor WebAssembly runtime and the browser's own PDF
// handling. Kept to the four functions the showcase needs: creating and
// revoking a blob URL for the <iframe> preview, triggering a download, and
// detecting whether the current browser renders PDF inline.
//
// Bytes arrive from .NET as a DotNetStreamReference, which the JS side reads
// through its `arrayBuffer()` method. That avoids the base64 round trip a
// `byte[]` parameter would force through the JSON interop channel.

export async function createBlobUrl(streamRef) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: "application/pdf" });
    return URL.createObjectURL(blob);
}

export function revokeBlobUrl(url) {
    if (!url) {
        return;
    }

    URL.revokeObjectURL(url);
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
