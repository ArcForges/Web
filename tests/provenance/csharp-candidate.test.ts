// SPDX-License-Identifier: AGPL-3.0-only
// Determinism, seal and security tests of the C# candidate and the Blazor profile bundle. They run the built tool
// (tools/ArcForges.Web.Tooling, Release) on temporary copies. Build and verification only: nothing is uploaded.
// Build the tool first: dotnet build tools/ArcForges.Web.Tooling/ArcForges.Web.Tooling.csproj -c Release --no-restore
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { access, cp, mkdtemp, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";
import { expectedIdentity } from "../../tooling/build-identity.ts";
import { toolLibrary } from "../../tooling/candidate.ts";
import { root, workerScript } from "../../tooling/project.ts";

// A missing tool build fails here with the build command, not later with a spawn error for every test.
await access(toolLibrary).catch(() => {
  throw new Error(`The C# tool is not built at ${toolLibrary}. Build it first (see the header of this file).`);
});

const sha = (bytes: Uint8Array | string) => createHash("sha256").update(bytes).digest("hex");
const scratch = await mkdtemp(path.join(tmpdir(), "arcforges-candidate-"));
const identityRecord = expectedIdentity("0.1.0-local");
const source = identityRecord.build.sourceCommit;
const dirty = identityRecord.build.dirty;
const workerPath = path.join(scratch, "index.js");
const identityPath = path.join(scratch, "build-info.json");
await writeFile(workerPath, await workerScript());
await writeFile(identityPath, `${JSON.stringify(identityRecord, null, 2)}\n`);
const candidateDirectory = (name: string) => path.join(scratch, name);

interface ToolResult {
  status: number;
  output: string;
}

function tool(args: string[]): ToolResult {
  const result = spawnSync("dotnet", [toolLibrary, ...args], {
    cwd: root,
    encoding: "utf8",
    windowsHide: true,
    timeout: 600000,
  });
  return { status: result.status ?? -1, output: `${result.stdout ?? ""}${result.stderr ?? ""}` };
}

function build(
  out: string,
  options: { worker?: string; identity?: string; dirtyFlag?: string } = {},
): ToolResult {
  return tool([
    "candidate",
    "build",
    "--out",
    out,
    "--repo",
    root,
    "--source-ref",
    source,
    "--version",
    "0.1.0-local",
    "--identity",
    options.identity ?? identityPath,
    "--worker",
    options.worker ?? workerPath,
    "--dirty",
    options.dirtyFlag ?? String(dirty),
  ]);
}

function verify(directory: string, expectedSource?: string): ToolResult {
  const args = ["candidate", "verify", "--dir", directory, "--repo", root];
  if (expectedSource) args.push("--expected-source", expectedSource);
  return tool(args);
}

function expectRefusal(result: ToolResult, message: string) {
  assert.notEqual(result.status, 0, `Expected a refusal containing: ${message}\n${result.output}`);
  assert(
    result.output.includes(message),
    `Wrong refusal, expected "${message}":\n${result.output}`,
  );
}

async function listFiles(directory: string, prefix = ""): Promise<string[]> {
  const found: string[] = [];
  for (const entry of await readdir(path.join(directory, prefix), { withFileTypes: true })) {
    const relative = `${prefix}${entry.name}`;
    if (entry.isDirectory()) found.push(...(await listFiles(directory, `${relative}/`)));
    else found.push(relative);
  }
  return found.sort();
}

async function snapshot(directory: string): Promise<Map<string, string>> {
  const result = new Map<string, string>();
  for (const file of await listFiles(directory))
    result.set(file, sha(await readFile(path.join(directory, file))));
  return result;
}

/** Recomputes the receipt and the seal over the current files, so only the deeper rules can refuse a tampered candidate. */
async function reseal(directory: string) {
  const receiptPath = path.join(directory, "provenance/receipt.json");
  const receipt = JSON.parse(await readFile(receiptPath, "utf8"));
  const members: Record<string, string> = {};
  for (const file of await listFiles(directory))
    if (file !== "manifest.json" && file !== "provenance/receipt.json")
      members[file] = sha(await readFile(path.join(directory, file)));
  receipt.members = members;
  await writeFile(receiptPath, `${JSON.stringify(receipt, null, 2)}\n`);
  const manifestPath = path.join(directory, "manifest.json");
  const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  const files: Record<string, string> = {};
  for (const file of await listFiles(directory))
    if (file !== "manifest.json") files[file] = sha(await readFile(path.join(directory, file)));
  manifest.files = files;
  await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
}

async function copyCandidate(from: string, name: string): Promise<string> {
  const to = candidateDirectory(name);
  await cp(from, to, { recursive: true });
  return to;
}

const baseline = candidateDirectory("baseline");
const repeat = candidateDirectory("repeat");
const built = build(baseline);
if (built.status !== 0) throw new Error(`Baseline candidate build failed:\n${built.output}`);

test("two builds of the same inputs are the same bytes and verify against the source", async () => {
  const second = build(repeat);
  assert.equal(second.status, 0, second.output);
  assert.deepEqual(await snapshot(repeat), await snapshot(baseline), "Candidate builds differ");
  const passed = verify(baseline, source);
  assert.equal(passed.status, 0, passed.output);
  assert.match(passed.output, /Candidate verified: 0\.1\.0-local/u);
});

test("a changed public page is refused by the seal", async () => {
  const dir = await copyCandidate(baseline, "page-changed");
  await writeFile(
    path.join(dir, "assets/index.html"),
    `${await readFile(path.join(dir, "assets/index.html"), "utf8")}<!-- x -->`,
  );
  expectRefusal(verify(dir, source), "Candidate changed: assets/index.html");
});

test("an added public file is refused by the seal", async () => {
  const dir = await copyCandidate(baseline, "file-added");
  await writeFile(path.join(dir, "assets/extra.txt"), "unreviewed");
  expectRefusal(verify(dir, source), "Candidate file set changed");
});

test("a removed required member is refused", async () => {
  const dir = await copyCandidate(baseline, "member-removed");
  await rm(path.join(dir, "assets/404.html"));
  expectRefusal(verify(dir, source), "Candidate file set changed");
});

test("a candidate for another source is refused", async () => {
  expectRefusal(verify(baseline, "b".repeat(40)), "Candidate source does not match this run");
});

test("a Worker that differs from the reviewed emission is refused at build time", async () => {
  const worker = path.join(scratch, "other-worker.js");
  await writeFile(worker, "export default { fetch() { return new Response('x'); } };\n");
  expectRefusal(build(candidateDirectory("worker-refused"), { worker }), "Worker script changed");
});

test("an identity of another commit or another dirty state is refused at build time", async () => {
  const other = path.join(scratch, "other-identity.json");
  const commit = JSON.parse(await readFile(identityPath, "utf8"));
  commit.build.sourceCommit = "a".repeat(40);
  await writeFile(other, JSON.stringify(commit));
  expectRefusal(
    build(candidateDirectory("identity-commit"), { identity: other }),
    "another source commit",
  );

  const flipped = path.join(scratch, "flipped-identity.json");
  const state = JSON.parse(await readFile(identityPath, "utf8"));
  state.build.dirty = !dirty;
  await writeFile(flipped, JSON.stringify(state));
  expectRefusal(
    build(candidateDirectory("identity-dirty"), { identity: flipped }),
    "another dirty state",
  );
});

test("an existing output directory is refused, so a stale candidate cannot be sealed", async () => {
  expectRefusal(build(baseline), "Refusing to write into an existing path");
});

test("a resealed public page change is refused by the regenerated Site comparison", async () => {
  const dir = await copyCandidate(baseline, "resealed-page");
  const file = path.join(dir, "assets/hello/index.html");
  await writeFile(file, `${await readFile(file, "utf8")}<script>alert(1)</script>`);
  await reseal(dir);
  expectRefusal(
    verify(dir, source),
    "Public Site bytes differ from the regenerated Site: hello/index.html",
  );
});

test("a resealed policy loosened to unsafe-inline is refused by the regenerated headers", async () => {
  const dir = await copyCandidate(baseline, "resealed-headers");
  const headers = path.join(dir, "assets/_headers");
  await writeFile(
    headers,
    (await readFile(headers, "utf8")).replace(
      "script-src 'self'",
      "script-src 'self' 'unsafe-inline'",
    ),
  );
  await reseal(dir);
  expectRefusal(
    verify(dir, source),
    "Public Site bytes differ from the regenerated Site: _headers",
  );
});

test("a resealed wrangler change that enables workers.dev is refused", async () => {
  const dir = await copyCandidate(baseline, "resealed-wrangler");
  const config = JSON.parse(await readFile(path.join(dir, "wrangler.json"), "utf8"));
  config.workers_dev = true;
  await writeFile(path.join(dir, "wrangler.json"), `${JSON.stringify(config, null, 2)}\n`);
  await reseal(dir);
  expectRefusal(verify(dir, source), "workers.dev must stay disabled");
});

test("a resealed Worker replacement is refused by the pinned digest", async () => {
  const dir = await copyCandidate(baseline, "resealed-worker");
  await writeFile(
    path.join(dir, "worker/index.js"),
    "export default { fetch: () => fetch('https://example.invalid') };\n",
  );
  await reseal(dir);
  expectRefusal(verify(dir, source), "Worker script changed from the reviewed source");
});

test("a resealed licence change is refused against the repository", async () => {
  const dir = await copyCandidate(baseline, "resealed-licence");
  await writeFile(path.join(dir, "assets/license.txt"), "Not the AGPL.\n");
  await reseal(dir);
  expectRefusal(verify(dir, source), "Full AGPL legal text changed");
});

test("a resealed identity for another source is refused", async () => {
  const dir = await copyCandidate(baseline, "resealed-identity");
  const info = JSON.parse(await readFile(path.join(dir, "assets/__build-info.json"), "utf8"));
  info.build.sourceCommit = "c".repeat(40);
  await writeFile(path.join(dir, "assets/__build-info.json"), `${JSON.stringify(info, null, 2)}\n`);
  await reseal(dir);
  expectRefusal(verify(dir, source), "The build identity names another source commit");
});

// The bundle tests consume the publish of ArcForges.Web.App made by the local proof (artifacts/publish/app). Without it,
// the tests are skipped rather than run against a synthetic bundle.
const publish = path.join(root, "artifacts/publish/app");
const havePublish = await access(path.join(publish, "wwwroot/index.html")).then(
  () => true,
  () => false,
);

async function buildBundle(out: string): Promise<{ status: number; output: string; name: string }> {
  const result = tool(["profiles", "bundle", "--publish", publish, "--out", out]);
  const name =
    (await listFiles(out)).find((file) => /^web-profiles-[0-9a-f]{64}\.tar$/u.test(file)) ?? "";
  return { ...result, name };
}

test(
  "the profile bundle keeps its name pattern, layout and reviewed headers, and verifies",
  { skip: !havePublish },
  async () => {
    const out = candidateDirectory("bundle");
    const first = await buildBundle(out);
    assert.equal(first.status, 0, first.output);
    assert.match(first.name, /^web-profiles-[0-9a-f]{64}\.tar$/u);
    const archive = path.join(out, first.name);
    const digest = first.name.slice("web-profiles-".length, -".tar".length);
    const verified = tool(["profiles", "verify", "--bundle", archive, "--expected-digest", digest]);
    assert.equal(verified.status, 0, verified.output);
    const repeated = await buildBundle(candidateDirectory("bundle-repeat"));
    assert.equal(repeated.name, first.name, "Bundle builds differ");
  },
);

test(
  "a bundle with a changed byte is refused by its digest and by its content hash",
  { skip: !havePublish },
  async () => {
    const out = candidateDirectory("bundle-tamper");
    const built = await buildBundle(out);
    const archive = path.join(out, built.name);
    const bytes = await readFile(archive);
    const flipped = Buffer.from(bytes);
    // The first content byte of the last entry (its header is one block before it, and the archive ends with two zero
    // blocks and the padding of the last file), so the header stays valid and only the content changes.
    const contentByte = bytes.length - 1024 - 512;
    flipped[contentByte] = (flipped[contentByte] ?? 0) ^ 0x01;
    const tampered = path.join(out, "tampered.tar");
    await writeFile(tampered, flipped);
    const digest = built.name.slice("web-profiles-".length, -".tar".length);
    expectRefusal(
      tool(["profiles", "verify", "--bundle", tampered, "--expected-digest", digest]),
      "Bundle digest mismatch",
    );
    expectRefusal(tool(["profiles", "verify", "--bundle", tampered]), "Content of");
  },
);

test(
  "a truncated bundle is refused by the strict archive reader",
  { skip: !havePublish },
  async () => {
    const out = candidateDirectory("bundle-truncated");
    const built = await buildBundle(out);
    const bytes = await readFile(path.join(out, built.name));
    const truncated = path.join(out, "truncated.tar");
    await writeFile(truncated, bytes.subarray(0, bytes.length - 1024));
    const result = tool(["profiles", "verify", "--bundle", truncated]);
    assert.notEqual(result.status, 0, result.output);
    assert.match(result.output, /terminator|truncated|Not a bundle archive|Data after/u);
  },
);

test("the static Site candidate exists in the seal with its required pages", async () => {
  for (const page of [
    "assets/index.html",
    "assets/hello/index.html",
    "assets/cloud-hello/index.html",
    "assets/404.html",
  ])
    await stat(path.join(baseline, page));
  const sbom = JSON.parse(await readFile(path.join(baseline, "sbom.cdx.json"), "utf8"));
  assert.equal(sbom.bomFormat, "CycloneDX");
  assert(sbom.components.length > 0, "The build SBOM lists no packages");
  const runtime = JSON.parse(await readFile(path.join(baseline, "runtime-sbom.cdx.json"), "utf8"));
  assert.deepEqual(runtime.components, [], "The static Site must ship no runtime dependency");
});

test("the seal lists every member and no member is a link or a source map", async () => {
  const manifest = JSON.parse(await readFile(path.join(baseline, "manifest.json"), "utf8"));
  assert.deepEqual(
    Object.keys(manifest.files).sort(),
    (await listFiles(baseline)).filter((file) => file !== "manifest.json"),
  );
  assert(
    Object.keys(manifest.files).every((file) => !file.endsWith(".map")),
    "A source map is published",
  );
});
