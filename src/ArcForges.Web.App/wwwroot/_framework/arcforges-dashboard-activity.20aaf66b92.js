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
