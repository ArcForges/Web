// SPDX-License-Identifier: AGPL-3.0-only
// Local static counterpart of the real Center browser fixture. It serves the verified Blazor publish under its
// dashboard paths. The outer, same-origin proxy owns deployment configuration and forwards /api to the actual Center.
import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { once } from "node:events";
import { constants } from "node:fs";
import { lstat, open, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { basename, extname, resolve } from "node:path";
import { parseArgs, promisify } from "node:util";
import { toolLibrary } from "../../tooling/candidate.ts";

const maxArchiveBytes = 24 * 1024 * 1024;
const maxMembers = 400;
const block = 512;
const revalidate = "public, no-cache, no-transform";
const immutable = "public, max-age=31536000, immutable, no-transform";
const loaders = new Set(["_framework/blazor.webassembly.js", "_framework/dotnet.js"]);
const rootAssets = new Set(["app.css", "favicon.svg", "robots.txt"]);
const rootDocuments = new Set(["/overview", "/devices", "/security", "/account", "/account/subscription"]);
const fingerprinted = /^_framework\/[A-Za-z0-9_][A-Za-z0-9._-]*\.[a-z0-9]{10}\.(?:js|wasm|dat)$/u;
const routeId = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";
const deviceDocument = new RegExp(
  `^/devices/${routeId}(?:/resources|/(?:sandboxes|runs)(?:/${routeId})?)?$`,
  "u",
);
const contentTypes: Record<string, string> = {
  ".css": "text/css; charset=utf-8",
  ".dat": "application/octet-stream",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".txt": "text/plain; charset=utf-8",
  ".wasm": "application/wasm",
};

const sha = (bytes: Uint8Array) => createHash("sha256").update(bytes).digest("hex");

interface ProfileManifest {
  profiles: Record<string, { page: string; csp: string }>;
  files: Record<string, { sha256: string; bytes: number }>;
}

interface Asset {
  bytes: Buffer;
  headers: Record<string, string>;
}

interface StaticReply {
  status: number;
  headers: Record<string, string>;
  body?: Buffer;
}

async function snapshot(archive: string): Promise<Buffer> {
  const expected = await lstat(archive, { bigint: true });
  assert(expected.isFile(), "The profile archive must be a regular file.");
  const flags =
    process.platform === "win32"
      ? constants.O_RDONLY
      : constants.O_RDONLY | constants.O_NONBLOCK | constants.O_NOFOLLOW;
  const input = await open(archive, flags);
  try {
    const stat = await input.stat({ bigint: true });
    assert(
      stat.isFile() && stat.dev === expected.dev && stat.ino === expected.ino && stat.size === expected.size,
      "The profile archive changed while opening.",
    );
    assert(stat.size <= BigInt(maxArchiveBytes), "The profile archive exceeds the byte limit.");
    const size = Number(stat.size);
    const bytes = Buffer.alloc(size + 1);
    let offset = 0;
    while (offset < bytes.length) {
      const part = await input.read(bytes, offset, bytes.length - offset, offset);
      if (part.bytesRead === 0) break;
      offset += part.bytesRead;
    }
    assert.equal(offset, size, "The profile archive changed while reading.");
    return bytes.subarray(0, size);
  } finally {
    await input.close();
  }
}

/** Array views into a C#-verified archive. The producer's verifier owns canonical headers and manifest integrity. */
function members(archive: Buffer): Map<string, Buffer> {
  const result = new Map<string, Buffer>();
  for (let offset = 0; archive[offset] !== 0; ) {
    const head = archive.subarray(offset, offset + block);
    assert(head.length === block && result.size < maxMembers, "Too many profile archive members.");
    const name = head.toString("ascii", 0, 100).split("\0", 1)[0] ?? "";
    const size = Number.parseInt(head.toString("ascii", 124, 135), 8);
    const start = offset + block;
    assert(Number.isSafeInteger(size) && size >= 0 && start + size <= archive.length);
    assert(!result.has(name), "Duplicate verified archive member.");
    result.set(name, archive.subarray(start, start + size));
    offset = start + Math.ceil(size / block) * block;
  }
  return result;
}

/** Builds a deny-by-default route function from the exact supplied, already compiled App. */
export async function dashboardAssets(archive: string, expectedDigest: string) {
  const bundle = resolve(archive);
  assert.match(expectedDigest, /^[0-9a-f]{64}$/u, "The approved pin must be a lowercase SHA-256.");
  assert.equal(basename(bundle), "web-profiles-" + expectedDigest + ".tar", "Unexpected profile archive name.");
  const image = await snapshot(bundle);
  assert.equal(sha(image), expectedDigest, "The archive digest differs from the approved pin.");
  await promisify(execFile)(
    "dotnet",
    [toolLibrary, "profiles", "verify", "--bundle", bundle, "--expected-digest", expectedDigest],
    { timeout: 30000, maxBuffer: 65536, windowsHide: true },
  );
  const files = members(image);
  const metadata = files.get("manifest.json");
  assert(metadata, "The profile manifest is missing.");
  const manifest = JSON.parse(metadata.toString("utf8")) as ProfileManifest;
  const profile = manifest.profiles.account;
  const chat = manifest.profiles.chat;
  assert(profile?.page === "account/index.html" && chat?.page === "chat/index.html");

  const makeAsset = (name: string, cache: string): Asset => {
    const bytes = files.get(name);
    const row = manifest.files[name];
    const mime = contentTypes[extname(name)];
    assert(bytes && row && mime && bytes.length === row.bytes, "Missing verified resource " + name);
    return {
      bytes,
      headers: {
        "Cache-Control": cache,
        "Content-Length": String(bytes.length),
        "Content-Type": mime,
        ETag: '"' + row.sha256 + '"',
        "Permissions-Policy": "camera=(), microphone=(), geolocation=(), payment=()",
        "Referrer-Policy": "no-referrer",
        "X-Content-Type-Options": "nosniff",
        "X-Robots-Tag": "noindex, nofollow",
      },
    };
  };
  const document = makeAsset(profile.page, revalidate);
  assert(document.bytes.equals(makeAsset(chat.page, revalidate).bytes), "The profiles need one compiled shell.");
  document.headers["Content-Security-Policy"] = profile.csp;
  document.headers["X-Frame-Options"] = "DENY";

  const assets = new Map<string, Asset>();
  for (const name of files.keys()) {
    if (["manifest.json", "_headers", profile.page, chat.page].includes(name)) continue;
    const plain: string = name.replace(/\.(?:br|gz)$/u, "");
    assert(
      rootAssets.has(plain) || loaders.has(plain) || fingerprinted.test(plain),
      "Unexpected published Dashboard resource " + name,
    );
    assert(files.has(plain), "Missing uncompressed resource " + plain);
    if (name === plain)
      assets.set("/" + name, makeAsset(name, fingerprinted.test(name) ? immutable : revalidate));
  }
  for (const required of ["app.css", ...loaders]) assert(assets.has("/" + required));
  assert([...assets.keys()].some((name) => name.endsWith(".wasm")), "No compiled WebAssembly resource.");

  return (method: string, target: string, ifNoneMatch?: string): StaticReply => {
    const pathname = target.split("?", 1)[0] ?? "";
    const asset =
      rootDocuments.has(pathname) || deviceDocument.test(pathname)
        ? document
        : assets.get(pathname);
    if (!asset || (method !== "GET" && method !== "HEAD"))
      return { status: 404, headers: { "Cache-Control": "no-store" } };
    if (ifNoneMatch === asset.headers.ETag) {
      const headers = { ...asset.headers };
      delete headers["Content-Length"];
      return { status: 304, headers };
    }
    return method === "HEAD"
      ? { status: 200, headers: asset.headers }
      : { status: 200, headers: asset.headers, body: asset.bytes };
  };
}

if (import.meta.main) {
  assert(!process.env.CI, "The Dashboard browser origin is a local opt-in fixture.");
  const { values } = parseArgs({
    options: {
      bundle: { type: "string" },
      sha256: { type: "string" },
      "ready-file": { type: "string" },
    },
  });
  const { bundle, sha256, "ready-file": readyFile } = values;
  assert(bundle && sha256 && readyFile, "Use --bundle <archive> --sha256 <digest> --ready-file <new-file>.");
  const asset = await dashboardAssets(bundle, sha256);
  const server = createServer((request, response) => {
    const reply = asset(request.method ?? "", request.url ?? "", request.headers["if-none-match"]);
    response.writeHead(reply.status, reply.headers).end(reply.body);
  });
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  try {
    const address = server.address();
    assert(address && typeof address !== "string" && address.address === "127.0.0.1");
    await writeFile(readyFile, JSON.stringify({ origin: "http://127.0.0.1:" + address.port }) + "\n", {
      flag: "wx",
      mode: 0o600,
    });
  } catch (error) {
    server.close();
    throw error;
  }
}
