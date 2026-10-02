// SPDX-License-Identifier: AGPL-3.0-only
import { Button } from "@arcforges/web-ui";
import { useEffect, useRef, useState } from "react";
import { failureText, isProbeFailure } from "../probe/failure.ts";
import { sayHello } from "../probe/hello.ts";

export function meta() {
  return [{ title: "Chat probe — ArcForges" }];
}

interface Entry {
  id: number;
  kind: "you" | "server" | "notice";
  text: string;
}
const maxEntries = 20;

export default function Chat() {
  const [name, setName] = useState("ArcForges");
  const [entries, setEntries] = useState<Entry[]>([]);
  const [pending, setPending] = useState(false);
  const active = useRef<AbortController | null>(null);
  const counter = useRef(0);

  useEffect(() => () => active.current?.abort(), []);

  function add(...next: Omit<Entry, "id">[]) {
    setEntries((current) =>
      [...current, ...next.map((entry) => ({ ...entry, id: ++counter.current }))].slice(
        -maxEntries,
      ),
    );
  }

  async function send() {
    if (active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setPending(true);
    add({ kind: "you", text: name });
    try {
      const reply = await sayHello(
        window.location.origin,
        name,
        controller.signal,
        window.fetch.bind(window),
      );
      add({ kind: "server", text: reply });
    } catch (error) {
      add({
        kind: "notice",
        text: isProbeFailure(error) ? failureText[error.kind] : failureText.unexpected,
      });
    } finally {
      if (active.current === controller) active.current = null;
      setPending(false);
    }
  }

  return (
    <section className="example">
      <p className="eyebrow">Chat profile</p>
      <h1>
        Say hello, <em>to the server.</em>
      </h1>
      <p className="hero-copy">
        Each message is one anonymous gRPC-Web call. Nothing is sent until you choose to send.
      </p>
      <noscript>
        <p className="no-script">Enable JavaScript to send a message.</p>
      </noscript>
      <form
        className="field"
        onSubmit={(event) => {
          event.preventDefault();
          void send();
        }}
      >
        <label>
          Name <input value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <Button type="submit" disabled={pending}>
          {pending ? "Sending…" : "Send"}
        </Button>
        {pending ? (
          <button type="button" className="button" onClick={() => active.current?.abort()}>
            Cancel
          </button>
        ) : null}
      </form>
      <ol className="transcript" aria-live="polite" aria-label="Messages">
        {entries.map((entry) => (
          <li key={entry.id}>
            <span className="kind">{entry.kind}</span>
            {entry.text}
          </li>
        ))}
      </ol>
    </section>
  );
}
