// SPDX-License-Identifier: AGPL-3.0-only
import "@arcforges/web-ui/styles.css";
import { Shell } from "@arcforges/web-ui";
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
        <Shell>{children}</Shell>
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
      <a className="back-link" href="/">
        ← Back home
      </a>
      <h1>{missing ? "Page not found." : "Something went wrong."}</h1>
      <p className="hero-copy">
        {missing ? "This page is not part of the preview." : "Reload the page to try again."}
      </p>
    </section>
  );
}
