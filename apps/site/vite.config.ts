// SPDX-License-Identifier: AGPL-3.0-only
import { reactRouter } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig, type Plugin } from "vite";
import { observeBrowser } from "../../tooling/browser-provenance.ts";
import {
  publicManifestDocument,
  renderRedirectRules,
  renderSitemapXml,
  validatePublicSiteManifest,
} from "./app/site-manifest.ts";

function staticSiteArtifacts(): Plugin {
  return {
    name: "arcforges-static-site-artifacts",
    apply: "build",
    buildStart() {
      validatePublicSiteManifest();
    },
    generateBundle() {
      this.emitFile({
        type: "asset",
        fileName: "site-manifest.json",
        source: `${JSON.stringify(publicManifestDocument(), null, 2)}\n`,
      });
      this.emitFile({ type: "asset", fileName: "sitemap.xml", source: renderSitemapXml() });
      this.emitFile({ type: "asset", fileName: "_redirects", source: renderRedirectRules() });
    },
  };
}

export default defineConfig({
  plugins: [tailwindcss(), reactRouter(), observeBrowser(), staticSiteArtifacts()],
  build: { sourcemap: false },
});
