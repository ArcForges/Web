// SPDX-License-Identifier: AGPL-3.0-only
import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";
import { expectedIdentity } from "../../tooling/build-identity.ts";

const manifest = JSON.parse(
  readFileSync(new URL("../../artifacts/candidate/manifest.json", import.meta.url), "utf8"),
);

test("the actual browser reads the sealed source/run and nine-axis report", async ({ page }) => {
  await page.goto("/");
  const observed = await page.evaluate(async () => {
    const response = await fetch("/__build-info.json", { cache: "no-store" });
    if (!response.ok) throw new Error(`Build identity returned ${response.status}`);
    return { identity: await response.json(), cache: response.headers.get("cache-control") };
  });
  expect(observed.identity).toEqual(expectedIdentity(manifest.version));
  expect(observed.cache).toContain("no-store");
  expect(observed.cache).toContain("no-transform");
});
