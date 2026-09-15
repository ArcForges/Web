// SPDX-License-Identifier: AGPL-3.0-only
import type { ButtonHTMLAttributes, ReactNode } from "react";

export function Arrow() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d="M5 12h14m-6-6 6 6-6 6" stroke="currentColor" strokeWidth="1.6" />
    </svg>
  );
}

export function Button({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button className="button" {...props}>
      {children}
      <Arrow />
    </button>
  );
}

export function Shell({ children }: { children: ReactNode }) {
  const sourceUrl = import.meta.env.VITE_SOURCE_REF
    ? `https://github.com/ArcForges/Web/tree/${import.meta.env.VITE_SOURCE_REF}`
    : "https://github.com/ArcForges/Web";
  return (
    <div className="site-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <a className="brand" href="/" aria-label="ArcForges home">
          <svg width="32" height="32" viewBox="0 0 32 32" fill="none" aria-hidden="true">
            <path
              d="M5 26 16 5l11 21M10 18h12"
              stroke="currentColor"
              strokeWidth="2.5"
              strokeLinejoin="round"
            />
            <path d="M5 26h22" stroke="currentColor" strokeWidth="2.5" />
          </svg>
          <span>ArcForges</span>
        </a>
        <nav aria-label="Main navigation">
          <a href="/hello">Hello example</a>
          <a className="source-link" href={sourceUrl}>
            Source <span aria-hidden="true">↗</span>
          </a>
        </nav>
      </header>
      <main id="main" tabIndex={-1}>
        {children}
      </main>
      <footer className="site-footer">
        <span>A small beginning. Built in the open.</span>
        <a href="/license.txt">AGPL-3.0-only</a>
        <a href="/third-party-notices.txt">Third-party notices</a>
      </footer>
    </div>
  );
}
