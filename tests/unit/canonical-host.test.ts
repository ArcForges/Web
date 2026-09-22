// SPDX-License-Identifier: AGPL-3.0-only
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { expect, test, vi } from "vitest";
import worker from "../../worker/index.js";
import { root, verifyWorkerScript } from "../../tooling/project.ts";

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

test("candidate verification rejects a replaced private Worker independently of its manifest", async () => {
  const directory = await mkdtemp(join(tmpdir(), "arcforges-canonical-worker-"));
  try {
    await mkdir(join(directory, "worker"));
    const source = (await readFile(join(root, "worker/index.js"), "utf8")).replaceAll("\r\n", "\n");
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
