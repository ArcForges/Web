// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { cp, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";
import {
  candidate,
  contentSecurityPolicy,
  digest,
  files,
  json,
  save,
  seal,
  verify,
} from "../../tooling/project.ts";

// A positive case must consume a real built candidate. There is no synthetic positive shortcut.
const baseline = await verify();
test("the actual compiled candidate has the complete browser and legal closure", async () => {
  const receipt = await json(path.join(candidate, "provenance/receipt.json"));
  assert.equal(receipt.parsedModules, 260);
  assert.equal(receipt.emittedModules, 129);
  const bom = await json(path.join(candidate, "runtime-sbom.cdx.json"));
  const names = new Set(bom.components.map((component: { name: string }) => component.name));
  for (const name of [
    "react",
    "react-dom",
    "react-router",
    "scheduler",
    "vite",
    "rolldown",
    "tailwindcss",
    "turbo-stream",
    "protobuf-wkt",
  ])
    assert(names.has(name), `Missing emitted origin: ${name}`);
  assert(!names.has("isbot") && !names.has("cookie-es"));
});

const r8ProfilePath = "eng/provenance/profiles/browser-resources-r8.json";
const expectedR8SourcePaths = [
  "node_modules/@bufbuild/protobuf/dist/esm/wkt/gen/google/protobuf/api_pb.js",
  "node_modules/@connectrpc/connect-web/dist/esm/assert-fetch-api.js",
  "packages/protobuf/src/wkt/gen/google/protobuf/api_pb.ts",
  "packages/connect-web/src/assert-fetch-api.ts",
  "packages/shared/ReactFlightPropertyAccess.js",
];
type DigestBinding = { path: string; digest: string };

function literalArray(block: string, key: string) {
  const start = block.indexOf(`${key} = [`);
  assert.notEqual(start, -1, `Missing ${key} array in Gitleaks allowlist`);
  const contentStart = start + `${key} = [`.length;
  const firstLineEnd = block.indexOf("\n", contentStart);
  const firstLine = block.slice(contentStart, firstLineEnd === -1 ? block.length : firstLineEnd);
  const inlineClose = firstLine.lastIndexOf("]");
  const end = inlineClose === -1 ? block.indexOf("\n]", contentStart) : contentStart + inlineClose;
  assert.notEqual(end, -1, `Unterminated ${key} array in Gitleaks allowlist`);
  return block
    .slice(contentStart, end)
    .split(/\r?\n/u)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      assert(line.startsWith("'''"), `Expected TOML literal string in ${key}`);
      assert(line.endsWith("''',") || line.endsWith("'''"), `Malformed ${key} entry`);
      return line.endsWith("''',") ? line.slice(3, -4) : line.slice(3, -3);
    });
}

function isSha256(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.length === 64 &&
    [...value].every((character) => "0123456789abcdef".includes(character))
  );
}

function profileBindings(value: unknown): DigestBinding[] {
  if (Array.isArray(value)) return value.flatMap(profileBindings);
  if (value === null || typeof value !== "object") return [];
  return Object.entries(value).flatMap(([key, child]) => [
    ...(expectedR8SourcePaths.includes(key) && isSha256(child)
      ? [{ path: key, digest: child }]
      : []),
    ...profileBindings(child),
  ]);
}

async function r8GitleaksFixture() {
  const root = process.cwd();
  const config = await readFile(path.join(root, ".gitleaks.toml"), "utf8");
  const profile = await json(path.join(root, r8ProfilePath));
  const blocks = config
    .split(/(?=^\[\[allowlists\]\]\s*$)/mu)
    .filter((block) => block.startsWith("[[allowlists]]"));
  const applicable = blocks.filter((block) =>
    literalArray(block, "paths").some((pattern) => new RegExp(pattern).test(r8ProfilePath)),
  );
  assert.equal(applicable.length, 1, "r8 must have exactly one applicable allowlist");

  const [block] = applicable;
  assert(block);
  assert(block.split(/\r?\n/u).includes('condition = "AND"'));
  assert(block.split(/\r?\n/u).includes('targetRules = ["generic-api-key"]'));
  assert(block.split(/\r?\n/u).includes('regexTarget = "line"'));
  assert.deepEqual(literalArray(block, "paths"), [
    String.raw`^eng/provenance/profiles/browser-resources-r8\.json$`,
  ]);

  const observedRows = profileBindings(profile);
  const uniqueBindings = [
    ...new Map(
      observedRows.map((binding) => [`${binding.path}\n${binding.digest}`, binding]),
    ).values(),
  ];
  assert.equal(observedRows.length, 8, "The r8 profile must retain all eight observed rows");
  assert.equal(
    uniqueBindings.length,
    6,
    "The eight rows must contain exactly six unique path/digest bindings",
  );
  assert.deepEqual(
    [...new Set(uniqueBindings.map(({ path: sourcePath }) => sourcePath))].sort(),
    [...expectedR8SourcePaths].sort(),
    "Only the five observed public source paths may be bound",
  );

  const expectedPatterns = uniqueBindings
    .map(({ path: sourcePath, digest }) => {
      const escapedPath = sourcePath.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&");
      return `^\\s*"${escapedPath}": "${digest}",?$`;
    })
    .sort();
  const patterns = literalArray(block, "regexes");
  assert.deepEqual(
    [...patterns].sort(),
    expectedPatterns,
    "Only the six exact profile-backed bindings may be ignored",
  );

  const linePatterns = patterns.map((pattern) => new RegExp(pattern));
  const pathPatterns = literalArray(block, "paths").map((pattern) => new RegExp(pattern));
  const matches = (file: string, line: string) =>
    pathPatterns.some((pattern) => pattern.test(file)) &&
    linePatterns.some((pattern) => pattern.test(line));
  const formatRow = (file: string, digest: string) => `"${file}": "${digest}",`;
  for (const { path: sourcePath, digest } of observedRows)
    assert(matches(r8ProfilePath, formatRow(sourcePath, digest)));

  return { uniqueBindings, matches, formatRow };
}

test("the r8 Gitleaks exception exactly matches profile-backed digest bindings", async () => {
  const { uniqueBindings, matches, formatRow } = await r8GitleaksFixture();
  for (const { path: sourcePath, digest } of uniqueBindings)
    assert(matches(r8ProfilePath, formatRow(sourcePath, digest)));
});

test("the r8 Gitleaks exception rejects digest, path and credential mutations", async () => {
  const { uniqueBindings, matches, formatRow } = await r8GitleaksFixture();
  const first = uniqueBindings[0];
  assert(first);
  const changedDigest = `${first.digest.slice(0, -1)}${first.digest.endsWith("0") ? "1" : "0"}`;
  const differentSourcePath = `${first.path}.unreviewed`;
  const otherProfilePath = "eng/provenance/profiles/browser-resources-r9.json";
  const unrelatedDigest = "f".repeat(64);
  const credentialLine = ['"api', '_key": "', "ghp", "_", "example", '"'].join("");

  assert(!matches(r8ProfilePath, formatRow(first.path, changedDigest)));
  assert(!matches(r8ProfilePath, formatRow(differentSourcePath, first.digest)));
  assert(!matches(otherProfilePath, formatRow(first.path, first.digest)));
  assert(!matches(r8ProfilePath, formatRow(first.path, unrelatedDigest)));
  assert(!matches(r8ProfilePath, credentialLine));
});

async function reseal(root: string) {
  const receipt = await json(path.join(root, "provenance/receipt.json"));
  receipt.members = Object.fromEntries(
    await Promise.all(
      (await files(root))
        .filter((file) => !["manifest.json", "provenance/receipt.json"].includes(file))
        .map(async (file) => [file, digest(await readFile(path.join(root, file)))]),
    ),
  );
  await save(path.join(root, "provenance/receipt.json"), receipt);
  await seal(root, { source: baseline.source, version: baseline.version, dirty: baseline.dirty });
}
function rejects(name: string, change: (root: string) => Promise<void>, reason: RegExp) {
  test(name, async () => {
    const root = await mkdtemp(path.join(tmpdir(), "web-candidate-provenance-"));
    try {
      await cp(candidate, root, { recursive: true });
      await change(root);
      await reseal(root);
      await assert.rejects(verify(root, baseline.source), reason);
    } finally {
      assert.equal(path.dirname(root), tmpdir());
      assert(path.basename(root).startsWith("web-candidate-provenance-"));
      await rm(root, { recursive: true, force: true });
    }
  });
}
async function edit(root: string, file: string, change: (text: string) => string) {
  const full = path.join(root, file);
  await writeFile(full, change(await readFile(full, "utf8")));
}
rejects(
  "reject executable changes after both manifests are resealed",
  async (root) => {
    const file = (await files(root)).find((file) =>
      /^assets\/assets\/entry.client-.*\.js$/u.test(file),
    );
    assert(file);
    await edit(root, file, (text) => `${text}\nfetch('/unreviewed');`);
  },
  /independently reviewed two-build oracle/u,
);
rejects(
  "reject added executable modules with plausible generated names",
  async (root) => {
    await writeFile(path.join(root, "assets/assets/extra-12345678.js"), "console.log('new');");
  },
  /Unclassified compiled browser resource/u,
);
rejects(
  "reject omitted compiled modules",
  async (root) => {
    const profile = await json(path.join(root, "provenance/profile.json"));
    const chunk = profile.graph.chunks.find(
      (item: { modules: string[] }) => item.modules.length > 0,
    );
    assert(chunk);
    const stem = path.posix.basename(chunk.name).replace(/\.js$/u, "");
    const file = (await files(root)).find(
      (file) =>
        file.startsWith("assets/assets/") &&
        file.endsWith(".js") &&
        path.posix.basename(file).startsWith(`${stem}-`),
    );
    assert(file);
    await rm(path.join(root, file));
  },
  /independently reviewed two-build oracle/u,
);
rejects(
  "reject added static files",
  async (root) => {
    await writeFile(path.join(root, "assets/new.txt"), "unreviewed");
  },
  /Unclassified public member/u,
);
rejects(
  "reject HTML with a changed inline script",
  async (root) => {
    await edit(root, "assets/index.html", (text) =>
      text.replace("<head>", "<head><script>window.changed=true;</script>"),
    );
  },
  /independently reviewed two-build oracle/u,
);
rejects(
  "reject a forged Router fingerprint",
  async (root) => {
    const file = (await files(root)).find((file) => /\/manifest-.*\.js$/u.test(file));
    assert(file);
    await edit(root, file, (text) => text.replace('"version":"', '"version":"0'));
  },
  /Router manifest fingerprint mismatch/u,
);
rejects(
  "reject a changed module graph",
  async (root) => {
    const graph = await json(path.join(root, "provenance/browser-graph.json"));
    graph.modules[0].imports = [];
    graph.modules[0].id = "foreign-source.js";
    await save(path.join(root, "provenance/browser-graph.json"), graph);
  },
  /Candidate graph differs/u,
);
rejects(
  "reject a substituted profile",
  async (root) => {
    const value = await json(path.join(root, "provenance/profile.json"));
    value.templates = {};
    await save(path.join(root, "provenance/profile.json"), value);
  },
  /Candidate substituted/u,
);
rejects(
  "reject a modified used record",
  async (root) => {
    const receipt = await json(path.join(root, "provenance/receipt.json"));
    const id = receipt.records.find((value: string) => value.startsWith("browser-resources-"));
    assert(id);
    const file = `provenance/records/${id}.json`;
    const value = await json(path.join(root, file));
    value.review.rationale = "forged";
    await save(path.join(root, file), value);
  },
  /Candidate record changed/u,
);
rejects(
  "reject omitted original legal terms",
  async (root) => {
    await edit(root, "assets/third-party-notices.txt", (text) =>
      text.replaceAll("Evan Wallace", "Removed Author"),
    );
  },
  /Required full third-party notices changed/u,
);
rejects(
  "reject reformatting of immutable candidate records",
  async (root) => {
    const receipt = await json(path.join(root, "provenance/receipt.json"));
    const id = receipt.records.find((value: string) => value.startsWith("browser-resources-"));
    assert(id);
    await edit(root, `provenance/records/${id}.json`, (text) => `${text}\n`);
  },
  /Candidate record bytes changed/u,
);
rejects(
  "reject omitted full AGPL legal body",
  async (root) => {
    await writeFile(path.join(root, "assets/license.txt"), "AGPL-3.0-only\n");
  },
  /Full AGPL legal text changed/u,
);
rejects(
  "reject weakened CSP after resealing",
  async (root) => {
    await edit(root, "assets/_headers", (text) =>
      text.replace("default-src 'self'", "default-src *"),
    );
  },
  /Security\/cache headers changed/u,
);
rejects(
  "reject CSP that omits exact scripts from localized prerenders",
  async (root) => {
    const pages = await Promise.all(
      ["assets/index.html", "assets/hello/index.html", "assets/cloud-hello/index.html"].map(
        (file) => readFile(path.join(root, file), "utf8"),
      ),
    );
    const csp = contentSecurityPolicy(pages);
    await edit(root, "assets/_headers", (text) =>
      text.replace(/^ {2}Content-Security-Policy:.*$/mu, `  Content-Security-Policy: ${csp}`),
    );
  },
  /Security\/cache headers changed/u,
);
rejects(
  "reject browser SBOM dependency-edge tampering",
  async (root) => {
    const value = await json(path.join(root, "runtime-sbom.cdx.json"));
    value.dependencies = [];
    await save(path.join(root, "runtime-sbom.cdx.json"), value);
  },
  /Emitted browser SBOM identities\/edges changed/u,
);
rejects(
  "reject build SBOM identity tampering",
  async (root) => {
    const value = await json(path.join(root, "sbom.cdx.json"));
    value.metadata.component.name = "foreign-checkout";
    await save(path.join(root, "sbom.cdx.json"), value);
  },
  /Build SBOM identities\/edges changed/u,
);
rejects(
  "reject changed Contracts source identity",
  async (root) => {
    const file = path.join(root, "contracts/proto/source.json");
    const value = await json(file);
    assert.match(value.commit, /^[a-f0-9]{40}$/u);
    value.commit = `${value.commit[0] === "0" ? "1" : "0"}${value.commit.slice(1)}`;
    await save(file, value);
  },
  /Published Contracts member changed/u,
);
rejects(
  "reject added candidate-private files",
  async (root) => {
    await writeFile(path.join(root, "private-extra.txt"), "unclassified");
  },
  /Unclassified candidate member/u,
);

rejects(
  "reject build metadata mutation even after both manifests are resealed",
  async (root) => {
    const file = path.join(root, "assets/__build-info.json");
    const value = await json(file);
    value.build.buildId = "another-run";
    await save(file, value);
  },
  /Built identity differs/u,
);
rejects(
  "reject axis mutation even after both manifests are resealed",
  async (root) => {
    const file = path.join(root, "assets/__build-info.json");
    const value = await json(file);
    value.axes.ContractSet.values[0].version = "999";
    await save(file, value);
  },
  /Built identity differs/u,
);
