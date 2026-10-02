// SPDX-License-Identifier: AGPL-3.0-only
import "@arcforges/web-ui/styles.css";
import "./app.css";
import type { ReactNode } from "react";
import {
  isRouteErrorResponse,
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
  useRouteError,
} from "react-router";

export function Layout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <meta name="robots" content="noindex, nofollow" />
        <meta name="theme-color" content="#f4f3ed" />
        <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
        <Meta />
        <Links />
      </head>
      <body>
        <div className="site-shell">
          <a className="skip-link" href="#main">
            Skip to content
          </a>
          <header className="site-header">
            <span className="brand">ArcForges</span>
            <span className="profile-note">Production profile proof</span>
          </header>
          <main id="main" tabIndex={-1}>
            {children}
          </main>
          <footer className="site-footer">
            <span>Minimal proof profile. Not a product surface.</span>
          </footer>
        </div>
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}

export default function App() {
  return <Outlet />;
}

export function ErrorBoundary() {
  const error = useRouteError();
  const missing = isRouteErrorResponse(error) && error.status === 404;
  return (
    <section className="example">
      <h1>{missing ? "Page not found." : "Something went wrong."}</h1>
      <p className="hero-copy">
        {missing ? "This profile has a single page." : "Reload the page to try again."}
      </p>
    </section>
  );
}
