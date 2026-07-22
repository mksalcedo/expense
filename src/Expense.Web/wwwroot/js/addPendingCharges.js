// Lets AddPendingCharges.razor accept a screenshot straight from the clipboard (Ctrl+V) in
// addition to the InputFile "Choose File" fallback. Kept as a JS-isolated module (dynamically
// imported, not a global <script>) rather than the alternative of reading clipboard.read() from
// .NET - browsers only allow clipboard image access from a user-gesture-triggered 'paste' event,
// so the listener has to live in JS.
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
                break;
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
