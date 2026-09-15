// SPDX-License-Identifier: AGPL-3.0-only
import { Arrow } from "@arcforges/web-ui";

export function meta() {
  return [
    { title: "Hello, world. — ArcForges" },
    { name: "description", content: "A small, open-source beginning for ArcForges Web." },
  ];
}

export default function Home() {
  return (
    <>
      <section className="hero">
        <div>
          <p className="eyebrow">
            <span className="status-dot" aria-hidden="true" />
            ArcForges Web · Early preview
          </p>
          <h1>
            Hello,
            <br />
            <em>world.</em>
          </h1>
          <p className="hero-copy">
            Every good thing starts somewhere.
            <br />
            Welcome to the first page of ArcForges Web.
          </p>
          <a className="button" href="/hello">
            Make it your hello
            <Arrow />
          </a>
        </div>
        <div className="hello-art" aria-hidden="true">
          <span>hello.</span>
        </div>
      </section>
      <section className="intro-line" aria-labelledby="intro-title">
        <h2 id="intro-title">A beginning you can explore.</h2>
        <p>
          This small preview has one job: say hello. Try the example, look around the source, and
          follow along as ArcForges takes shape.
        </p>
      </section>
    </>
  );
}
