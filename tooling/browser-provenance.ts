// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import type { Plugin } from "vite";
import { auditProvenance, hash, parseDocument, readOwned, relative, store } from "./provenance.ts";

const owner = path.resolve(import.meta.dirname, "..");
export const profilePath = "eng/provenance/profiles/browser-resources-r1.json";
const sha = (value: string | Uint8Array) => createHash("sha256").update(value).digest("hex");
const lf = (value: Buffer) =>
  new TextDecoder("utf-8", { fatal: true }).decode(value).replaceAll("\r\n", "\n");
const json = <T>(root: string, file: string) => parseDocument(readOwned(root, file)) as T;
const write = (root: string, file: string, value: unknown) => {
  relative(file);
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
  writeFileSync(path.join(root, file), `${JSON.stringify(value, null, 2)}\n`);
};
export function members(root: string, prefix = ""): string[] {
  return readdirSync(path.join(root, prefix), { withFileTypes: true })
    .flatMap((entry) => {
      assert(!entry.isSymbolicLink(), "Linked artifact member");
      const file = `${prefix}${entry.name}`;
      if (entry.isDirectory()) return members(root, `${file}/`);
      assert(entry.isFile(), "Nonregular artifact member");
      return [relative(file)];
    })
    .sort();
}
const logical = (name: string) => name.replace(/-[A-Za-z0-9_-]{8}(?=\.(?:js|css)$)/u, "");
interface Module {
  id: string;
  imports: string[];
  dynamicImports: string[];
  sourceSha256: string | null;
  helperSha256: string | null;
}
interface Chunk {
  name: string;
  modules: string[];
  imports: string[];
  dynamicImports: string[];
  exports: string[];
}
interface Graph {
  modules: Module[];
  chunks: Chunk[];
  assets: string[];
}
interface Template {
  sha256: string;
  revisions: number;
  routerVersions: number;
  assetReferences: number;
}
interface Component {
  name: string;
  version: string;
  spdx: string;
  repository: string;
  commit: string;
  packagePath: string | null;
  emitted: boolean;
  role: string;
}
interface Profile {
  schemaVersion: 1;
  repository: "Web";
  graph: Graph;
  templates: Record<string, Template>;
  inputs: Record<string, string>;
  contracts: Record<string, string>;
  packageIdentities: Record<string, { version: string; integrity: string; resolved: string }>;
  noticeInputs: Record<string, string>;
  additionalNotices: string[];
  components: Component[];
  buildSbomSha256: string;
  runtimeSbom: unknown;
  evidence: unknown;
}
export function profile(root = owner): Profile {
  const value = json<Profile>(root, profilePath);
  assert.equal(value.schemaVersion, 1);
  assert.equal(value.repository, "Web");
  return value;
}

// Observe the actual client compilation, without changing resolution or output bytes.
export function observeBrowser(): Plugin {
  const clean = (id: string) =>
    id.replaceAll("\\", "/").replaceAll(`${owner.replaceAll("\\", "/")}/`, "");
  return {
    name: "arcforges-browser-provenance",
    enforce: "post",
    generateBundle(_options, bundle) {
      if (this.environment.name !== "client") return;
      const modules = [...this.getModuleIds()]
        .map((id): Module => {
          const info = this.getModuleInfo(id);
          assert(info, "Missing browser module information");
          const file = id.split("?")[0];
          assert(file);
          const physical = existsSync(file);
          const source = physical ? lf(readFileSync(file)) : null;
          return {
            id: clean(id),
            imports: info.importedIds.map(clean).sort(),
            dynamicImports: info.dynamicallyImportedIds.map(clean).sort(),
            sourceSha256: source === null ? null : sha(source),
            helperSha256: physical ? null : sha(clean(info.code ?? "")),
          };
        })
        .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
      const chunks: Chunk[] = [];
      const assets: string[] = [];
      for (const item of Object.values(bundle)) {
        if (item.type === "asset") assets.push(logical(item.fileName));
        else
          chunks.push({
            name: logical(item.fileName),
            modules: Object.keys(item.modules).map(clean).sort(),
            imports: item.imports.map(logical).sort(),
            dynamicImports: item.dynamicImports.map(logical).sort(),
            exports: [...item.exports].sort(),
          });
      }
      chunks.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
      const graph = { modules, chunks, assets: assets.sort() };
      assert.deepEqual(
        graph,
        profile().graph,
        "Actual browser module/resource graph changed; review a new profile",
      );
      write(owner, "artifacts/evidence/browser-graph.json", graph);
    },
  };
}

export function browserTemplates(root: string, revision: string): Record<string, Template> {
  assert(/^[a-f0-9]{40}$/u.test(revision), "Invalid browser source identity");
  const all = members(root);
  const aliases = new Map<string, string>();
  for (const file of all.filter((file) => file.startsWith("assets/"))) {
    assert(
      /^assets\/[^/]+-[A-Za-z0-9_-]{8}\.(?:js|css)$/u.test(file),
      "Unclassified compiled browser resource",
    );
    const name = logical(path.posix.basename(file));
    assert(!aliases.has(name), "Duplicate logical browser resource");
    aliases.set(name, path.posix.basename(file));
  }
  const manifestName = aliases.get("manifest.js");
  assert(manifestName, "Missing Router manifest");
  const manifestText = lf(readOwned(root, `assets/${manifestName}`));
  assert(
    manifestText.startsWith("window.__reactRouterManifest=") && manifestText.endsWith(";"),
    "Changed Router manifest wrapper",
  );
  const manifest = parseDocument(
    manifestText.slice("window.__reactRouterManifest=".length, -1),
  ) as { entry: unknown; routes: unknown; version: string };
  const version = sha(JSON.stringify({ entry: manifest.entry, routes: manifest.routes })).slice(
    0,
    8,
  );
  assert.equal(manifest.version, version, "Router manifest fingerprint mismatch");
  assert.equal(manifestName, `manifest-${version}.js`);
  const expected = profile().templates;
  for (const name of aliases.keys())
    assert(Object.hasOwn(expected, `assets/${name}`), "Unclassified compiled browser resource");
  const result: Record<string, Template> = {};
  for (const file of all) {
    const key = file.startsWith("assets/") ? `assets/${logical(path.posix.basename(file))}` : file;
    if (!Object.hasOwn(expected, key)) continue; // The complete candidate member gate checks all remaining packaging outputs.
    let value = lf(readOwned(root, file));
    assert(
      !/<asset:|<revision>|<router-version>/u.test(value),
      "Reserved template marker in input",
    );
    for (const [name, actual] of [...aliases].sort((a, b) => b[1].length - a[1].length))
      value = value.replaceAll(actual, `<asset:${name}>`);
    value = value.replaceAll(revision, "<revision>").replaceAll(version, "<router-version>");
    result[key] = {
      sha256: sha(value),
      revisions: value.split("<revision>").length - 1,
      routerVersions: value.split("<router-version>").length - 1,
      assetReferences: value.split("<asset:").length - 1,
    };
  }
  assert.deepEqual(
    result,
    expected,
    "Generated browser bytes differ from the independently reviewed two-build oracle",
  );
  return result;
}

export function verifyBrowserInputs(root = owner) {
  const approved = profile(root);
  for (const [file, digest] of Object.entries({ ...approved.inputs, ...approved.noticeInputs }))
    assert.equal(hash(readOwned(root, file), "lf"), digest, `Changed browser input: ${file}`);
  const lock = json<{
    packages: Record<string, { version: string; integrity: string; resolved: string }>;
  }>(root, "package-lock.json");
  for (const [file, expected] of Object.entries(approved.packageIdentities)) {
    const actual = lock.packages[file];
    assert(actual, `Missing reviewed package: ${file}`);
    assert.deepEqual(
      { version: actual.version, integrity: actual.integrity, resolved: actual.resolved },
      expected,
      `Package identity changed: ${file}`,
    );
  }
  return approved;
}
export function extraNotices(root = owner): string {
  const approved = verifyBrowserInputs(root);
  let result = "\n===== Additional generated browser resources and embedded origins =====\n";
  for (const file of approved.additionalNotices)
    result += `\n${file}\n${lf(readOwned(root, file))}\n`;
  return result;
}

interface Bom {
  serialNumber?: string;
  metadata: { timestamp?: string; component: { name: string } };
}
export function canonicalBuildSbom(value: unknown): unknown {
  const copy = structuredClone(value) as Bom;
  delete copy.serialNumber;
  delete copy.metadata.timestamp;
  copy.metadata.component.name = "@arcforges/web-workspace";
  return copy;
}
export function canonicalJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value !== null && typeof value === "object")
    return `{${Object.entries(value)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([key, item]) => `${JSON.stringify(key)}:${canonicalJson(item)}`)
      .join(",")}}`;
  return JSON.stringify(value);
}
export function runtimeSbom() {
  return profile().runtimeSbom;
}

interface Identity {
  source: string;
  version: string;
}
function publicNames(root: string) {
  return members(path.join(root, "assets"));
}
function receipt(root: string, identity: Identity, active: string[]) {
  return {
    schemaVersion: 1,
    repository: "Web",
    source: identity.source,
    version: identity.version,
    profile: profilePath,
    profileSha256: hash(readOwned(owner, profilePath), "lf"),
    records: active,
    parsedModules: profile().graph.modules.length,
    emittedModules: new Set(profile().graph.chunks.flatMap((chunk) => chunk.modules)).size,
    members: Object.fromEntries(
      members(root)
        .filter((file) => !["manifest.json", "provenance/receipt.json"].includes(file))
        .map((file) => [file, hash(readOwned(root, file))]),
    ),
  };
}
export function prepareProvenance(root: string, identity: Identity) {
  const audit = auditProvenance(owner);
  const graph = json<Graph>(owner, "artifacts/evidence/browser-graph.json");
  assert.deepEqual(graph, profile().graph);
  write(root, "provenance/browser-graph.json", graph);
  writeFileSync(path.join(root, "provenance/profile.json"), lf(readOwned(owner, profilePath)));
  mkdirSync(path.join(root, "provenance/records"), { recursive: true });
  for (const id of audit.activeRecords)
    writeFileSync(
      path.join(root, `provenance/records/${id}.json`),
      lf(readOwned(owner, `${store}${id}.json`)),
    );
  writeFileSync(
    path.join(root, "assets/source-provenance.txt"),
    readOwned(owner, "eng/provenance/NOTICE.txt"),
  );
  write(root, "provenance/receipt.json", receipt(root, identity, audit.activeRecords));
}
export function verifyBrowserCandidate(
  root: string,
  identity: Identity,
  notices: string,
  headers: string,
) {
  const audit = auditProvenance(owner);
  const approved = verifyBrowserInputs();
  assert.deepEqual(
    json(root, "provenance/profile.json"),
    approved,
    "Candidate substituted the reviewed profile",
  );
  assert.equal(
    hash(readOwned(root, "provenance/profile.json"), "lf"),
    hash(readOwned(owner, profilePath), "lf"),
    "Candidate profile bytes changed",
  );
  assert.deepEqual(
    json(root, "provenance/browser-graph.json"),
    approved.graph,
    "Candidate graph differs from actual reviewed inputs",
  );
  for (const id of audit.activeRecords) {
    assert.deepEqual(
      json(root, `provenance/records/${id}.json`),
      json(owner, `${store}${id}.json`),
      "Candidate record changed",
    );
    assert.equal(
      hash(readOwned(root, `provenance/records/${id}.json`), "lf"),
      hash(readOwned(owner, `${store}${id}.json`), "lf"),
      "Candidate record bytes changed",
    );
  }
  browserTemplates(path.join(root, "assets"), identity.source);
  const expectedPublic = [
    ...Object.keys(approved.templates).filter((name) => !name.startsWith("assets/")),
    ...publicNames(root)
      .filter((name) => name.startsWith("assets/"))
      .map(relative),
    "_headers",
    "__build.json",
    "license.txt",
    "third-party-notices.txt",
    "source-provenance.txt",
  ].sort();
  assert.deepEqual(publicNames(root), expectedPublic, "Unclassified public member");
  for (const [file, expected] of Object.entries(approved.contracts))
    assert.equal(
      hash(readOwned(root, file)),
      expected,
      `Published Contracts member changed: ${file}`,
    );
  assert.equal(
    lf(readOwned(root, "assets/license.txt")),
    lf(readOwned(owner, "LICENSE")),
    "Full AGPL legal text changed",
  );
  assert.equal(
    lf(readOwned(root, "assets/third-party-notices.txt")),
    notices.replaceAll("\r\n", "\n"),
    "Required full third-party notices changed",
  );
  assert.equal(
    lf(readOwned(root, "assets/source-provenance.txt")),
    lf(readOwned(owner, "eng/provenance/NOTICE.txt")),
    "Source notice changed",
  );
  assert.equal(lf(readOwned(root, "assets/_headers")), headers, "Security/cache headers changed");
  assert.equal(
    sha(canonicalJson(json(root, "sbom.cdx.json"))),
    approved.buildSbomSha256,
    "Build SBOM identities/edges changed",
  );
  assert.deepEqual(
    json(root, "runtime-sbom.cdx.json"),
    approved.runtimeSbom,
    "Emitted browser SBOM identities/edges changed",
  );
  const expectedFiles = [
    ...expectedPublic.map((name) => `assets/${name}`),
    ...Object.keys(approved.contracts),
    ...audit.activeRecords.map((id) => `provenance/records/${id}.json`),
    "provenance/profile.json",
    "provenance/browser-graph.json",
    "provenance/receipt.json",
    "sbom.cdx.json",
    "runtime-sbom.cdx.json",
    "wrangler.json",
    "manifest.json",
  ].sort();
  assert.deepEqual(members(root), expectedFiles, "Unclassified candidate member");
  assert.deepEqual(
    json(root, "provenance/receipt.json"),
    receipt(root, identity, audit.activeRecords),
    "Source-bound provenance receipt changed",
  );
}
