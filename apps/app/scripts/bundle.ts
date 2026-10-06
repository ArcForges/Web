// SPDX-License-Identifier: AGPL-3.0-only
// The profile bundle: the two built production profiles (Account and Chat) as ONE immutable, deterministic archive,
// named by its own SHA-256, for the Cloud proof deployment job to serve as static assets on the proof origin.
//   node scripts/bundle.ts [outputDirectory]       (after the profile builds; `npm run build:profiles` runs it)
//
// Nothing is rebuilt and nothing is rewritten here except what the candidate build also does: the served bytes are
// the bytes of the two builds (a Windows build's CRLF page is normalised to the LF bytes a browser executes, so the
// Content-Security-Policy hashes match them). The two builds share the content-hashed `assets/` directory and the
// root files; a path with two different contents refuses the bundle. The archive is plain POSIX ustar with a fixed
// order, zero times and owners and no compression, so equal content is the same bytes and the same name.
//
// Layout: manifest.json (first), `_headers`, the root files, `assets/**`, `account/index.html`, `chat/index.html`.
// `_headers` carries each profile's Content-Security-Policy on its own path (derived from that page's inline scripts
// exactly as the site candidate derives its policy), the security headers, and the immutable cache of `assets/**`.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { contentSecurityPolicy } from "../../../tooling/project.ts";
import { type Profile, profiles } from "../profile.ts";
import { listFiles, measureProfile, notServed, pageFile } from "./measure.ts";

const app = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repository = resolve(app, "../..");

export const bundleSchema = 1;
export const manifestName = "manifest.json";
export const headersName = "_headers";
/** Release assets are named by the digest of their own bytes. */
export const bundleName = (digest: string) => `web-profiles-${digest}.tar`;

const sha256 = (value: Uint8Array) => createHash("sha256").update(value).digest("hex");

export interface BundleManifest {
  schema: typeof bundleSchema;
  /** Per profile: the page, the measured content digest of its own build and the policy applied to its path. */
  profiles: Record<string, { page: string; buildDigest: string; csp: string }>;
  /** Every other file of the archive with its SHA-256 and size. */
  files: Record<string, { sha256: string; bytes: number }>;
}

/** The policy of one profile page: the site candidate's derivation over that page alone. */
export function profileCsp(page: string): string {
  return contentSecurityPolicy([page.replaceAll("\r\n", "\n")]);
}

/** `_headers` for the merged tree. Each profile's policy is applied to its own path only. */
export function headersFile(policies: Record<string, string>): string {
  const lines = [
    "/*",
    "  X-Content-Type-Options: nosniff",
    "  Referrer-Policy: no-referrer",
    "  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()",
    "  X-Robots-Tag: noindex, nofollow",
  ];
  for (const profile of profiles) {
    const policy = policies[profile];
    assert(policy, `No policy for ${profile}`);
    // A Cloudflare _headers line is at most 2000 characters.
    assert(policy.length < 1800, `The ${profile} policy exceeds the header line budget`);
    lines.push(
      `/${profile}/*`,
      `  Content-Security-Policy: ${policy}`,
      "  X-Frame-Options: DENY",
      "  Cache-Control: public, no-cache, no-transform",
    );
  }
  lines.push(
    "/assets/*",
    "  ! Cache-Control",
    "  Cache-Control: public, max-age=31536000, immutable, no-transform",
  );
  return `${lines.join("\n")}\n`;
}

export interface Entry {
  path: string;
  bytes: Uint8Array;
}

/** The merged served tree of the profile builds, refusing any path that two builds fill differently. */
export function mergeProfiles(clients: Record<Profile, string>): Map<string, Uint8Array> {
  const merged = new Map<string, Uint8Array>();
  for (const profile of profiles) {
    const client = clients[profile];
    assert(client, `No build directory for ${profile}`);
    const served = listFiles(client).filter((file) => !notServed(file));
    assert(served.includes(pageFile(profile)), `${profile} has no ${pageFile(profile)}`);
    for (const file of served) {
      // Only a profile's own page lives under its path; every other profile's page would be a foreign file.
      const owner = profiles.find((name) => file.startsWith(`${name}/`));
      assert(
        owner === undefined || (owner === profile && file === pageFile(profile)),
        `${profile} build has a foreign path: ${file}`,
      );
      let bytes: Uint8Array = readFileSync(join(client, file));
      if (file === pageFile(profile))
        bytes = Buffer.from(Buffer.from(bytes).toString("utf8").replaceAll("\r\n", "\n"));
      const existing = merged.get(file);
      if (existing !== undefined)
        assert(
          Buffer.compare(Buffer.from(existing), Buffer.from(bytes)) === 0,
          `Two builds fill ${file} with different bytes`,
        );
      else merged.set(file, bytes);
    }
  }
  return merged;
}

const block = 512;

function octal(value: number, length: number): string {
  const text = value.toString(8);
  assert(text.length < length, "Value does not fit its tar field");
  return `${text.padStart(length - 1, "0")}\0`;
}

function header(path: string, size: number): Buffer {
  assert(/^[A-Za-z0-9_][A-Za-z0-9._/-]*$/u.test(path) && !path.includes(".."), `Bad path ${path}`);
  assert(!path.includes("//") && !path.endsWith("/"), `Bad path ${path}`);
  const name = Buffer.from(path, "ascii");
  assert(name.length <= 100, `Path too long for a plain tar header: ${path}`);
  const head = Buffer.alloc(block);
  name.copy(head, 0);
  head.write(octal(0o644, 8), 100, "ascii");
  head.write(octal(0, 8), 108, "ascii");
  head.write(octal(0, 8), 116, "ascii");
  head.write(octal(size, 12), 124, "ascii");
  head.write(octal(0, 12), 136, "ascii");
  head.fill(0x20, 148, 156);
  head.write("0", 156, "ascii");
  head.write("ustar\0", 257, "ascii");
  head.write("00", 263, "ascii");
  let sum = 0;
  for (const byte of head) sum += byte;
  head.write(`${sum.toString(8).padStart(6, "0")}\0 `, 148, "ascii");
  return head;
}

/** Deterministic archive of the entries in the given order. */
export function writeTar(entries: Entry[]): Buffer {
  const seen = new Set<string>();
  const parts: Buffer[] = [];
  for (const entry of entries) {
    assert(!seen.has(entry.path), `Duplicate entry ${entry.path}`);
    seen.add(entry.path);
    parts.push(header(entry.path, entry.bytes.byteLength), Buffer.from(entry.bytes));
    const pad = (block - (entry.bytes.byteLength % block)) % block;
    if (pad > 0) parts.push(Buffer.alloc(pad));
  }
  parts.push(Buffer.alloc(block * 2));
  return Buffer.concat(parts);
}

/** Strict reader of exactly what `writeTar` writes; anything else is refused. */
export function readTar(archive: Uint8Array): Entry[] {
  const data = Buffer.from(archive);
  assert(data.length % block === 0 && data.length >= block * 2, "Not a bundle archive");
  const entries: Entry[] = [];
  let offset = 0;
  for (;;) {
    assert(offset + block <= data.length, "Archive ends without its terminator");
    const head = data.subarray(offset, offset + block);
    if (head.every((byte) => byte === 0)) {
      assert(offset + block * 2 === data.length, "Data after the terminator");
      assert(data.subarray(offset + block, offset + block * 2).every((byte) => byte === 0));
      return entries;
    }
    const end = head.indexOf(0);
    const path = head.subarray(0, end < 0 || end > 100 ? 100 : end).toString("ascii");
    const size = Number.parseInt(head.subarray(124, 135).toString("ascii"), 8);
    assert(Number.isSafeInteger(size) && size >= 0, "Bad entry size");
    const expected = header(path, size);
    assert(
      Buffer.compare(head, expected) === 0,
      `Entry ${path} is not a plain regular file header`,
    );
    const start = offset + block;
    assert(start + size <= data.length, `Entry ${path} is truncated`);
    entries.push({ path, bytes: data.subarray(start, start + size) });
    offset = start + Math.ceil(size / block) * block;
  }
}

export interface Bundle {
  digest: string;
  name: string;
  archive: Buffer;
  manifest: BundleManifest;
}

/** The bundle of the merged tree and the per-profile build measurements. */
export function buildBundle(
  clients: Record<Profile, string>,
  measure: (client: string, profile: Profile) => { digest: string } = (client, profile) =>
    measureProfile(client, profile),
): Bundle {
  const merged = mergeProfiles(clients);
  const csp: Record<string, string> = {};
  const summary: BundleManifest["profiles"] = {};
  for (const profile of profiles) {
    const page = merged.get(pageFile(profile));
    assert(page);
    csp[profile] = profileCsp(Buffer.from(page).toString("utf8"));
    summary[profile] = {
      page: pageFile(profile),
      buildDigest: measure(clients[profile], profile).digest,
      csp: csp[profile] as string,
    };
  }
  const tree = new Map(merged);
  assert(!tree.has(headersName) && !tree.has(manifestName), "A build ships a reserved name");
  tree.set(headersName, Buffer.from(headersFile(csp), "utf8"));
  const files: BundleManifest["files"] = {};
  const sorted = [...tree.keys()].sort();
  for (const path of sorted) {
    const bytes = tree.get(path) as Uint8Array;
    files[path] = { sha256: sha256(bytes), bytes: bytes.byteLength };
  }
  const manifest: BundleManifest = { schema: bundleSchema, profiles: summary, files };
  const entries: Entry[] = [
    { path: manifestName, bytes: Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8") },
    ...sorted.map((path) => ({ path, bytes: tree.get(path) as Uint8Array })),
  ];
  const archive = writeTar(entries);
  const digest = sha256(archive);
  return { digest, name: bundleName(digest), archive, manifest };
}

/** Reads a bundle back and checks it against its manifest and its name: what the Cloud job also checks. */
export function verifyBundle(archive: Uint8Array, expectedDigest?: string): BundleManifest {
  const digest = sha256(archive);
  if (expectedDigest !== undefined) assert.equal(digest, expectedDigest, "Bundle digest mismatch");
  const entries = readTar(archive);
  const [first, ...rest] = entries;
  assert(first?.path === manifestName, "The manifest must be the first entry");
  const manifest = JSON.parse(Buffer.from(first.bytes).toString("utf8")) as BundleManifest;
  assert.equal(manifest.schema, bundleSchema);
  assert.deepEqual(
    rest.map((entry) => entry.path),
    Object.keys(manifest.files).sort(),
    "The archive does not hold exactly the manifest's files in order",
  );
  for (const entry of rest) {
    const row = manifest.files[entry.path];
    assert(row, `Unlisted file ${entry.path}`);
    assert.equal(row.bytes, entry.bytes.byteLength, `Size of ${entry.path}`);
    assert.equal(row.sha256, sha256(entry.bytes), `Content of ${entry.path}`);
  }
  return manifest;
}

export function writeBundle(
  clients: Record<Profile, string>,
  outputDirectory: string,
  measure?: (client: string, profile: Profile) => { digest: string },
): Bundle {
  const bundle = buildBundle(clients, measure);
  verifyBundle(bundle.archive, bundle.digest);
  mkdirSync(outputDirectory, { recursive: true });
  // Only this task's own earlier bundles are removed, so the directory always holds exactly one.
  for (const name of readdirSync(outputDirectory))
    if (/^web-profiles-[0-9a-f]{64}\.tar$/u.test(name))
      rmSync(join(outputDirectory, name), { force: true });
  writeFileSync(join(outputDirectory, bundle.name), bundle.archive);
  return bundle;
}

export const defaultClients = (): Record<Profile, string> => ({
  account: join(app, "build", "account", "client"),
  chat: join(app, "build", "chat", "client"),
});
export const defaultOutput = () => join(repository, "artifacts", "profiles");

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const bundle = writeBundle(defaultClients(), resolve(process.argv[2] ?? defaultOutput()));
  console.log(
    `Profile bundle ${bundle.name}: ${Object.keys(bundle.manifest.files).length} files, ${bundle.archive.byteLength} bytes`,
  );
}
