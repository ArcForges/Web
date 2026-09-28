// SPDX-License-Identifier: AGPL-3.0-only
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, test } from "vitest";
import {
  allPrerenderPaths,
  localeForPath,
  pageById,
  publicManifestDocument,
  publicSiteManifest,
  renderRedirectRules,
  renderSitemapXml,
  sitemapUrls,
  validatePublicSiteManifest,
  type DocumentPage,
  type ExamplePage,
  type HomePage,
  type PublicSiteManifest,
  type SitePage,
} from "../app/site-manifest";
import { loadLocalizedPage } from "../app/site-content.server";
import { metadataForPage } from "../app/site-metadata";
import routeConfig from "../app/routes";

const siteRoot = fileURLToPath(new URL("..", import.meta.url));
const clientRoot = join(siteRoot, "build", "client");

function testOnlyVersionedManifest(): PublicSiteManifest {
  const home: HomePage = {
    id: "home",
    kind: "home",
    locale: "fr",
    localizedPath: "/fr/",
    title: "Bonjour, monde. — ArcForges",
    description: "Fixture only: translated public home page.",
    eyebrow: "ArcForges Web · Test fixture",
    headingFirst: "Bonjour,",
    headingSecond: "monde.",
    lead: ["Fixture locale content."],
    primaryLink: { href: "/fr/hello/", label: "Test link" },
    introTitle: "Fixture locale",
    introBody: "This translation exists only in a WEB.01 unit fixture.",
  };
  const documents: DocumentPage[] = ["v1", "v2"].map((version) => ({
    id: `docs-arcscope-${version}`,
    kind: "document",
    locale: "en",
    defaultPath: `/docs/arcscope/${version}/`,
    localizedPath: `/en/docs/arcscope/${version}/`,
    title: `ArcScope docs ${version}`,
    description: `Fixture-only ArcScope documentation version ${version}.`,
    body: `Fixture body for version ${version}.`,
    version,
  }));
  const frenchHello: ExamplePage = {
    id: "hello",
    kind: "hello",
    locale: "fr",
    localizedPath: "/fr/hello/",
    title: "Bonjour — ArcForges",
    description: "Fixture-only localized hello page.",
  };
  const base = publicSiteManifest as PublicSiteManifest;
  return {
    ...base,
    locales: [...base.locales, { code: "fr", label: "Français", pathPrefix: "fr" }],
    pages: [...base.pages, home, frenchHello, ...documents] as SitePage[],
    documentationVersions: ["v1", "v2"],
    redirects: [
      ...base.redirects,
      { source: "/old-docs", destination: "/en/docs/arcscope/v2/", status: 308 },
    ],
  };
}

describe("public static-site inventory", () => {
  test("registers each route module once so route identifiers are unique", () => {
    const files = routeConfig.flatMap((route) => ("file" in route ? [route.file] : []));
    expect(new Set(files).size).toBe(files.length);
  });

  test("pre-renders every default and locale-scoped public route in stable order", () => {
    expect(allPrerenderPaths()).toEqual([
      "/",
      "/cloud-hello/",
      "/en/",
      "/en/cloud-hello/",
      "/en/hello/",
      "/hello/",
    ]);
    expect(allPrerenderPaths()).toEqual([...allPrerenderPaths()].sort());
  });

  test("publishes no private Account, Chat, Operations, API or runtime-config route", () => {
    const document = publicManifestDocument();
    const routePaths = document.pages.flatMap((page) => page.paths);
    expect(routePaths.some((path) => /^\/(?:account|chat|ops|api)(?:\/|$)/u.test(path))).toBe(
      false,
    );
    expect(JSON.stringify(document)).not.toMatch(
      /(?:clientSecret|apiKey|privatePrice|accountRoute|chatRoute)/iu,
    );
    expect(publicSiteManifest.redirects).toContainEqual({
      source: "/account",
      destination: "https://account.arcforges.com/",
      status: 308,
    });
  });

  test("generates deterministic canonical metadata, language alternates, sitemap and redirects", () => {
    const home = pageById("home");
    const homeMetadata = {
      ...home,
      canonicalUrl: "https://arcforges.com/en/",
      alternateLanguages: [{ locale: "en", href: "https://arcforges.com/en/" }],
    };
    expect(metadataForPage(homeMetadata)).toContainEqual({ title: "Hello, world. — ArcForges" });
    expect(metadataForPage(homeMetadata)).toContainEqual({
      tagName: "link",
      rel: "canonical",
      href: "https://arcforges.com/en/",
    });
    expect(metadataForPage(homeMetadata)).toContainEqual({
      tagName: "link",
      rel: "alternate",
      hrefLang: "en",
      href: "https://arcforges.com/en/",
    });
    expect(renderSitemapXml()).toContain("https://arcforges.com/en/");
    expect(renderSitemapXml()).not.toContain("/account");
    expect(renderRedirectRules()).toBe("/account https://account.arcforges.com/ 308\n");
  });

  test("a named test-only fixture covers multiple locales and documentation versions", () => {
    const fixture = testOnlyVersionedManifest();
    validatePublicSiteManifest(fixture);
    expect(allPrerenderPaths(fixture)).toContain("/fr/");
    expect(allPrerenderPaths(fixture)).toContain("/fr/hello/");
    expect(allPrerenderPaths(fixture)).toContain("/docs/arcscope/v1/");
    expect(allPrerenderPaths(fixture)).toContain("/en/docs/arcscope/v2/");
    expect(publicManifestDocument(fixture).documentationVersions).toEqual(["v1", "v2"]);
    expect(sitemapUrls(fixture)).toContain("https://arcforges.com/fr/");
    expect(sitemapUrls(fixture)).toContain("https://arcforges.com/en/docs/arcscope/v2/");
    expect(renderRedirectRules(fixture)).toContain("/old-docs /en/docs/arcscope/v2/ 308");
    expect(loadLocalizedPage({ locale: undefined, "*": "arcscope/v2/" }, fixture).kind).toBe(
      "document",
    );
    expect(loadLocalizedPage({ locale: undefined, "*": "docs/arcscope/v2/" }, fixture).kind).toBe(
      "document",
    );
    expect(loadLocalizedPage({ locale: undefined, "*": "fr/hello/" }, fixture).locale).toBe("fr");
    expect(loadLocalizedPage({ locale: "fr", "*": "hello/" }, fixture).locale).toBe("fr");
    expect(localeForPath("/fr/hello/", fixture)).toBe("fr");
    expect(localeForPath("/hello/", fixture)).toBe("en");
    const shuffled = {
      ...fixture,
      locales: [...fixture.locales].reverse(),
      pages: [...fixture.pages].reverse(),
      redirects: [...fixture.redirects].reverse(),
    };
    expect(allPrerenderPaths(shuffled)).toEqual(allPrerenderPaths(fixture));
    expect(publicManifestDocument(shuffled)).toEqual(publicManifestDocument(fixture));
    expect(renderSitemapXml(shuffled)).toBe(renderSitemapXml(fixture));
    expect(renderRedirectRules(shuffled)).toBe(renderRedirectRules(fixture));

    const homeAlternates = fixture.pages
      .filter((page) => page.id === "home")
      .map((page) => ({
        locale: page.locale,
        href: new URL(page.localizedPath, fixture.origin).href,
      }));
    const frenchHome = fixture.pages.find((page) => page.id === "home" && page.locale === "fr");
    if (!frenchHome) throw new Error("The test-only locale fixture is missing its French home");
    const frenchMetadata = metadataForPage({
      title: frenchHome.title,
      description: frenchHome.description,
      canonicalUrl: new URL(frenchHome.localizedPath, fixture.origin).href,
      alternateLanguages: homeAlternates,
    });
    expect(frenchMetadata).toContainEqual({
      tagName: "link",
      rel: "canonical",
      href: "https://arcforges.com/fr/",
    });
    expect(frenchMetadata).toContainEqual({
      tagName: "link",
      rel: "alternate",
      hrefLang: "en",
      href: "https://arcforges.com/en/",
    });
    expect(frenchMetadata).toContainEqual({
      tagName: "link",
      rel: "alternate",
      hrefLang: "fr",
      href: "https://arcforges.com/fr/",
    });
  });

  test("rejects route collisions, private route additions and redirect traps", () => {
    const base = publicSiteManifest as PublicSiteManifest;
    const englishHome = pageById("home");
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        pages: [...base.pages, { ...englishHome, localizedPath: "/en/" }],
      }),
    ).toThrow(/Invalid or duplicate public page\/locale/u);
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        pages: [
          ...base.pages,
          {
            id: "private",
            kind: "hello",
            locale: "en",
            localizedPath: "/en/account/",
            title: "Private route",
            description: "This private route must not enter the public site.",
          },
        ],
      }),
    ).toThrow(/Private application path/u);
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        redirects: [{ source: "/loop/", destination: "/loop/", status: 308 }],
      }),
    ).toThrow(/Redirect loop/u);
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        redirects: [
          { source: "/old-a/", destination: "/old-b/", status: 308 },
          { source: "/old-b/", destination: "/old-a/", status: 308 },
        ],
      }),
    ).toThrow(/Redirect loop/u);
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        redirects: [{ source: "/hello/", destination: "/en/hello/", status: 308 }],
      }),
    ).toThrow(/collides with a public route/u);
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        pages: base.pages.map((page) =>
          page.id === "home"
            ? { ...page, primaryLink: { href: "/account/", label: "Account" } }
            : page,
        ),
      }),
    ).toThrow(/known public route/u);
    expect(() =>
      validatePublicSiteManifest({
        ...base,
        redirects: [
          { source: "/old/", destination: "https://arcforges.com/account/", status: 308 },
        ],
      }),
    ).toThrow(/known public route/u);
  });

  test("a one-page metadata edit stays local to that page's public inventory", () => {
    const original = publicSiteManifest as PublicSiteManifest;
    const changed: PublicSiteManifest = {
      ...original,
      pages: original.pages.map((page) =>
        page.id === "home" ? { ...page, description: `${page.description} Revised.` } : page,
      ),
    };
    const before = publicManifestDocument(original);
    const after = publicManifestDocument(changed);
    expect(after.pages.find((page) => page.id === "home")?.description).toBe(
      `${before.pages.find((page) => page.id === "home")?.description} Revised.`,
    );
    expect(after.pages.filter((page) => page.id !== "home")).toEqual(
      before.pages.filter((page) => page.id !== "home"),
    );
    expect(renderSitemapXml(changed)).toBe(renderSitemapXml(original));
    expect(renderRedirectRules(changed)).toBe(renderRedirectRules(original));
  });

  test("the built HTML is complete before scripts and generated public artifacts match the inventory", async () => {
    const home = await readFile(join(clientRoot, "index.html"), "utf8");
    const localizedHome = await readFile(join(clientRoot, "en", "index.html"), "utf8");
    const notFound = await readFile(join(clientRoot, "404.html"), "utf8");
    const emittedManifest = await readFile(join(clientRoot, "site-manifest.json"), "utf8");
    const emittedSitemap = await readFile(join(clientRoot, "sitemap.xml"), "utf8");
    const emittedRedirects = await readFile(join(clientRoot, "_redirects"), "utf8");

    for (const html of [home, localizedHome]) {
      expect(html).toContain("A beginning you can explore.");
      expect(html).toContain("Make it your hello");
      expect(html).toContain('rel="canonical"');
      expect(html).toMatch(/hreflang="en"/iu);
      expect(html).toContain('name="robots" content="noindex, nofollow"');
    }
    const helloAnchor = '<a class="button" href="/hello/">Make it your hello';
    const helloAnchorIndex = home.indexOf(helloAnchor);
    const firstScriptIndex = home.indexOf("<script");
    expect(helloAnchorIndex).toBeGreaterThanOrEqual(0);
    expect(firstScriptIndex).toBeGreaterThanOrEqual(0);
    expect(helloAnchorIndex).toBeLessThan(firstScriptIndex);
    expect(notFound).toContain("Page not found.");
    expect(JSON.parse(emittedManifest)).toEqual(publicManifestDocument());
    expect(emittedSitemap).toBe(renderSitemapXml());
    expect(emittedRedirects).toBe(renderRedirectRules());
  });
});
