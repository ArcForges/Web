// SPDX-License-Identifier: AGPL-3.0-only
import { Button } from "@arcforges/web-ui";
import { useCallback, useEffect, useRef, useState } from "react";
import { type FailureKind, failureText, isProbeFailure } from "../probe/failure.ts";
import { endSession, readSession, type SessionSnapshot } from "../probe/session.ts";

export function meta() {
  return [{ title: "Account session — ArcForges" }];
}

type View =
  | { phase: "loading" }
  | { phase: "ready"; session: SessionSnapshot }
  | { phase: "failed"; kind: FailureKind }
  | { phase: "ended" };

function kindOf(error: unknown): FailureKind {
  return isProbeFailure(error) ? error.kind : "unexpected";
}

export default function Account() {
  const [view, setView] = useState<View>({ phase: "loading" });
  const [working, setWorking] = useState(false);
  const active = useRef<AbortController | null>(null);

  const run = useCallback(async function run<T>(
    task: (signal: AbortSignal) => Promise<T>,
  ): Promise<T | undefined> {
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    try {
      const result = await task(controller.signal);
      return controller.signal.aborted ? undefined : result;
    } catch (error) {
      if (!controller.signal.aborted) {
        const kind = kindOf(error);
        // An ended session is reported as such, never as a transient fault.
        setView(kind === "unauthenticated" ? { phase: "ended" } : { phase: "failed", kind });
      }
      return undefined;
    } finally {
      if (active.current === controller) active.current = null;
    }
  }, []);

  const check = useCallback(() => {
    setView({ phase: "loading" });
    void run((signal) =>
      readSession({ origin: window.location.origin, signal, fetcher: window.fetch.bind(window) }),
    ).then((session) => {
      if (session) setView({ phase: "ready", session });
    });
  }, [run]);

  useEffect(() => {
    // The session is read once when the page opens.
    check();
    return () => active.current?.abort();
  }, [check]);

  function signOut(csrfToken: string) {
    setWorking(true);
    void run((signal) =>
      endSession({
        origin: window.location.origin,
        signal,
        fetcher: window.fetch.bind(window),
        csrfToken,
      }),
    ).then((effect) => {
      setWorking(false);
      if (effect !== undefined) setView({ phase: "ended" });
    });
  }

  const session = view.phase === "ready" ? view.session : undefined;
  return (
    <section className="example">
      <p className="eyebrow">Account profile</p>
      <h1>
        Your session, <em>as the server sees it.</em>
      </h1>
      <noscript>
        <p className="no-script">Enable JavaScript to read your session.</p>
      </noscript>
      <div className="panel" role="status" aria-live="polite" aria-atomic="true">
        {view.phase === "loading" ? <p>Checking your session…</p> : null}
        {view.phase === "ended" ? <p>You are signed out. Reload to check again.</p> : null}
        {view.phase === "failed" ? <p role="alert">{failureText[view.kind]}</p> : null}
        {session?.state === "anonymous" ? <p>You are not signed in.</p> : null}
        {session?.state === "authenticated" ? (
          <dl className="facts">
            <dt>Signed in as</dt>
            <dd>{session.displayName ?? "(no display name)"}</dd>
            <dt>Locale</dt>
            <dd>{session.locale ?? "—"}</dd>
            <dt>Time zone</dt>
            <dd>{session.timezone ?? "—"}</dd>
            <dt>Workspaces</dt>
            <dd>{session.workspaces}</dd>
            <dt>Session ends</dt>
            <dd>{session.expiresAt}</dd>
            <dt>Idle expiry</dt>
            <dd>{session.idleExpiresAt ?? "—"}</dd>
            <dt>Recovery generation</dt>
            <dd className="exact">{session.recoveryGeneration}</dd>
          </dl>
        ) : null}
      </div>
      <div className="field">
        {session?.state === "authenticated" ? (
          <Button type="button" disabled={working} onClick={() => signOut(session.csrfToken)}>
            {working ? "Signing out…" : "Sign out"}
          </Button>
        ) : null}
        {view.phase === "failed" ? (
          <Button type="button" onClick={check}>
            Try again
          </Button>
        ) : null}
      </div>
    </section>
  );
}
