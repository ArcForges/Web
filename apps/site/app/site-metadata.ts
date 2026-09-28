// SPDX-License-Identifier: AGPL-3.0-only
import type { MetaDescriptor } from "react-router";

export interface SitePageMetadata {
  title: string;
  description: string;
  canonicalUrl: string;
  alternateLanguages: readonly { locale: string; href: string }[];
}

export function metadataForPage(page: SitePageMetadata) {
  return [
    { title: page.title },
    { name: "description", content: page.description },
    { property: "og:title", content: page.title },
    { property: "og:description", content: page.description },
    { property: "og:type", content: "website" },
    { property: "og:url", content: page.canonicalUrl },
    { tagName: "link", rel: "canonical", href: page.canonicalUrl },
    ...page.alternateLanguages.map((alternate) => ({
      tagName: "link" as const,
      rel: "alternate",
      hrefLang: alternate.locale,
      href: alternate.href,
    })),
  ] satisfies MetaDescriptor[];
}
