// SPDX-License-Identifier: AGPL-3.0-only
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { pathToFileURL } from "node:url";
import { expect, test, vi } from "vitest";
import worker from "../../worker/index.ts";
import { root, verifyWorkerScript, workerScript } from "../../tooling/project.ts";

test.each([
  ["https://www.arcforges.com/", "https://arcforges.com/"],
  [
    "https://www.arcforges.com/cloud-hello/?name=a%2Fb&next=%2Fhello%2F",
    "https://arcforges.com/cloud-hello/?name=a%2Fb&next=%2Fhello%2F",
  ],
  [
    "http://www.arcforges.com:8080//other.example/path?x=1&x=2",
    "https://arcforges.com//other.example/path?x=1&x=2",
  ],
])("www redirects before static assets: %s", async (url, destination) => {
  const fetch = vi.fn<(_request: Request) => Promise<Response>>();
  const response = await worker.fetch(new Request(url), { ASSETS: { fetch } });
  expect(response.status).toBe(308);
  expect(response.headers.get("location")).toBe(destination);
  expect(fetch).not.toHaveBeenCalled();
});

test.each(["HEAD", "POST"])("canonical redirect preserves %s semantics", async (method) => {
  const fetch = vi.fn<(_request: Request) => Promise<Response>>();
  const response = await worker.fetch(
    new Request("https://www.arcforges.com/api/example?mode=binary", { method }),
    { ASSETS: { fetch } },
  );
  expect(response.status).toBe(308);
  expect(response.headers.get("location")).toBe("https://arcforges.com/api/example?mode=binary");
  expect(fetch).not.toHaveBeenCalled();
});

test.each([
  "https://arcforges.com/cloud-hello/",
  "http://127.0.0.1:4173/hello/",
  "https://www.arcforges.com.other.example/",
  "https://other.example/?next=https://www.arcforges.com/",
])("other hosts preserve the original asset request and response: %s", async (url) => {
  const request = new Request(url, { headers: { accept: "text/html" } });
  const asset = new Response("asset", {
    headers: { "content-security-policy": "default-src 'self'" },
  });
  const fetch = vi.fn(async () => asset);
  const response = await worker.fetch(request, { ASSETS: { fetch } });
  expect(fetch).toHaveBeenCalledExactlyOnceWith(request);
  expect(response).toBe(asset);
});

test("delivery runs the reviewed Worker before assets without a deployment rebuild", async () => {
  const config = JSON.parse(await readFile(join(root, "wrangler.json"), "utf8"));
  expect(config.main).toBe("./worker/index.js");
  expect(config.no_bundle).toBe(true);
  expect(config.assets.binding).toBe("ASSETS");
  expect(config.assets.run_worker_first).toBe(true);
});

test("the emitted candidate Worker is importless and carries no type syntax", async () => {
  const emitted = await workerScript();
  expect(emitted).not.toMatch(/^\s*import\s/mu);
  expect(emitted).not.toMatch(/\brequire\(/u);
  expect(emitted).not.toMatch(/\binterface\s|:\s*Env\b|:\s*Request\b/u);
  expect(emitted).toMatch(/export default \{/u);
});

test.each([
  "https://www.arcforges.com/",
  "https://www.arcforges.com/cloud-hello/?name=a%2Fb&next=%2Fhello%2F",
  "http://www.arcforges.com:8080//other.example/path?x=1&x=2",
  "https://www.arcforges.com/api/example?mode=binary",
  "https://arcforges.com/cloud-hello/",
  "http://127.0.0.1:4173/hello/",
])("the emitted candidate Worker answers like the TypeScript source: %s", async (url) => {
  const directory = await mkdtemp(join(tmpdir(), "arcforges-emitted-worker-"));
  try {
    const modulePath = join(directory, "index.mjs");
    await writeFile(modulePath, await workerScript());
    const emitted = (await import(pathToFileURL(modulePath).href)).default;
    const sourceAsset = vi.fn(async () => new Response("asset"));
    const emittedAsset = vi.fn(async () => new Response("asset"));
    const sourceResponse = await worker.fetch(new Request(url), { ASSETS: { fetch: sourceAsset } });
    const emittedResponse = await emitted.fetch(new Request(url), {
      ASSETS: { fetch: emittedAsset },
    });
    expect(emittedResponse.status).toBe(sourceResponse.status);
    expect(emittedResponse.headers.get("location")).toBe(sourceResponse.headers.get("location"));
    expect(emittedAsset.mock.calls.length).toBe(sourceAsset.mock.calls.length);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("candidate verification rejects a replaced private Worker independently of its manifest", async () => {
  const directory = await mkdtemp(join(tmpdir(), "arcforges-canonical-worker-"));
  try {
    await mkdir(join(directory, "worker"));
    const source = await workerScript();
    await writeFile(join(directory, "worker/index.js"), source);
    await expect(verifyWorkerScript(directory)).resolves.toBeUndefined();
    await writeFile(
      join(directory, "worker/index.js"),
      source.replace('arcforges.com";', 'other.example";'),
    );
    await expect(verifyWorkerScript(directory)).rejects.toThrow("Worker script changed");
  } finally {
    expect(dirname(directory)).toBe(tmpdir());
    expect(basename(directory)).toMatch(/^arcforges-canonical-worker-/u);
    await rm(directory, { recursive: true, force: true });
  }
});
