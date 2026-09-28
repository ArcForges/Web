// SPDX-License-Identifier: AGPL-3.0-only
import {
  normalizePublicPath,
  pageById,
  pageByLocalizedPath,
  publicSiteManifest,
  type PublicSiteManifest,
  type SitePage,
} from "./site-manifest";

export type SitePageData = SitePage & {
  canonicalUrl: string;
  alternateLanguages: readonly { locale: string; href: string }[];
};

type PageMetadata = Pick<SitePageData, "canonicalUrl" | "alternateLanguages">;

function withMetadata<T extends SitePage>(
  page: T,
  manifest: PublicSiteManifest = publicSiteManifest,
): T & PageMetadata {
  return {
    ...page,
    canonicalUrl: new URL(page.localizedPath, manifest.origin).href,
    alternateLanguages: manifest.pages
      .filter((candidate) => candidate.id === page.id)
      .sort((left, right) => (left.locale < right.locale ? -1 : left.locale > right.locale ? 1 : 0))
      .map((candidate) => ({
        locale: candidate.locale,
        href: new URL(candidate.localizedPath, manifest.origin).href,
      })),
  };
}

export function loadHomePage() {
  return withMetadata(pageById("home"));
}

export function loadHelloPage() {
  return withMetadata(pageById("hello"));
}

export function loadCloudHelloPage() {
  return withMetadata(pageById("cloud-hello"));
}

export function loadLocalizedPage(
  params: Record<string, string | undefined>,
  manifest: PublicSiteManifest = publicSiteManifest,
): SitePageData {
  const locale = params.locale;
  if (locale && !/^[a-z]{2,3}(?:-[A-Z]{2})?$/u.test(locale)) {
    throw new Response("Page not found", { status: 404 });
  }
  const wildcard = params["*"] ?? "";
  const pathSegments = wildcard.split("/").filter(Boolean);
  const inferredLocale =
    locale ?? manifest.locales.find((candidate) => candidate.pathPrefix === pathSegments[0])?.code;
  let path: string;
  try {
    const defaultPath = wildcard.startsWith("docs/") ? `/${wildcard}` : `/docs/${wildcard}`;
    path = normalizePublicPath(
      locale ? `/${locale}/${wildcard}` : inferredLocale ? `/${wildcard}` : defaultPath,
    );
  } catch {
    throw new Response("Page not found", { status: 404 });
  }
  const page = inferredLocale
    ? pageByLocalizedPath(path, manifest)
    : manifest.pages.find((candidate) => candidate.defaultPath === path);
  if (!page || (inferredLocale && page.locale !== inferredLocale)) {
    throw new Response("Page not found", { status: 404 });
  }
  return withMetadata(page, manifest);
}
