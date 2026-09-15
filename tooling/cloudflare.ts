// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { candidate, digest, json, npm, root, run, save, verify } from "./project.ts";

const statePath = join(root, "artifacts/deployment.json");
type Identity = { source: string; version: string };
type Deployment = Identity & { url: string; verified: boolean; deployedAt: string };
export const deploymentUrl = "https://arcforges.com";
function checkUrl(url: string) {
  assert.equal(url, deploymentUrl, "Unexpected deployment host");
}
export function requireCustomDomain(
  domains: { hostname: string; service: string; environment: string }[],
) {
  assert(
    domains.some(
      (domain) =>
        domain.hostname === "arcforges.com" &&
        domain.service === "arcforges-web" &&
        domain.environment === "production",
    ),
    "Attach arcforges.com to arcforges-web in Cloudflare Settings > Domains & Routes before deployment. No DNS changes were made.",
  );
}
export async function waitForIdentity(
  url: string,
  expected: Identity,
  options: {
    fetcher?: typeof fetch;
    pause?: () => Promise<void>;
    attempts?: number;
  } = {},
) {
  checkUrl(url);
  const fetcher = options.fetcher ?? fetch;
  const pause = options.pause ?? (() => new Promise((resolve) => setTimeout(resolve, 2000)));
  for (let attempt = 0; attempt < (options.attempts ?? 45); attempt++) {
    try {
      const response = await fetcher(`${url}/__build.json?verify=${attempt}`, {
        cache: "no-store",
        redirect: "error",
        signal: AbortSignal.timeout(10000),
      });
      if (response.ok) {
        const actual = (await response.json()) as Identity;
        if (actual.source === expected.source && actual.version === expected.version) return;
      } else if ([401, 403].includes(response.status))
        throw new Error(`Access denied (${response.status})`);
    } catch (error) {
      if (error instanceof Error && error.message.startsWith("Access denied")) throw error;
    }
    if (attempt + 1 < (options.attempts ?? 45)) await pause();
  }
  throw new Error(
    "Deployed assets did not reach the expected source/version within the read-only polling window. No redeployment was attempted.",
  );
}
async function deploy() {
  const manifest = await verify();
  assert(!manifest.dirty, "Commit the source before deployment");
  assert.equal(
    process.env.GITHUB_REF,
    "refs/heads/main",
    "Automatic deployment only runs from main",
  );
  assert.equal(process.env.GITHUB_EVENT_NAME, "push", "Automatic deployment requires a main push");
  const account = process.env.CLOUDFLARE_ACCOUNT_ID;
  const token = process.env.CLOUDFLARE_API_TOKEN;
  assert(
    account && /^[a-f0-9]{32}$/.test(account),
    "Set CLOUDFLARE_ACCOUNT_ID in the GitHub cloudflare environment variables.",
  );
  assert(
    typeof token === "string" && token.trim().length > 0,
    "Deployment is not configured: add CLOUDFLARE_API_TOKEN to the GitHub cloudflare environment secrets. PR checks do not need it.",
  );
  // Refuse an old queued run or rerun that would roll production behind main.
  const current = await fetch("https://api.github.com/repos/ArcForges/Web/git/ref/heads/main", {
    headers: {
      Accept: "application/vnd.github+json",
      ...(process.env.GH_TOKEN ? { Authorization: `Bearer ${process.env.GH_TOKEN}` } : {}),
    },
    signal: AbortSignal.timeout(15000),
  });
  assert(current.ok, `Cannot verify main (${current.status})`);
  const ref = (await current.json()) as { object: { sha: string } };
  assert.equal(
    ref.object.sha,
    manifest.source,
    "Superseded run: deploy the current main candidate instead",
  );
  const response = await fetch(
    `https://api.cloudflare.com/client/v4/accounts/${account}/workers/domains?hostname=arcforges.com`,
    {
      headers: { Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(15000),
    },
  );
  assert(
    response.ok,
    `Cannot read Worker domain mappings (${response.status}). Check Workers Scripts:Edit permission.`,
  );
  const body = (await response.json()) as {
    success: boolean;
    result?: { hostname: string; service: string; environment: string }[];
  };
  assert(body.success && Array.isArray(body.result), "Cannot read Worker domain mappings");
  requireCustomDomain(body.result);
  const state: Deployment = {
    source: manifest.source,
    version: manifest.version,
    url: deploymentUrl,
    verified: false,
    deployedAt: new Date().toISOString(),
  };
  await save(statePath, { ...state, status: "deploying" });
  try {
    const output = run(process.execPath, [
      join(root, "node_modules/wrangler/bin/wrangler.js"),
      "deploy",
      "--config",
      join(candidate, "wrangler.json"),
    ]);
    console.log(output.replaceAll(token, "[REDACTED]"));
    await save(statePath, { ...state, status: "deployed-awaiting-verification" });
  } catch (error) {
    await save(statePath, { ...state, status: "deployment-failed" });
    throw new Error(String(error).replaceAll(token, "[REDACTED]"));
  }
}
async function smoke() {
  const manifest = await verify();
  const state = (await json(statePath)) as Deployment;
  assert.equal(state.source, manifest.source);
  assert.equal(state.version, manifest.version);
  checkUrl(state.url);
  console.log("Waiting for the published static candidate using read-only HTTPS requests.");
  await waitForIdentity(state.url, state);
  for (const [path, hash] of Object.entries(manifest.files)) {
    if (!path.startsWith("assets/") || path === "assets/_headers" || path === "assets/404.html")
      continue;
    let route = path.slice("assets".length);
    if (route.endsWith("/index.html")) route = route.slice(0, -"index.html".length);
    const response = await fetch(`${state.url}${route}`, {
      headers: { Accept: route.endsWith("/") ? "text/html" : "*/*" },
      redirect: "error",
      signal: AbortSignal.timeout(15000),
    });
    assert(response.ok, `Deployed ${route} returned ${response.status}`);
    assert.equal(
      digest(new Uint8Array(await response.arrayBuffer())),
      hash,
      `Deployed bytes differ: ${route}`,
    );
    assert.equal(response.headers.get("x-content-type-options"), "nosniff");
    assert(response.headers.get("cache-control")?.includes("no-transform"));
    if (route.endsWith("/"))
      assert(
        response.headers.get("content-security-policy")?.includes("script-src 'self' 'sha256-"),
        "Missing generated CSP",
      );
    if (route.startsWith("/assets/"))
      assert(response.headers.get("cache-control")?.includes("immutable"));
  }
  for (const path of ["/api/missing", "/assets/missing.js", "/not-a-page"]) {
    const response = await fetch(state.url + path, {
      signal: AbortSignal.timeout(15000),
      redirect: "error",
    });
    assert.equal(response.status, 404, `Unexpected fallback at ${path}`);
    assert.equal(
      digest(new Uint8Array(await response.arrayBuffer())),
      digest(await readFile(join(candidate, "assets/404.html"))),
    );
  }
  console.log("Verifying the real domain in Chromium, Firefox and WebKit.");
  process.stdout.write(
    npm(["exec", "--no", "--", "playwright", "test", "--config", "playwright.live.config.ts"]),
  );
  await save(statePath, {
    ...state,
    verified: true,
    status: "verified",
    browsersVerified: true,
    verifiedAt: new Date().toISOString(),
  });
  console.log(`Verified ${state.version}: ${state.url}`);
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv[2] === "deploy") await deploy();
  else if (process.argv[2] === "smoke") await smoke();
  else throw new Error("Use deploy or smoke.");
}
