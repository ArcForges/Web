// SPDX-License-Identifier: AGPL-3.0-only
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { afterEach, expect, test } from "vitest";
import {
  buildBundle,
  bundleName,
  headersFile,
  manifestName,
  mergeProfiles,
  profileCsp,
  readTar,
  verifyBundle,
  writeBundle,
  writeTar,
} from "../../apps/app/scripts/bundle.ts";
import { pageFile, servedFile } from "../../apps/app/scripts/measure.ts";

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});
const sha256 = (value: Uint8Array | string) => createHash("sha256").update(value).digest("hex");

function page(profile: string, script = `window.profile = "${profile}";`) {
  return `<!DOCTYPE html><html><head><link rel="modulepreload" href="/assets/entry.js"/></head><body><script>${script}</script><script type="module" src="/assets/entry.js"></script></body></html>`;
}

function client(profile: "account" | "chat", extra: Record<string, string> = {}) {
  const root = mkdtempSync(join(tmpdir(), "cloud71-"));
  roots.push(root);
  const files: Record<string, string> = {
    [pageFile(profile)]: page(profile),
    // The fallback shell the build writes at the root is never part of a bundle.
    "index.html": "<html></html>",
    "__spa-fallback.html": "<html></html>",
    ".vite/manifest.json": "{}",
    "favicon.svg": "<svg/>",
    "robots.txt": "User-agent: *\n",
    "assets/entry.js": "export const entry = 1;",
    "assets/root.css": "body{color:red}",
    [`assets/${profile}-1234.js`]: `export const own = "${profile}";`,
    ...extra,
  };
  for (const [name, content] of Object.entries(files)) {
    mkdirSync(dirname(join(root, name)), { recursive: true });
    writeFileSync(join(root, name), content);
  }
  return root;
}

const clients = () => ({ account: client("account"), chat: client("chat") });
const measure = (_client: string, profile: string) => ({ digest: sha256(`build ${profile}`) });

test("a request path maps to a served file and the root is not a page", () => {
  expect(servedFile("/")).toBeUndefined();
  expect(servedFile("/index.html")).toBeUndefined();
  expect(servedFile("/__spa-fallback.html")).toBeUndefined();
  expect(servedFile("/.vite/manifest.json")).toBeUndefined();
  expect(servedFile("/chat/")).toBe("chat/index.html");
  expect(servedFile("/account/")).toBe("account/index.html");
  expect(servedFile("/assets/entry.js")).toBe("assets/entry.js");
  expect(servedFile("/favicon.svg")).toBe("favicon.svg");
});

test("the merged tree holds each profile's page, one copy of the shared files and no fallback shell", () => {
  const merged = mergeProfiles(clients());
  expect([...merged.keys()].sort()).toEqual([
    "account/index.html",
    "assets/account-1234.js",
    "assets/chat-1234.js",
    "assets/entry.js",
    "assets/root.css",
    "chat/index.html",
    "favicon.svg",
    "robots.txt",
  ]);
});

test("a path that two builds fill with different bytes refuses the bundle", () => {
  expect(() =>
    mergeProfiles({
      account: client("account"),
      chat: client("chat", { "assets/entry.js": "export const entry = 2;" }),
    }),
  ).toThrow(/different bytes/u);
  expect(() =>
    mergeProfiles({
      account: client("account"),
      chat: client("chat", { "favicon.svg": "<svg>other</svg>" }),
    }),
  ).toThrow(/favicon\.svg/u);
});

test("a build without its own page or with a foreign page is refused", () => {
  const missing = client("chat");
  rmSync(join(missing, "chat/index.html"));
  expect(() => mergeProfiles({ account: client("account"), chat: missing })).toThrow(
    /chat has no chat\/index\.html/u,
  );
  expect(() =>
    mergeProfiles({
      account: client("account", { "chat/index.html": page("chat") }),
      chat: client("chat"),
    }),
  ).toThrow(/foreign path/u);
  expect(() =>
    mergeProfiles({
      account: client("account", { "account/extra.html": "<html></html>" }),
      chat: client("chat"),
    }),
  ).toThrow(/foreign path/u);
});

test("each profile's policy is derived from its own page and applies to its own path only", () => {
  const accountPolicy = profileCsp(page("account"));
  const chatPolicy = profileCsp(page("chat"));
  const hash = (script: string) =>
    `'sha256-${createHash("sha256").update(script).digest("base64")}'`;
  expect(accountPolicy).toContain(hash('window.profile = "account";'));
  expect(accountPolicy).not.toContain(hash('window.profile = "chat";'));
  expect(chatPolicy).toContain(hash('window.profile = "chat";'));
  expect(accountPolicy).toContain("connect-src 'self'");
  expect(accountPolicy).not.toContain("unsafe-inline");
  // The bytes a browser executes use LF; a CRLF page hashes to the same policy.
  expect(profileCsp(page("account").replaceAll(";<", ";\r\n<"))).toBe(
    profileCsp(page("account").replaceAll(";<", ";\n<")),
  );
  const headers = headersFile({ account: accountPolicy, chat: chatPolicy });
  const blocks = headers.split(/\n(?=\/)/u);
  const own = (path: string) => blocks.find((block) => block.startsWith(`${path}\n`)) ?? "";
  expect(own("/account/*")).toContain(`Content-Security-Policy: ${accountPolicy}`);
  expect(own("/account/*")).not.toContain(chatPolicy);
  expect(own("/chat/*")).toContain(`Content-Security-Policy: ${chatPolicy}`);
  expect(own("/chat/*")).toContain("X-Frame-Options: DENY");
  expect(own("/*")).not.toContain("Content-Security-Policy");
  expect(own("/*")).toContain("X-Content-Type-Options: nosniff");
  expect(own("/assets/*")).toContain("! Cache-Control");
  expect(own("/assets/*")).toContain("max-age=31536000, immutable");
  expect(headers.split("\n").every((line) => line.length < 2000)).toBe(true);
  expect(() => headersFile({ account: accountPolicy })).toThrow(/No policy for chat/u);
  expect(() => headersFile({ account: "x".repeat(1800), chat: chatPolicy })).toThrow(
    /header line budget/u,
  );
});

test("the archive is deterministic and reads back exactly what was written", () => {
  const entries = [
    { path: manifestName, bytes: Buffer.from("{}\n") },
    { path: "assets/a.js", bytes: Buffer.alloc(512, 7) },
    { path: "assets/b.js", bytes: Buffer.alloc(0) },
    { path: "z.txt", bytes: Buffer.from("tail") },
  ];
  const first = writeTar(entries);
  expect(sha256(writeTar(entries))).toBe(sha256(first));
  expect(first.length % 512).toBe(0);
  const back = readTar(first);
  expect(back.map((entry) => entry.path)).toEqual(entries.map((entry) => entry.path));
  for (const [index, entry] of back.entries())
    expect(Buffer.compare(Buffer.from(entry.bytes), entries[index]?.bytes ?? Buffer.alloc(0))).toBe(
      0,
    );
  // No timestamp, owner or mode varies: the first header carries zeros and the fixed mode.
  expect(first.subarray(100, 108).toString("ascii")).toBe("0000644\0");
  expect(first.subarray(136, 148).toString("ascii")).toBe("00000000000\0");
});

test("the archive writer and reader refuse unsafe paths, links, damage and trailing data", () => {
  for (const path of ["../x", "/abs", "a//b", "a/", "a b", "", "a/../b", "x".repeat(101)])
    expect(() => writeTar([{ path, bytes: Buffer.alloc(0) }]), path).toThrow();
  expect(() =>
    writeTar([
      { path: "a", bytes: Buffer.alloc(0) },
      { path: "a", bytes: Buffer.alloc(0) },
    ]),
  ).toThrow(/Duplicate/u);
  const good = writeTar([{ path: "a.txt", bytes: Buffer.from("hello") }]);
  const symlink = Buffer.from(good);
  symlink.write("2", 156, "ascii");
  expect(() => readTar(symlink)).toThrow(/plain regular file/u);
  const flipped = Buffer.from(good);
  flipped[0] = (flipped[0] ?? 0) ^ 1;
  expect(() => readTar(flipped)).toThrow();
  expect(() => readTar(good.subarray(0, good.length - 512))).toThrow();
  expect(() => readTar(Buffer.concat([good, Buffer.alloc(512, 1)]))).toThrow();
  expect(() => readTar(Buffer.concat([good, Buffer.alloc(512)]))).toThrow(/after the terminator/u);
  expect(() => readTar(Buffer.alloc(10))).toThrow(/Not a bundle/u);
});

test("equal content is the same bundle and its name is the digest of its own bytes", () => {
  const first = buildBundle(clients(), measure);
  const second = buildBundle(clients(), measure);
  expect(second.digest).toBe(first.digest);
  expect(first.digest).toBe(sha256(first.archive));
  expect(first.name).toBe(bundleName(first.digest));
  expect(first.name).toMatch(/^web-profiles-[0-9a-f]{64}\.tar$/u);
  expect(Object.keys(first.manifest.files)).toEqual(Object.keys(first.manifest.files).sort());
  expect(first.manifest.files._headers?.sha256).toBe(
    sha256(readTar(first.archive).find((entry) => entry.path === "_headers")?.bytes ?? ""),
  );
  expect(first.manifest.profiles.account?.page).toBe("account/index.html");
  expect(first.manifest.profiles.chat?.csp).toBe(profileCsp(page("chat")));
  expect(readTar(first.archive)[0]?.path).toBe(manifestName);
  const changed = buildBundle(
    {
      account: client("account", { "assets/root.css": "body{color:blue}" }),
      chat: client("chat", { "assets/root.css": "body{color:blue}" }),
    },
    measure,
  );
  expect(changed.digest).not.toBe(first.digest);
  const otherBuild = buildBundle(clients(), (_client, profile) => ({
    digest: sha256(`x ${profile}`),
  }));
  expect(otherBuild.digest).not.toBe(first.digest);
});

test("verification checks the digest, the exact file set and every file against the manifest", () => {
  const bundle = buildBundle(clients(), measure);
  expect(verifyBundle(bundle.archive, bundle.digest).schema).toBe(1);
  expect(() => verifyBundle(bundle.archive, sha256("another"))).toThrow(/digest mismatch/u);
  // A content change that keeps the size, with the new digest expected, is still caught by the manifest.
  const entries = readTar(bundle.archive).map((entry) => ({
    path: entry.path,
    bytes: Buffer.from(entry.bytes),
  }));
  const target = entries.find((entry) => entry.path === "assets/entry.js");
  expect(target).toBeDefined();
  if (!target) return;
  target.bytes[0] = (target.bytes[0] ?? 0) ^ 1;
  const tampered = writeTar(entries);
  expect(() => verifyBundle(tampered, sha256(tampered))).toThrow(/Content of assets\/entry\.js/u);
  const extra = writeTar([...entries, { path: "assets/zz.js", bytes: Buffer.from("x") }]);
  expect(() => verifyBundle(extra, sha256(extra))).toThrow(/exactly the manifest's files/u);
  const reordered = writeTar([
    entries[1] as (typeof entries)[0],
    entries[0] as (typeof entries)[0],
  ]);
  expect(() => verifyBundle(reordered, sha256(reordered))).toThrow(/manifest must be the first/u);
});

test("writing a bundle leaves exactly one digest-named archive in the output directory", () => {
  const out = mkdtempSync(join(tmpdir(), "cloud71-out-"));
  roots.push(out);
  writeFileSync(join(out, `web-profiles-${"0".repeat(64)}.tar`), "stale");
  writeFileSync(join(out, "unrelated.txt"), "kept");
  const written = writeBundle(clients(), out, measure);
  expect(readdirSync(out).sort()).toEqual([written.name, "unrelated.txt"].sort());
  expect(sha256(readFileSync(join(out, written.name)))).toBe(written.digest);
});
