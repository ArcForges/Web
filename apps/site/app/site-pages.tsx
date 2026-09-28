// SPDX-License-Identifier: AGPL-3.0-only
import { Arrow, Button } from "@arcforges/web-ui";
import { type FormEvent, useEffect, useRef, useState } from "react";
import { checkServerConnection } from "./cloud-hello";
import { greet } from "./hello";
import type { ExamplePage, HomePage } from "./site-manifest";

export function HomePageView({ page }: { page: HomePage }) {
  return (
    <>
      <section className="hero">
        <div>
          <p className="eyebrow">
            <span className="status-dot" aria-hidden="true" />
            {page.eyebrow}
          </p>
          <h1>
            {page.headingFirst}
            <br />
            <em>{page.headingSecond}</em>
          </h1>
          <p className="hero-copy">
            {page.lead.map((line, index) => (
              <span key={line}>
                {index > 0 && <br />}
                {line}
              </span>
            ))}
          </p>
          <a className="button" href={page.primaryLink.href}>
            {page.primaryLink.label}
            <Arrow />
          </a>
        </div>
        <div className="hello-art" aria-hidden="true">
          <span>hello.</span>
        </div>
      </section>
      <section className="intro-line" aria-labelledby="intro-title">
        <h2 id="intro-title">{page.introTitle}</h2>
        <p>{page.introBody}</p>
      </section>
    </>
  );
}

export function HelloPageView({ page }: { page: ExamplePage }) {
  const [name, setName] = useState("World");
  const [message, setMessage] = useState("Hello, World!");
  const [error, setError] = useState("");
  const [ready, setReady] = useState(false);
  useEffect(() => {
    setReady(true);
  }, []);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      setMessage(greet(name));
      setError("");
    } catch {
      setError("Enter a name between 1 and 80 characters, without control characters.");
    }
  }
  return (
    <section className="example">
      <a className="back-link" href="/">
        ← Back home
      </a>
      <p className="eyebrow">
        <span className="status-dot" aria-hidden="true" />
        Hello example
      </p>
      <h1>{page.title}</h1>
      <p className="hero-copy">{page.description}</p>
      <form className="hello-form" onSubmit={submit}>
        <label htmlFor="hello-name">Your name</label>
        <div className="input-row">
          <input
            id="hello-name"
            disabled={!ready}
            value={name}
            onChange={(event) => setName(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            aria-invalid={Boolean(error)}
            aria-describedby={error ? "name-error name-hint" : "name-hint"}
          />
          <Button type="submit" disabled={!ready}>
            Say hello
          </Button>
        </div>
        <p id="name-hint" className="field-hint">
          Up to 80 characters. No account needed.
        </p>
        {error && (
          <p id="name-error" className="field-error" role="alert">
            {error}
          </p>
        )}
      </form>
      <noscript>
        <p className="no-script">
          The example greeting is shown below. Enable JavaScript to personalize it.
        </p>
      </noscript>
      <div className="greeting" role="status" aria-live="polite" aria-atomic="true">
        <span className="greeting-label">Your greeting</span>
        <p>{message}</p>
      </div>
      <p className="field-hint">
        <a href="/cloud-hello/">Check the server connection →</a>
      </p>
    </section>
  );
}

export function CloudHelloPageView({ page }: { page: ExamplePage }) {
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
      <h1>{page.title}</h1>
      <p className="hero-copy">{page.description}</p>
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
