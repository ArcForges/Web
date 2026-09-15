// SPDX-License-Identifier: AGPL-3.0-only
import { defineConfig } from "@playwright/test";
import candidateConfig from "./playwright.config.ts";
import { deploymentUrl } from "./tooling/cloudflare.ts";

export default defineConfig({
  ...candidateConfig,
  webServer: [],
  testIgnore: ["**/cloud-hello-fixture.spec.ts"],
  outputDir: "test-results/live",
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-live-report" }]],
  use: { ...candidateConfig.use, baseURL: deploymentUrl },
});
