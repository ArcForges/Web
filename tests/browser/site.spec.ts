// SPDX-License-Identifier: AGPL-3.0-only
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
test("production pages hydrate under CSP and support keyboard greeting without network requests", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(message.text());
  });
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Hello,world.");
  await page.getByRole("link", { name: "Make it your hello" }).click();
  await expect(page.getByRole("button", { name: "Say hello" })).toBeEnabled();
  const requests: string[] = [];
  page.on("request", (request) => requests.push(request.url()));
  const input = page.getByLabel("Your name");
  await input.fill("世界 🌍");
  await input.press("Enter");
  await expect(page.getByRole("status")).toContainText("Hello, 世界 🌍!");
  await input.fill(" ");
  await input.press("Enter");
  await expect(page.getByRole("alert")).toBeVisible();
  await input.fill("<script>alert(1)</script>");
  await input.press("Enter");
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByRole("status")).toContainText("<script>alert(1)</script>");
  expect(requests).toEqual([]);
  expect(errors).toEqual([]);
});
test("public content and navigation work without JavaScript", async ({ browser, baseURL }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto(`${baseURL}/`);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.getByRole("link", { name: "Make it your hello" }).click();
  await expect(page.getByRole("status")).toContainText("Hello, World!");
  await expect(page.getByLabel("Your name")).toBeDisabled();
  await expect(page.getByRole("button", { name: "Say hello" })).toBeDisabled();
  await page.getByRole("link", { name: "Check the server connection" }).click();
  await expect(page.getByRole("button", { name: "Check connection" })).toBeDisabled();
  await context.close();
});
test("pages are accessible and fit narrow screens", async ({ page }, info) => {
  for (const route of ["/", "/hello/", "/cloud-hello/"]) {
    const pageName = route === "/" ? "home" : route.split("/")[1];
    await page.goto(route);
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
      .analyze();
    expect(results.violations).toEqual([]);
    await page.setViewportSize({ width: 360, height: 800 });
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    ).toBe(true);
    await page.screenshot({
      path: info.outputPath(`${pageName}-mobile.png`),
      fullPage: true,
    });
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.screenshot({
      path: info.outputPath(`${pageName}-desktop.png`),
      fullPage: true,
    });
  }
});
test("server connection page makes no automatic API calls", async ({ page }) => {
  const requests: string[] = [];
  const errors: string[] = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.startsWith("/api/")) requests.push(request.url());
  });
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto("/cloud-hello/");
  await expect(page.getByRole("heading", { level: 1 })).toContainText("from the server");
  await expect(page.getByRole("button", { name: "Check connection" })).toBeEnabled();
  await expect(page.getByRole("status")).toContainText("No request sent yet");
  expect(requests).toEqual([]);
  expect(errors).toEqual([]);
});
test("static delivery has security/cache headers and real missing-page and asset responses", async ({
  request,
}) => {
  const home = await request.get("/");
  expect(home.status()).toBe(200);
  expect(home.headers()["content-security-policy"]).toContain("script-src 'self' 'sha256-");
  expect(home.headers()["content-security-policy"]).not.toMatch(/unsafe-inline|unsafe-eval/);
  expect(home.headers()["x-content-type-options"]).toBe("nosniff");
  expect(home.headers()["cache-control"]).toContain("no-transform");
  const html = await home.text();
  const asset = html.match(/href="(\/assets\/[^"]+\.css)"/)?.[1];
  expect(asset).toBeDefined();
  expect((await request.get(asset ?? "")).headers()["cache-control"]).toContain("immutable");
  expect((await request.get("/__build.json")).headers()["cache-control"]).toContain("no-store");
  for (const path of ["/no-such-page", "/assets/missing.js", "/__spa-fallback.html"]) {
    const missing = await request.get(path);
    expect(missing.status()).toBe(404);
    expect(await missing.text()).toContain("Page not found");
  }
});
