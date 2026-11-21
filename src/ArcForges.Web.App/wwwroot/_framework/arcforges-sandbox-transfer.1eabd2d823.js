// SPDX-License-Identifier: AGPL-3.0-only

export async function saveVerifiedFile(fileName, stream) {
    const bytes = await stream.arrayBuffer();
    if (bytes.byteLength > 16 * 1024 * 1024) throw new RangeError("The browser download exceeds its limit.");

    const url = URL.createObjectURL(new Blob([bytes], { type: "application/octet-stream" }));
    const link = document.createElement("a");
    try {
        link.href = url;
        link.download = fileName;
        link.hidden = true;
        document.body.append(link);
        link.click();
    } finally {
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
}
