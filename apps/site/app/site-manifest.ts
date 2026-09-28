// SPDX-License-Identifier: AGPL-3.0-only
import { inputs } from "./site-inputs.ts";

export interface SiteLocale {
  code: string;
  label: string;
  pathPrefix: string;
}

interface SitePageBase {
  id: string;
  locale: string;
  defaultPath?: string;
  localizedPath: string;
  title: string;
  description: string;
}

export interface HomePage extends SitePageBase {
  kind: "home";
  eyebrow: string;
  headingFirst: string;
  headingSecond: string;
  lead: string[];
  primaryLink: { href: string; label: string };
  introTitle: string;
  introBody: string;
}

export interface ExamplePage extends SitePageBase {
  kind: "hello" | "cloud-hello";
}

export interface DocumentPage extends SitePageBase {
  kind: "document";
  body: string;
  version: string;
}

export type SitePage = HomePage | ExamplePage | DocumentPage;

export interface SiteRedirect {
  source: string;
  destination: string;
  status: 301 | 302 | 303 | 307 | 308;
}

export interface PublicSiteManifest {
  schemaVersion: 1;
  origin: string;
  defaultLocale: string;
  locales: readonly SiteLocale[];
  pages: readonly SitePage[];
  documentationVersions: readonly string[];
  redirects: readonly SiteRedirect[];
}

export const publicSiteManifest = inputs as unknown as PublicSiteManifest;

function ordinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

export function normalizePublicPath(path: string): string {
  if (!path.startsWith("/") || path.startsWith("//")) {
    throw new Error(`Public paths must be root-relative: ${path}`);
  }
  const hasControlCharacter = Array.from(path).some((character) => {
    const codePoint = character.codePointAt(0) ?? 0;
    return codePoint < 0x20 || codePoint === 0x7f;
  });
  if (/[?#%]/u.test(path) || path.includes("\\") || path.includes("//") || hasControlCharacter) {
    throw new Error(
      `Public paths contain a query, fragment, encoding, repeated slash or control: ${path}`,
    );
  }
  const segments = path.split("/").filter(Boolean);
  if (
    segments.some(
      (segment) =>
        segment === "." ||
        segment === ".." ||
        !/^[A-Za-z0-9._~-]+$/u.test(segment) ||
        segment.endsWith("."),
    )
  ) {
    throw new Error(`Public paths contain an unsafe segment: ${path}`);
  }
  const normalized = `/${segments.join("/")}`;
  return normalized === "/" ? normalized : `${normalized}/`;
}

export function validatePublicSiteManifest(
  manifest: PublicSiteManifest = publicSiteManifest,
): void {
  if (manifest.schemaVersion !== 1) throw new Error("Unsupported public site manifest version");
  if (manifest.origin !== "https://arcforges.com") throw new Error("Unexpected public site origin");

  const localeCodes = manifest.locales.map((locale) => locale.code);
  const localePrefixes = manifest.locales.map((locale) => locale.pathPrefix);
  if (new Set(localeCodes).size !== localeCodes.length) throw new Error("Duplicate site locale");
  if (new Set(localePrefixes).size !== localePrefixes.length) {
    throw new Error("Duplicate site locale path prefix");
  }
  if (!localeCodes.includes(manifest.defaultLocale)) throw new Error("Unknown default site locale");
  for (const locale of manifest.locales) {
    if (!/^[a-z]{2,3}(?:-[A-Z]{2})?$/u.test(locale.code)) {
      throw new Error(`Invalid locale code: ${locale.code}`);
    }
    if (!/^[a-z0-9-]+$/u.test(locale.pathPrefix)) {
      throw new Error(`Invalid locale path prefix: ${locale.pathPrefix}`);
    }
  }

  const pageLocales = new Set<string>();
  const paths = new Set<string>();
  for (const page of manifest.pages) {
    const identity = `${page.id}\u0000${page.locale}`;
    if (!/^[a-z0-9-]+$/u.test(page.id) || pageLocales.has(identity)) {
      throw new Error(`Invalid or duplicate public page/locale: ${page.id}/${page.locale}`);
    }
    pageLocales.add(identity);
    if (!localeCodes.includes(page.locale)) throw new Error(`Unknown page locale: ${page.locale}`);
    const locale = manifest.locales.find((candidate) => candidate.code === page.locale);
    const localizedPath = normalizePublicPath(page.localizedPath);
    if (
      !localizedPath.startsWith(`/${locale?.pathPrefix}/`) &&
      localizedPath !== `/${locale?.pathPrefix}`
    ) {
      throw new Error(`Localized path does not match locale prefix: ${page.localizedPath}`);
    }
    for (const candidate of [page.defaultPath, page.localizedPath].filter(
      (path): path is string => path !== undefined,
    )) {
      const path = normalizePublicPath(candidate);
      if (paths.has(path)) throw new Error(`Duplicate public route path: ${path}`);
      paths.add(path);
      const segments = path.split("/").filter(Boolean);
      const routeRoot = localeCodes.includes(segments[0] ?? "") ? segments[1] : segments[0];
      if (["account", "chat", "ops", "api"].includes(routeRoot ?? "")) {
        throw new Error(`Private application path cannot be a public site route: ${path}`);
      }
    }
    if (!page.title.trim() || !page.description.trim()) {
      throw new Error(`Public page metadata is required: ${page.id}`);
    }
    if (page.locale !== manifest.defaultLocale && page.defaultPath !== undefined) {
      throw new Error(`Only the default locale may own a default path: ${page.id}/${page.locale}`);
    }
    if (page.locale === manifest.defaultLocale && page.defaultPath === undefined) {
      throw new Error(`Default locale route is missing its compatibility path: ${page.id}`);
    }
    if (page.kind === "document" && !manifest.documentationVersions.includes(page.version)) {
      throw new Error(`Document page uses an unregistered version: ${page.version}`);
    }
  }

  for (const pageId of new Set(manifest.pages.map((page) => page.id))) {
    if (!pageLocales.has(`${pageId}\u0000${manifest.defaultLocale}`)) {
      throw new Error(`Public page is missing its default locale: ${pageId}`);
    }
  }

  for (const page of manifest.pages) {
    if (page.kind !== "home") continue;
    const target = normalizePublicPath(page.primaryLink.href);
    if (!paths.has(target)) {
      throw new Error(`Home page primary link must target a known public route: ${target}`);
    }
    if (!page.primaryLink.label.trim()) {
      throw new Error(`Home page primary link label is required: ${page.id}/${page.locale}`);
    }
  }

  const redirectSources = new Set<string>();
  const redirectTargets = new Map<string, string>();
  for (const redirect of manifest.redirects) {
    const source = normalizePublicPath(redirect.source);
    if (redirectSources.has(source)) throw new Error(`Duplicate redirect source: ${source}`);
    if (paths.has(source))
      throw new Error(`Redirect source collides with a public route: ${source}`);
    redirectSources.add(source);
    const target = new URL(redirect.destination, manifest.origin);
    if (target.protocol !== "https:")
      throw new Error(`Redirect must use HTTPS: ${redirect.destination}`);
    if (target.username || target.password || target.search || target.hash) {
      throw new Error("Redirect destination cannot contain credentials, a query or a fragment");
    }
    if (target.origin !== manifest.origin && target.origin !== "https://account.arcforges.com") {
      throw new Error(`Redirect uses an unapproved public origin: ${target.origin}`);
    }
    if (target.origin === manifest.origin) {
      redirectTargets.set(source, normalizePublicPath(target.pathname));
    }
    if (![301, 302, 303, 307, 308].includes(redirect.status)) {
      throw new Error(`Unsupported redirect status: ${redirect.status}`);
    }
  }

  for (const [source, firstTarget] of redirectTargets) {
    const visited = new Set<string>();
    let target = firstTarget;
    while (redirectTargets.has(target)) {
      if (visited.has(target) || target === source) throw new Error(`Redirect loop: ${source}`);
      visited.add(target);
      const next = redirectTargets.get(target);
      if (next === undefined) break;
      target = next;
    }
    if (!paths.has(target)) {
      throw new Error(
        `Redirect must resolve to a known public route or approved external origin: ${target}`,
      );
    }
  }

  const versions = manifest.documentationVersions;
  if (new Set(versions).size !== versions.length)
    throw new Error("Duplicate documentation version");
  if (versions.some((version) => !/^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/u.test(version))) {
    throw new Error("Invalid documentation version identifier");
  }
}

export function allPrerenderPaths(manifest: PublicSiteManifest = publicSiteManifest): string[] {
  validatePublicSiteManifest(manifest);
  const paths = manifest.pages.flatMap((page) => [page.defaultPath, page.localizedPath]);
  return [
    ...new Set(paths.filter((path): path is string => path !== undefined).map(normalizePublicPath)),
  ].sort(ordinal);
}

export function localeForPath(
  pathname: string,
  manifest: PublicSiteManifest = publicSiteManifest,
): string {
  const firstSegment = pathname.split("/").find((segment) => segment.length > 0);
  return (
    manifest.locales.find((locale) => locale.pathPrefix === firstSegment)?.code ??
    manifest.defaultLocale
  );
}

export function sitemapUrls(manifest: PublicSiteManifest = publicSiteManifest): string[] {
  validatePublicSiteManifest(manifest);
  return manifest.pages
    .map((page) => new URL(normalizePublicPath(page.localizedPath), manifest.origin).href)
    .sort(ordinal);
}

export function renderSitemapXml(manifest: PublicSiteManifest = publicSiteManifest): string {
  const urls = sitemapUrls(manifest);
  const entries = urls.map((url) => `  <url><loc>${escapeXml(url)}</loc></url>`).join("\n");
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${entries}\n</urlset>\n`;
}

export function renderRedirectRules(manifest: PublicSiteManifest = publicSiteManifest): string {
  validatePublicSiteManifest(manifest);
  return `${[...manifest.redirects]
    .sort((left, right) => ordinal(left.source, right.source))
    .map((redirect) => `${redirect.source} ${redirect.destination} ${redirect.status}`)
    .join("\n")}\n`;
}

export function publicManifestDocument(manifest: PublicSiteManifest = publicSiteManifest) {
  validatePublicSiteManifest(manifest);
  return {
    schemaVersion: manifest.schemaVersion,
    origin: manifest.origin,
    defaultLocale: manifest.defaultLocale,
    locales: [...manifest.locales].sort((left, right) => ordinal(left.code, right.code)),
    pages: [...manifest.pages]
      .map((page) => ({
        id: page.id,
        locale: page.locale,
        paths: [page.defaultPath, page.localizedPath]
          .filter((path): path is string => path !== undefined)
          .map(normalizePublicPath)
          .sort(ordinal),
        canonical: new URL(normalizePublicPath(page.localizedPath), manifest.origin).href,
        title: page.title,
        description: page.description,
      }))
      .sort((left, right) => ordinal(left.id, right.id) || ordinal(left.locale, right.locale)),
    documentationVersions: [...manifest.documentationVersions].sort(ordinal),
    redirects: [...manifest.redirects]
      .sort((left, right) => ordinal(left.source, right.source))
      .map((redirect) => ({ ...redirect })),
  };
}

export function pageById(id: "home"): HomePage;
export function pageById(id: "hello"): ExamplePage;
export function pageById(id: "cloud-hello"): ExamplePage;
export function pageById(id: string): SitePage;
export function pageById(id: string): SitePage {
  const page = publicSiteManifest.pages.find(
    (candidate) => candidate.id === id && candidate.locale === publicSiteManifest.defaultLocale,
  );
  if (!page) throw new Error(`Missing public site page: ${id}`);
  return page;
}

export function pageByLocalizedPath(
  path: string,
  manifest: PublicSiteManifest = publicSiteManifest,
): SitePage | undefined {
  const normalized = normalizePublicPath(path);
  return manifest.pages.find((page) => normalizePublicPath(page.localizedPath) === normalized);
}

function escapeXml(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}
