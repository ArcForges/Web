// SPDX-License-Identifier: AGPL-3.0-only
export function subscribe(callback) {
    let disposed = false;
    let scheduled = false;
    function resume() {
        if (disposed || scheduled || document.visibilityState === "hidden") return;
        scheduled = true;
        queueMicrotask(() => {
            scheduled = false;
            if (!disposed && document.visibilityState !== "hidden") {
                callback.invokeMethodAsync("NotifyResumed").catch(() => {});
            }
        });
    }

    window.addEventListener("focus", resume);
    window.addEventListener("online", resume);
    document.addEventListener("visibilitychange", resume);
    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            window.removeEventListener("focus", resume);
            window.removeEventListener("online", resume);
            document.removeEventListener("visibilitychange", resume);
        },
    };
}

// The caller validates the challenge and identifiers. This bridge translates the browser's BufferSource fields.
export async function getPasskey(rpId, challenge, credentials) {
    const allowCredentials = credentials ? credentials.split(".").map(id => ({ type: "public-key", id: decode(id) })) : [];
    const credential = await navigator.credentials.get({
        publicKey: { rpId, challenge: decode(challenge), allowCredentials, userVerification: "required" },
    });
    if (!credential || credential.type !== "public-key") return null;

    const { clientDataJSON, authenticatorData, signature, userHandle } = credential.response;
    return [credential.rawId, clientDataJSON, authenticatorData, signature, userHandle].map(encode).join(".");
}

function decode(text) {
    return Uint8Array.from(atob(text.replaceAll("-", "+").replaceAll("_", "/")), character => character.charCodeAt(0));
}

function encode(buffer) {
    if (buffer === null || buffer === undefined) return "";
    // Bound this transfer before spreading into a browser string. The caller applies the field-specific limits.
    if (buffer.byteLength > 16384) throw new RangeError("The passkey response is too large to transfer.");
    return btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/=+$/, "").replaceAll("+", "-").replaceAll("/", "_");
}
