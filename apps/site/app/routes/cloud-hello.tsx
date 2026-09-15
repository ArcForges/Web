// SPDX-License-Identifier: AGPL-3.0-only
import { Button } from "@arcforges/web-ui";
import { useEffect, useRef, useState } from "react";
import { checkServerConnection } from "../cloud-hello";

export function meta() {
  return [{ title: "Server connection — ArcForges" }];
}

export default function CloudHello() {
  const [ready, setReady] = useState(false);
  const [state, setState] = useState<"idle" | "pending" | "success" | "error">("idle");
  const [reply, setReply] = useState("");
  const active = useRef<AbortController | null>(null);
  useEffect(() => {
    setReady(true);
    return () => active.current?.abort();
  }, []);

  async function connect() {
    if (active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setState("pending");
    try {
      const message = await checkServerConnection(window.location.origin, controller.signal);
      if (!controller.signal.aborted) {
        setReply(message);
        setState("success");
      }
    } catch {
      if (!controller.signal.aborted) setState("error");
    } finally {
      if (active.current === controller) active.current = null;
    }
  }

  const message = {
    idle: "No request sent yet.",
    pending: "Contacting the server…",
    success: reply,
    error: "The server is unavailable or returned an unexpected response. Try again later.",
  }[state];

  return (
    <section className="example">
      <a className="back-link" href="/hello/">
        ← Back to your hello
      </a>
      <p className="eyebrow">Connection example</p>
      <h1>
        A hello, <em>from the server.</em>
      </h1>
      <p className="hero-copy">
        Send a fixed “ArcForges” greeting to check the server connection. Your name from the local
        example stays in your browser. Nothing is sent until you choose to connect.
      </p>
      <Button type="button" disabled={!ready || state === "pending"} onClick={connect}>
        {state === "pending" ? "Connecting…" : "Check connection"}
      </Button>
      <noscript>
        <p className="no-script">Enable JavaScript to check the server connection.</p>
      </noscript>
      <div
        className="greeting"
        role={state === "error" ? "alert" : "status"}
        aria-live="polite"
        aria-atomic="true"
      >
        <span className="greeting-label">Server response</span>
        <p>{message}</p>
      </div>
    </section>
  );
}
