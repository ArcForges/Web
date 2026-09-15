// SPDX-License-Identifier: AGPL-3.0-only
import { Button } from "@arcforges/web-ui";
import { type FormEvent, useEffect, useState } from "react";
import { greet } from "../hello";

export function meta() {
  return [{ title: "Your hello — ArcForges" }];
}

export default function Hello() {
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
      <h1>
        A hello, <em>just for you.</em>
      </h1>
      <p className="hero-copy">
        Start with your name. This example runs in your browser, and your name stays on this page.
      </p>
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
    </section>
  );
}
