// SPDX-License-Identifier: AGPL-3.0-only
export const inputs = {
  schemaVersion: 1,
  origin: "https://arcforges.com",
  defaultLocale: "en",
  locales: [{ code: "en", label: "English", pathPrefix: "en" }],
  pages: [
    {
      id: "home",
      kind: "home",
      locale: "en",
      defaultPath: "/",
      localizedPath: "/en/",
      title: "Hello, world. — ArcForges",
      description: "A small, open-source beginning for ArcForges Web.",
      headingFirst: "Hello,",
      headingSecond: "world.",
      eyebrow: "ArcForges Web · Early preview",
      lead: ["Every good thing starts somewhere.", "Welcome to the first page of ArcForges Web."],
      primaryLink: { href: "/hello/", label: "Make it your hello" },
      introTitle: "A beginning you can explore.",
      introBody:
        "This small preview has one job: say hello. Try the example, look around the source, and follow along as ArcForges takes shape.",
    },
    {
      id: "hello",
      kind: "hello",
      locale: "en",
      defaultPath: "/hello/",
      localizedPath: "/en/hello/",
      title: "Your hello — ArcForges",
      description: "A local greeting example that runs in your browser.",
    },
    {
      id: "cloud-hello",
      kind: "cloud-hello",
      locale: "en",
      defaultPath: "/cloud-hello/",
      localizedPath: "/en/cloud-hello/",
      title: "Server connection — ArcForges",
      description: "An optional example that checks the ArcForges server connection.",
    },
  ],
  documentationVersions: [],
  redirects: [
    {
      source: "/account",
      destination: "https://account.arcforges.com/",
      status: 308,
    },
  ],
} as const;
