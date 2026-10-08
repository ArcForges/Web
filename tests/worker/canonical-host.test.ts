// SPDX-License-Identifier: AGPL-3.0-only
// The canonical-host Worker (worker/index.ts) and its emitted candidate form. Ported one-for-one from the vitest file
// tests/unit/canonical-host.test.ts to node:test; the cases and the expected values are unchanged.
import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { pathToFileURL } from "node:url";
import { test } from "node:test";
import worker from "../../worker/index.ts";
import { root, verifyWorkerScript, workerScript } from "../../tooling/project.ts";

type Env = Parameters<typeof worker.fetch>[1];

function recordingAsset(body: string, headers: Record<string, string> = {}) {
  const calls: Request[] = [];
  const fetch = async (request: Request) => {
    calls.push(request);
    return new Response(body, { headers });
  };
  return { calls, env: { ASSETS: { fetch } } as unknown as Env };
}

const redirects: [string, string][] = [
  ["https://www.arcforges.com/", "https://arcforges.com/"],
  [
    "https://www.arcforges.com/cloud-hello/?name=a%2Fb&next=%2Fhello%2F",
    "https://arcforges.com/cloud-hello/?name=a%2Fb&next=%2Fhello%2F",
  ],
  [
    "http://www.arcforges.com:8080//other.example/path?x=1&x=2",
    "https://arcforges.com//other.example/path?x=1&x=2",
  ],
];

for (const [url, destination] of redirects)
  test(`www redirects before static assets: ${url}`, async () => {
    const asset = recordingAsset("asset");
    const response = await worker.fetch(new Request(url), asset.env);
    assert.equal(response.status, 308);
    assert.equal(response.headers.get("location"), destination);
    assert.equal(asset.calls.length, 0);
  });

for (const method of ["HEAD", "POST"])
  test(`canonical redirect preserves ${method} semantics`, async () => {
    const asset = recordingAsset("asset");
    const response = await worker.fetch(
      new Request("https://www.arcforges.com/api/example?mode=binary", { method }),
      asset.env,
    );
    assert.equal(response.status, 308);
    assert.equal(
      response.headers.get("location"),
      "https://arcforges.com/api/example?mode=binary",
    );
    assert.equal(asset.calls.length, 0);
  });

for (const url of [
  "https://arcforges.com/cloud-hello/",
  "http://127.0.0.1:4173/hello/",
  "https://www.arcforges.com.other.example/",
  "https://other.example/?next=https://www.arcforges.com/",
])
  test(`other hosts preserve the original asset request and response: ${url}`, async () => {
    const request = new Request(url, { headers: { accept: "text/html" } });
    const asset = recordingAsset("asset", { "content-security-policy": "default-src 'self'" });
    const response = await worker.fetch(request, asset.env);
    assert.equal(asset.calls.length, 1);
    assert.equal(asset.calls[0], request);
    assert.equal(response.headers.get("content-security-policy"), "default-src 'self'");
  });

test("delivery runs the reviewed Worker before assets without a deployment rebuild", async () => {
  const config = JSON.parse(await readFile(join(root, "wrangler.json"), "utf8"));
  assert.equal(config.main, "./worker/index.js");
  assert.equal(config.no_bundle, true);
  assert.equal(config.assets.binding, "ASSETS");
  assert.equal(config.assets.run_worker_first, true);
});

test("the emitted candidate Worker is importless and carries no type syntax", async () => {
  const emitted = await workerScript();
  assert.doesNotMatch(emitted, /^\s*import\s/mu);
  assert.doesNotMatch(emitted, /\brequire\(/u);
  assert.doesNotMatch(emitted, /\binterface\s|:\s*Env\b|:\s*Request\b/u);
  assert.match(emitted, /export default \{/u);
});

for (const url of [
  "https://www.arcforges.com/",
  "https://www.arcforges.com/cloud-hello/?name=a%2Fb&next=%2Fhello%2F",
  "http://www.arcforges.com:8080//other.example/path?x=1&x=2",
  "https://www.arcforges.com/api/example?mode=binary",
  "https://arcforges.com/cloud-hello/",
  "http://127.0.0.1:4173/hello/",
])
  test(`the emitted candidate Worker answers like the TypeScript source: ${url}`, async () => {
    const directory = await mkdtemp(join(tmpdir(), "arcforges-emitted-worker-"));
    try {
      const modulePath = join(directory, "index.mjs");
      await writeFile(modulePath, await workerScript());
      const emitted = (await import(pathToFileURL(modulePath).href)).default;
      const sourceAsset = recordingAsset("asset");
      const emittedAsset = recordingAsset("asset");
      const sourceResponse = await worker.fetch(new Request(url), sourceAsset.env);
      const emittedResponse = await emitted.fetch(new Request(url), emittedAsset.env);
      assert.equal(emittedResponse.status, sourceResponse.status);
      assert.equal(emittedResponse.headers.get("location"), sourceResponse.headers.get("location"));
      assert.equal(emittedAsset.calls.length, sourceAsset.calls.length);
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
    await verifyWorkerScript(directory);
    await writeFile(
      join(directory, "worker/index.js"),
      source.replace('arcforges.com";', 'other.example";'),
    );
    await assert.rejects(verifyWorkerScript(directory), /Worker script changed/u);
  } finally {
    assert.equal(dirname(directory), tmpdir());
    assert.match(basename(directory), /^arcforges-canonical-worker-/u);
    await rm(directory, { recursive: true, force: true });
  }
});
