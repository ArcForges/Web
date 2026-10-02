// SPDX-License-Identifier: AGPL-3.0-only
// Measurement and gates for the production profile builds. Pure over a built `client` directory, so the
// tests run them on fixtures; nothing here starts a browser or contacts a network.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readdirSync, readFileSync } from "node:fs";
import { join, posix } from "node:path";
import { gzipSync } from "node:zlib";
import { type DefaultTreeAdapterTypes, parse } from "parse5";

export interface Weight {
  bytes: number;
  gzip: number;
}
export interface Measurement {
  profile: string;
  files: number;
  initialRequests: number;
  initialJs: Weight;
  initialCss: Weight;
  totalJs: Weight;
  totalCss: Weight;
  htmlBytes: number;
  /** SHA-256 of every file name and content hash, so two builds compare in one value. */
  digest: string;
}
export interface Baseline {
  initialRequests: number;
  initialJsGzip: number;
  initialCssGzip: number;
  totalGzip: number;
  htmlBytes: number;
}
export interface Budgets {
  schema: 1;
  regressionPercent: number;
  profiles: Record<string, Baseline>;
}

const sha256 = (value: Uint8Array) => createHash("sha256").update(value).digest("hex");
const weigh = (bytes: Uint8Array): Weight => ({
  bytes: bytes.byteLength,
  gzip: gzipSync(bytes, { level: 9 }).byteLength,
});
const add = (a: Weight, b: Weight): Weight => ({
  bytes: a.bytes + b.bytes,
  gzip: a.gzip + b.gzip,
});
const empty = (): Weight => ({ bytes: 0, gzip: 0 });

/** Build-tool files that are not part of what is served. */
const notServed = (file: string) => file.startsWith(".vite/") || file === "__spa-fallback.html";

export function listFiles(directory: string, prefix = ""): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(join(directory, prefix), { withFileTypes: true })) {
    assert(!entry.isSymbolicLink(), `Symlink in a profile build: ${entry.name}`);
    const file = `${prefix}${entry.name}`;
    if (entry.isDirectory()) found.push(...listFiles(directory, `${file}/`));
    else found.push(file);
  }
  return found.sort();
}

/** The resources the entry page needs before it is interactive, in document order. */
export function initialResources(html: string): {
  js: string[];
  css: string[];
  references: string[];
} {
  const js: string[] = [];
  const css: string[] = [];
  const references: string[] = [];
  const visit = (node: DefaultTreeAdapterTypes.Node) => {
    if ("tagName" in node) {
      const attribute = (name: string) => node.attrs.find((item) => item.name === name)?.value;
      const href = attribute("href");
      const src = attribute("src");
      for (const reference of [href, src])
        if (reference !== undefined && node.tagName !== "a") references.push(reference);
      if (node.tagName === "link" && attribute("rel") === "modulepreload" && href) js.push(href);
      if (node.tagName === "link" && attribute("rel") === "stylesheet" && href) css.push(href);
      if (node.tagName === "script" && src && !js.includes(src)) js.push(src);
    }
    if ("childNodes" in node) for (const child of node.childNodes) visit(child);
  };
  visit(parse(html));
  return { js, css, references };
}

export function measureProfile(client: string, profile: string): Measurement {
  const all = listFiles(client).filter((file) => !notServed(file));
  const bytes = new Map(all.map((file) => [file, readFileSync(join(client, file))]));
  const index = bytes.get("index.html");
  assert(index, `Profile ${profile} has no prerendered index.html`);
  const initial = initialResources(new TextDecoder("utf-8", { fatal: true }).decode(index));
  const resolve = (reference: string) => {
    assert(
      reference.startsWith("/") && !reference.startsWith("//"),
      `Not same-origin: ${reference}`,
    );
    const file = posix.normalize(reference.slice(1));
    assert(bytes.has(file), `Initial resource is missing from the build: ${reference}`);
    return bytes.get(file) as Uint8Array;
  };
  let initialJs = empty();
  let initialCss = empty();
  for (const reference of initial.js) initialJs = add(initialJs, weigh(resolve(reference)));
  for (const reference of initial.css) initialCss = add(initialCss, weigh(resolve(reference)));
  let totalJs = empty();
  let totalCss = empty();
  for (const [file, content] of bytes) {
    if (file.endsWith(".js")) totalJs = add(totalJs, weigh(content));
    if (file.endsWith(".css")) totalCss = add(totalCss, weigh(content));
  }
  const digest = sha256(
    new TextEncoder().encode(
      all.map((file) => `${file}\n${sha256(bytes.get(file) as Uint8Array)}\n`).join(""),
    ),
  );
  return {
    profile,
    files: all.length,
    initialRequests: new Set([...initial.js, ...initial.css]).size + 1,
    initialJs,
    initialCss,
    totalJs,
    totalCss,
    htmlBytes: index.byteLength,
    digest,
  };
}

/** Wire markers that must stay inside the profile that owns them. */
const owned: Record<string, { present: string; absent: string }> = {
  account: { present: "/session/v1/bootstrap", absent: "application/grpc-web" },
  chat: { present: "application/grpc-web", absent: "/session/v1/bootstrap" },
};

/** Structural gates; returns every violation so a failure is complete. */
export function verifyProfile(client: string, profile: string): string[] {
  const problems: string[] = [];
  const all = listFiles(client).filter((file) => !notServed(file));
  const marker = owned[profile];
  if (!marker) return [`Unknown profile ${profile}`];
  for (const file of all) {
    if (file.endsWith(".map")) problems.push(`Source map shipped: ${file}`);
    if (/(?:^|\/)(?:\.env|\.dev\.vars|node_modules)(?:\/|$)/u.test(file))
      problems.push(`Private file shipped: ${file}`);
  }
  const html = all.includes("index.html") ? readFileSync(join(client, "index.html"), "utf8") : "";
  if (!html) problems.push("No prerendered index.html");
  else
    for (const reference of initialResources(html).references)
      if (!reference.startsWith("/") || reference.startsWith("//"))
        problems.push(`Reference is not same-origin: ${reference}`);
  const code = all
    .filter((file) => file.endsWith(".js"))
    .map((file) => readFileSync(join(client, file), "utf8"))
    .join("\n");
  if (!code.includes(marker.present)) problems.push(`Profile ${profile} lost its own wire marker`);
  if (code.includes(marker.absent))
    problems.push(`Profile ${profile} contains the other profile's wire marker`);
  return problems;
}

export function toBaseline(measurement: Measurement): Baseline {
  return {
    initialRequests: measurement.initialRequests,
    initialJsGzip: measurement.initialJs.gzip,
    initialCssGzip: measurement.initialCss.gzip,
    totalGzip: measurement.totalJs.gzip + measurement.totalCss.gzip,
    htmlBytes: measurement.htmlBytes,
  };
}

/** The regression budget: a metric may grow at most `regressionPercent` over its baseline (Design TH-01). */
export function checkBudgets(measurement: Measurement, budgets: Budgets): string[] {
  const baseline = budgets.profiles[measurement.profile];
  if (!baseline) return [`No baseline for profile ${measurement.profile}`];
  const actual = toBaseline(measurement);
  const problems: string[] = [];
  for (const key of Object.keys(baseline) as (keyof Baseline)[]) {
    const limit = Math.floor((baseline[key] * (100 + budgets.regressionPercent)) / 100);
    if (actual[key] > limit)
      problems.push(
        `${measurement.profile} ${key} ${actual[key]} exceeds ${limit} (baseline ${baseline[key]})`,
      );
  }
  return problems;
}
