// Shared by AddPendingCharges.razor and ReviewQueue.razor - lets either page accept a
// screenshot straight from the clipboard (Ctrl+V) via a DotNetObjectReference callback. Kept
// as a JS-isolated module (dynamically imported, not a global <script>) rather than the
// alternative of reading clipboard.read() from .NET - browsers only allow clipboard image
// access from a user-gesture-triggered 'paste' event, so the listener has to live in JS.
// Only one page-wide listener is ever registered at a time (register calls unregister first),
// so a caller that needs to scope pasting to one specific UI element (e.g. Review Queue's
// per-row "Paste screenshot" mode) does that by only calling registerPasteListener while that
// element is active, not by teaching this module about multiple simultaneous targets.
//
// Also accepts plain-text pastes that look like JSON (e.g. the order-page bookmarklet's
// clipboard output - see docs/amazon-order-scraper-bookmarklet.md) via a second callback,
// OnOrderDataPasted - same listener, same paste target, routed by clipboard content type
// rather than needing a separate button/UI mode.
let currentHandler = null;

export function registerPasteListener(dotNetRef) {
    unregisterPasteListener();

    currentHandler = async (event) => {
        const items = event.clipboardData?.items;
        if (!items) return;

        for (const item of items) {
            if (item.kind === 'file' && item.type.startsWith('image/')) {
                const file = item.getAsFile();
                if (!file) continue;

                try {
                    // The clipboard's own reported type varies by OS/screenshot tool (e.g. some
                    // Linux tools hand the browser image/bmp) and Anthropic's API only accepts
                    // jpeg/png/gif/webp - so always re-encode through a canvas to a known-good
                    // PNG rather than trusting whatever type the clipboard claims.
                    const pngBlob = await convertToPng(file);
                    const buffer = await pngBlob.arrayBuffer();
                    const streamRef = DotNet.createJSStreamReference(new Uint8Array(buffer));
                    await dotNetRef.invokeMethodAsync('OnImagePasted', streamRef, 'image/png');
                    event.preventDefault();
                } catch (error) {
                    console.error('Failed to read pasted image:', error);
                }
                return;
            }
        }

        for (const item of items) {
            if (item.kind === 'string' && item.type === 'text/plain') {
                item.getAsString(async (text) => {
                    const trimmed = text.trim();
                    // Only intercept text that could plausibly be the bookmarklet's JSON -
                    // an ordinary text paste elsewhere in this mode should behave normally
                    // (e.g. editing the title/price inputs also open on this row).
                    if (trimmed.startsWith('{')) {
                        await dotNetRef.invokeMethodAsync('OnOrderDataPasted', trimmed);
                    }
                });
                return;
            }
        }
    };

    document.addEventListener('paste', currentHandler);
}

export function unregisterPasteListener() {
    if (currentHandler) {
        document.removeEventListener('paste', currentHandler);
        currentHandler = null;
    }
}

async function convertToPng(file) {
    const bitmap = await createImageBitmap(file);
    const canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    canvas.getContext('2d').drawImage(bitmap, 0, 0);

    return await new Promise((resolve, reject) => {
        canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('canvas.toBlob returned null')), 'image/png');
    });
}
