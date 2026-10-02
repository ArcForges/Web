// SPDX-License-Identifier: AGPL-3.0-only
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { afterEach, expect, test } from "vitest";
import { activeProfile, isProfile, profiles } from "../../apps/app/profile.ts";
import {
  type Budgets,
  checkBudgets,
  initialResources,
  measureProfile,
  toBaseline,
  verifyProfile,
} from "../../apps/app/scripts/measure.ts";

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

function build(
  profile: "account" | "chat",
  extra: Record<string, string> = {},
  drop: string[] = [],
) {
  const root = mkdtempSync(join(tmpdir(), "prf08-"));
  roots.push(root);
  const own = profile === "account" ? "/session/v1/bootstrap" : "application/grpc-web";
  const files: Record<string, string> = {
    "index.html": `<!DOCTYPE html><html><head><link rel="icon" href="/favicon.svg"/><link rel="modulepreload" href="/assets/entry.js"/><link rel="modulepreload" href="/assets/route.js"/><link rel="stylesheet" href="/assets/root.css"/></head><body><a href="https://example.test/outside">source</a><script src="/assets/entry.js" type="module"></script></body></html>`,
    "favicon.svg": "<svg/>",
    "assets/entry.js": `export const entry = "${"e".repeat(500)}";`,
    "assets/route.js": `export const route = "${own}${"r".repeat(800)}";`,
    "assets/root.css": `body{color:red}${"/*x*/".repeat(100)}`,
    "__spa-fallback.html": "<html></html>",
    ".vite/manifest.json": "{}",
    ...extra,
  };
  for (const name of drop) delete files[name];
  for (const [name, content] of Object.entries(files)) {
    mkdirSync(dirname(join(root, name)), { recursive: true });
    writeFileSync(join(root, name), content);
  }
  return root;
}

test("only the two named profiles exist and a build must name one", () => {
  expect([...profiles]).toEqual(["account", "chat"]);
  expect(isProfile("account")).toBe(true);
  expect(isProfile("chat")).toBe(true);
  for (const value of ["", "Account", "operator", undefined, 1, null])
    expect(isProfile(value)).toBe(false);
  expect(activeProfile({ ARCFORGES_PROFILE: "chat" })).toBe("chat");
  expect(() => activeProfile({})).toThrow(/ARCFORGES_PROFILE/);
  expect(() => activeProfile({ ARCFORGES_PROFILE: "site" })).toThrow(/ARCFORGES_PROFILE/);
});

test("initial resources are the preloaded modules, script sources and stylesheets, not anchors", () => {
  const found = initialResources(readFileSync(join(build("chat"), "index.html"), "utf8"));
  expect(found.js).toEqual(["/assets/entry.js", "/assets/route.js"]);
  expect(found.css).toEqual(["/assets/root.css"]);
  expect(found.references).not.toContain("https://example.test/outside");
  expect(found.references).toContain("/favicon.svg");
});

test("measurement counts only served files and requests, weighs gzip and fingerprints content", () => {
  const root = build("chat");
  const measured = measureProfile(root, "chat");
  expect(measured.files).toBe(5);
  expect(measured.initialRequests).toBe(4);
  expect(measured.initialJs.bytes).toBe(
    readFileSync(join(root, "assets/entry.js")).length +
      readFileSync(join(root, "assets/route.js")).length,
  );
  expect(measured.initialJs.gzip).toBeLessThan(measured.initialJs.bytes);
  expect(measured.initialJs.gzip).toBeGreaterThan(0);
  expect(measured.initialCss.bytes).toBe(readFileSync(join(root, "assets/root.css")).length);
  expect(measured.totalJs).toEqual(measured.initialJs);
  expect(measured.htmlBytes).toBe(readFileSync(join(root, "index.html")).length);
  const same = measureProfile(build("chat"), "chat");
  expect(same.digest).toBe(measured.digest);
  writeFileSync(
    join(root, "assets/route.js"),
    `export const route = "application/grpc-web${"r".repeat(799)}";`,
  );
  expect(measureProfile(root, "chat").digest).not.toBe(measured.digest);
  const renamed = build("chat", { "assets/extra.js": "export {};" });
  expect(measureProfile(renamed, "chat").digest).not.toBe(measured.digest);
  expect(measureProfile(renamed, "chat").totalJs.bytes).toBeGreaterThan(measured.totalJs.bytes);
  expect(measureProfile(renamed, "chat").initialJs).toEqual(measured.initialJs);
});

test("a build with a missing initial resource, no entry page or a foreign reference is refused", () => {
  expect(() => measureProfile(build("chat", {}, ["assets/route.js"]), "chat")).toThrow(
    /missing from the build/,
  );
  expect(() => measureProfile(build("chat", {}, ["index.html"]), "chat")).toThrow(
    /no prerendered index/,
  );
  const absolute = build("chat", {
    "index.html":
      '<html><head><link rel="modulepreload" href="https://cdn.example.test/a.js"/></head></html>',
  });
  expect(() => measureProfile(absolute, "chat")).toThrow(/Not same-origin/);
  const protocolRelative = build("chat", {
    "index.html": '<html><head><script src="//cdn.example.test/a.js"></script></head></html>',
  });
  expect(() => measureProfile(protocolRelative, "chat")).toThrow(/Not same-origin/);
});

test("the structural gates pass a clean profile and name every violation otherwise", () => {
  expect(verifyProfile(build("account"), "account")).toEqual([]);
  expect(verifyProfile(build("chat"), "chat")).toEqual([]);
  expect(verifyProfile(build("chat"), "operator")).toEqual(["Unknown profile operator"]);
  expect(verifyProfile(build("chat", { "assets/route.js.map": "{}" }), "chat")).toEqual([
    "Source map shipped: assets/route.js.map",
  ]);
  expect(verifyProfile(build("chat", { "node_modules/x.js": "" }), "chat")).toEqual([
    "Private file shipped: node_modules/x.js",
  ]);
  expect(verifyProfile(build("chat", { ".env": "A=1" }), "chat")).toEqual([
    "Private file shipped: .env",
  ]);
  expect(verifyProfile(build("chat", {}, ["index.html"]), "chat")).toEqual([
    "No prerendered index.html",
  ]);
  expect(
    verifyProfile(
      build("chat", {
        "index.html":
          '<html><head><link rel="modulepreload" href="assets/route.js"/></head></html>',
      }),
      "chat",
    ),
  ).toEqual(["Reference is not same-origin: assets/route.js"]);
  expect(
    verifyProfile(
      build("chat", {
        "index.html": '<html><head><script src="//cdn.example.test/a.js"></script></head></html>',
      }),
      "chat",
    ),
  ).toEqual(["Reference is not same-origin: //cdn.example.test/a.js"]);
});

test("one profile's wire code must not reach the other profile", () => {
  const crossed = build("chat", { "assets/extra.js": 'export const p = "/session/v1/bootstrap";' });
  expect(verifyProfile(crossed, "chat")).toEqual([
    "Profile chat contains the other profile's wire marker",
  ]);
  const crossedAccount = build("account", {
    "assets/extra.js": 'export const p = "application/grpc-web";',
  });
  expect(verifyProfile(crossedAccount, "account")).toEqual([
    "Profile account contains the other profile's wire marker",
  ]);
  expect(verifyProfile(build("chat", { "assets/route.js": "export {};" }), "chat")).toEqual([
    "Profile chat lost its own wire marker",
  ]);
  expect(verifyProfile(build("account", { "assets/route.js": "export {};" }), "account")).toEqual([
    "Profile account lost its own wire marker",
  ]);
});

const budgets = (): Budgets => ({
  schema: 1,
  regressionPercent: 10,
  profiles: {
    chat: {
      initialRequests: 10,
      initialJsGzip: 1000,
      initialCssGzip: 200,
      totalGzip: 1500,
      htmlBytes: 3000,
    },
  },
});

test("the regression budget allows exactly ten percent growth per metric and catches a deliberate regression", () => {
  const measured = measureProfile(build("chat"), "chat");
  const actual = toBaseline(measured);
  expect(Object.keys(actual).sort()).toEqual(
    ["htmlBytes", "initialCssGzip", "initialJsGzip", "initialRequests", "totalGzip"].sort(),
  );
  const exact: Budgets = { schema: 1, regressionPercent: 10, profiles: { chat: { ...actual } } };
  expect(checkBudgets(measured, exact)).toEqual([]);
  // A baseline whose ten percent ceiling equals the measurement passes; one byte lower fails.
  for (const key of Object.keys(actual) as (keyof typeof actual)[]) {
    const value = actual[key];
    const baseline = Math.ceil((value * 100) / 110);
    const at: Budgets = {
      schema: 1,
      regressionPercent: 10,
      profiles: { chat: { ...actual, [key]: baseline } },
    };
    expect(Math.floor((baseline * 110) / 100)).toBeGreaterThanOrEqual(value);
    expect(checkBudgets(measured, at), key).toEqual([]);
    const below: Budgets = {
      schema: 1,
      regressionPercent: 10,
      profiles: { chat: { ...actual, [key]: Math.floor((value * 100) / 110) - 1 } },
    };
    const problems = checkBudgets(measured, below);
    expect(problems, key).toHaveLength(1);
    expect(problems[0]).toContain(key);
  }
  const regressed = build("chat", {
    "assets/entry.js": `export const entry = "${Array.from({ length: 4000 }, (_, i) => i * 7919).join(",")}";`,
  });
  expect(
    checkBudgets(measureProfile(regressed, "chat"), {
      ...budgets(),
      profiles: { chat: { ...actual } },
    }).length,
  ).toBeGreaterThan(0);
});

test("a budget needs a baseline for the profile it judges", () => {
  expect(checkBudgets(measureProfile(build("account"), "account"), budgets())).toEqual([
    "No baseline for profile account",
  ]);
});

test("the committed baseline covers both profiles with positive measured values", () => {
  const committed = JSON.parse(
    readFileSync(new URL("../../apps/app/budgets.json", import.meta.url), "utf8"),
  ) as Budgets;
  expect(committed.schema).toBe(1);
  expect(committed.regressionPercent).toBe(10);
  expect(Object.keys(committed.profiles).sort()).toEqual([...profiles]);
  for (const baseline of Object.values(committed.profiles))
    for (const value of Object.values(baseline)) {
      expect(Number.isInteger(value)).toBe(true);
      expect(value).toBeGreaterThan(0);
    }
});
