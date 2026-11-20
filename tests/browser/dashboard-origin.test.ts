// SPDX-License-Identifier: AGPL-3.0-only
// Opt-in test of the isolated static origin using a real compiled profile archive.
// Set ARCFORGES_DASHBOARD_PROFILE_BUNDLE and ARCFORGES_DASHBOARD_PROFILE_SHA256 and run this file directly.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, realpath, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";
import { dashboardAssets } from "./dashboard-origin.ts";

const archive = process.env.ARCFORGES_DASHBOARD_PROFILE_BUNDLE;
const pin = process.env.ARCFORGES_DASHBOARD_PROFILE_SHA256;
assert(archive && pin, "Supply the Web-produced profile archive and its independently known SHA-256.");

test("verified compiled Dashboard documents, cache policy and exact route boundaries", async () => {
  const asset = await dashboardAssets(archive, pin);
  const overview = asset("GET", "/overview");
  assert.equal(overview.status, 200);
  const shell = overview.body?.toString("utf8") ?? "";
  assert.match(shell, /<title>ArcForges Dashboard<\/title>/u);
  assert.match(shell, /_framework\/blazor\.webassembly\.js/u);
  assert.match(overview.headers["Content-Type"] ?? "", /^text\/html/u);
  assert.match(
    overview.headers["Content-Security-Policy"] ?? "",
    /^default-src 'self'; script-src 'self' 'wasm-unsafe-eval'.*; connect-src 'self';/u,
  );
  assert.equal(overview.headers["X-Frame-Options"], "DENY");
  assert.equal(overview.headers["X-Content-Type-Options"], "nosniff");
  assert.equal(overview.headers["Cache-Control"], "public, no-cache, no-transform");
  const device = "/devices/77bfe61e-b5ea-4a5b-8c22-3ab49787a10e";
  for (const route of ["/devices", device, device + "?tab=summary"]) {
    const reply = asset("GET", route);
    assert.equal(reply.status, 200, route);
    assert.deepEqual(reply.body, overview.body, route);
    assert.deepEqual(reply.headers, overview.headers, route);
  }
  const head = asset("HEAD", device);
  assert.equal(head.status, 200);
  assert.deepEqual(head.headers, overview.headers);
  assert.equal(head.body, undefined);
  const unchanged = asset("GET", device, overview.headers.ETag);
  assert.equal(unchanged.status, 304);
  assert.equal(unchanged.headers["Content-Length"], undefined);
  assert.equal(unchanged.headers["Cache-Control"], overview.headers["Cache-Control"]);
  assert.equal(asset("GET", "/app.css").headers["Content-Type"], "text/css; charset=utf-8");
  assert.equal(
    asset("GET", "/_framework/blazor.webassembly.js").headers["Cache-Control"],
    "public, no-cache, no-transform",
  );
  const image = await readFile(archive);
  const manifestLength = Number.parseInt(image.toString("ascii", 124, 135), 8);
  const manifest = JSON.parse(image.toString("utf8", 512, 512 + manifestLength)) as {
    files: Record<string, { sha256: string; bytes: number }>;
  };
  const wasm = Object.keys(manifest.files).find((name) => name.endsWith(".wasm"));
  assert(wasm);
  const resource = asset("GET", "/" + wasm);
  assert.equal(resource.status, 200);
  assert.equal(resource.headers["Content-Type"], "application/wasm");
  assert.equal(resource.headers["Cache-Control"], "public, max-age=31536000, immutable, no-transform");
  assert.equal(resource.body?.length, manifest.files[wasm]?.bytes);
  const activityPath = Object.keys(manifest.files).find((name) =>
    /^_framework\/arcforges-dashboard-activity\.[0-9a-f]{10}\.js$/u.test(name),
  );
  assert(activityPath, "The compiled manifest names the fingerprinted lifecycle module.");
  const activity = asset("GET", "/" + activityPath);
  assert.equal(activity.status, 200);
  assert.equal(activity.headers["Content-Type"], "text/javascript; charset=utf-8");
  assert.equal(activity.headers["Cache-Control"], resource.headers["Cache-Control"]);
  assert(activity.body);
  const activityHash = createHash("sha256").update(activity.body).digest("hex");
  assert.equal(activityHash, manifest.files[activityPath]?.sha256);
  assert.equal(activityPath, "_framework/arcforges-dashboard-activity." + activityHash.slice(0, 10) + ".js");

  for (const target of [
    "/",
    "/overview/",
    "/overview/more",
    "/Overview",
    "/devices/",
    "/devices/not-a-guid",
    "/devices/77BFE61E-B5EA-4A5B-8C22-3AB49787A10E",
    "/devices//77bfe61e-b5ea-4a5b-8c22-3ab49787a10e",
    "/devices/%37%37bfe61e-b5ea-4a5b-8c22-3ab49787a10e",
    "/x/../overview",
    "/%6fverview",
    "/account",
    "/chat",
    "/index.html",
    "/_headers",
    "/manifest.json",
    "/appsettings.json",
    "/api/unknown",
    "/dashboard-activity.js",
    "/_framework/arcforges-dashboard-activity.js",
    "/_framework/dotnet.js.gz",
    "/_framework/blazor.webassembly.js.map",
  ]) {
    assert.deepEqual(asset("GET", target), { status: 404, headers: { "Cache-Control": "no-store" } }, target);
  }
  assert.equal(asset("POST", device).status, 404);
});

test("the Dashboard rejects wrong and repinned damaged profile bytes", async () => {
  const parent = await realpath(tmpdir());
  const scratch = await mkdtemp(path.join(parent, "arcforges-dashboard-archive-"));
  assert.equal(path.dirname(scratch), parent);
  try {
    const bytes = await readFile(archive);
    const wrongPin = "0".repeat(64);
    const wrongName = path.join(scratch, "web-profiles-" + wrongPin + ".tar");
    await writeFile(wrongName, bytes);
    await assert.rejects(dashboardAssets(wrongName, wrongPin), /differs from the approved pin/u);
    // Alter the first byte after the final asset's canonical header. Its manifest row still describes the original bytes.
    const lastAsset = bytes.length - 1024 - 512;
    assert(lastAsset >= 512 && bytes[lastAsset] !== undefined);
    bytes[lastAsset] ^= 1;
    const repin = createHash("sha256").update(bytes).digest("hex");
    const repinned = path.join(scratch, "web-profiles-" + repin + ".tar");
    await writeFile(repinned, bytes);
    await assert.rejects(dashboardAssets(repinned, repin), /Verification failed: Content of/u);
  } finally {
    await rm(scratch, { recursive: true, force: true });
  }
});
