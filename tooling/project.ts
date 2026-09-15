// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { cp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { type DefaultTreeAdapterTypes, parse } from "parse5";

export const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
export const candidate = join(root, "artifacts/candidate");
export function run(command: string, args: string[], cwd = root): string {
  const result = spawnSync(command, args, {
    cwd,
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024,
    windowsHide: true,
  });
  if (result.status !== 0)
    throw new Error(
      `${command} ${args.join(" ")} failed\n${result.stdout ?? ""}${result.stderr ?? ""}`,
      { cause: result.error },
    );
  return result.stdout;
}
export function npm(args: string[]): string {
  assert(process.env.npm_execpath, "Run this command through npm run.");
  return run(process.execPath, [process.env.npm_execpath, ...args]);
}
export const digest = (value: string | Uint8Array) =>
  createHash("sha256").update(value).digest("hex");
export async function json(path: string) {
  return JSON.parse(await readFile(path, "utf8"));
}
export async function save(path: string, value: unknown) {
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, `${JSON.stringify(value, null, 2)}\n`);
}
export async function files(directory: string): Promise<string[]> {
  const found: string[] = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    assert(!entry.isSymbolicLink(), `Symlink not allowed in candidate: ${entry.name}`);
    if (entry.isDirectory())
      for (const child of await files(join(directory, entry.name)))
        found.push(`${entry.name}/${child}`);
    else found.push(entry.name);
  }
  return found.sort();
}
export function releaseVersion(runNumber: string | undefined, attempt: string | undefined) {
  if (!runNumber) return "0.1.0-local";
  assert(
    /^[1-9]\d*$/.test(runNumber) && /^[1-9]\d*$/.test(attempt ?? ""),
    "Invalid CI version counters",
  );
  return `0.1.0-ci.${runNumber}.${attempt}`;
}
export function contentSecurityPolicy(html: string[]) {
  const scripts = new Set<string>();
  function visit(node: DefaultTreeAdapterTypes.Node) {
    if (
      "tagName" in node &&
      node.tagName === "script" &&
      !node.attrs.some((attr) => attr.name === "src")
    ) {
      const script = node.childNodes.map((child) => ("value" in child ? child.value : "")).join("");
      if (script) scripts.add(`'sha256-${createHash("sha256").update(script).digest("base64")}'`);
    }
    if ("childNodes" in node) for (const child of node.childNodes) visit(child);
  }
  for (const page of html) visit(parse(page));
  return `default-src 'self'; script-src 'self' ${[...scripts].sort().join(" ")}; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`;
}
interface Manifest {
  schema: 1;
  version: string;
  source: string;
  dirty: boolean;
  files: Record<string, string>;
}
export async function seal(directory: string, identity: Omit<Manifest, "schema" | "files">) {
  const hashes: Record<string, string> = {};
  for (const path of await files(directory))
    if (path !== "manifest.json") hashes[path] = digest(await readFile(join(directory, path)));
  await save(join(directory, "manifest.json"), { schema: 1, ...identity, files: hashes });
}
export async function verify(
  directory = candidate,
  expectedSource = process.env.GITHUB_SHA,
): Promise<Manifest> {
  const manifest = (await json(join(directory, "manifest.json"))) as Manifest;
  assert.equal(manifest.schema, 1);
  assert(/^[a-f0-9]{40}$/.test(manifest.source), "Invalid source revision");
  if (expectedSource)
    assert.equal(manifest.source, expectedSource, "Candidate source does not match this run");
  assert.deepEqual(
    (await files(directory)).filter((path) => path !== "manifest.json"),
    Object.keys(manifest.files).sort(),
    "Candidate file set changed",
  );
  for (const [path, hash] of Object.entries(manifest.files)) {
    const absolute = resolve(directory, path);
    assert(absolute.startsWith(resolve(directory) + sep), "Invalid candidate path");
    assert.equal(digest(await readFile(absolute)), hash, `Candidate changed: ${path}`);
    if (path.startsWith("assets/"))
      assert(
        !/(?:\.map$|(?:^|\/)(?:\.env|\.dev\.vars|node_modules|server|\.vite|__spa-fallback))/.test(
          path,
        ),
        `Private/build file in public assets: ${path}`,
      );
  }
  const config = await json(join(directory, "wrangler.json"));
  assert.deepEqual(Object.keys(config).sort(), [
    "assets",
    "compatibility_date",
    "name",
    "preview_urls",
    "workers_dev",
  ]);
  assert.equal(config.name, "arcforges-web");
  assert.equal(config.preview_urls, false);
  assert.equal(config.workers_dev, true);
  assert.deepEqual(config.assets, {
    directory: "./assets",
    html_handling: "auto-trailing-slash",
    not_found_handling: "404-page",
  });
  const identity = await json(join(directory, "assets/__build.json"));
  assert.equal(identity.source, manifest.source);
  assert.equal(identity.version, manifest.version);
  for (const path of [
    "assets/index.html",
    "assets/hello/index.html",
    "assets/404.html",
    "assets/_headers",
    "sbom.cdx.json",
    "runtime-sbom.cdx.json",
    "assets/license.txt",
    "assets/third-party-notices.txt",
  ])
    assert(manifest.files[path], `Missing required file: ${path}`);
  return manifest;
}
async function notices() {
  const lock = await json(join(root, "package-lock.json"));
  let text =
    "ArcForges Web third-party notices\nGenerated from the production dependency closure. Some packages are build-time only or tree-shaken.\n";
  for (const [path, entry] of Object.entries(lock.packages) as [
    string,
    { dev?: boolean; link?: boolean; version?: string; license?: string },
  ][]) {
    if (!path.includes("node_modules/") || entry.dev || entry.link) continue;
    const name = path.split("node_modules/").at(-1);
    text += `\n===== ${name} ${entry.version} (${entry.license ?? "see included license"}) =====\n`;
    const directory = join(root, path);
    const names = (await readdir(directory)).filter((name) =>
      /^(?:license|licence|copying|notice)(?:[.-].*)?$/i.test(name),
    );
    if (
      name === "@bufbuild/protobuf" ||
      name === "@connectrpc/connect" ||
      name === "@connectrpc/connect-web"
    ) {
      const group = name === "@bufbuild/protobuf" ? "protobuf-es" : "connect-es";
      text += await readFile(join(root, "third-party", `${group}-LICENSE.txt`), "utf8");
      const sources = [
        "dist/esm/index.js",
        ...(name === "@bufbuild/protobuf" ? ["dist/esm/wire/varint.js"] : []),
      ];
      for (const source of sources) {
        const header = (await readFile(join(directory, source), "utf8")).match(
          /^(?:\/\/[^\n]*\n)+/,
        )?.[0];
        assert(header?.includes("Copyright"), `Review changed license header in ${name}/${source}`);
        text += `\n${header}\n`;
      }
    } else assert(names.length > 0, `Review missing upstream license: ${name}`);
    for (const name of names)
      text += `\n${name}\n${await readFile(join(directory, name), "utf8")}\n`;
  }
  return text;
}
async function build() {
  const source = run("git", ["rev-parse", "HEAD"]).trim();
  const dirty = run("git", ["status", "--porcelain", "--untracked-files=normal"]).trim().length > 0;
  const version = releaseVersion(process.env.GITHUB_RUN_NUMBER, process.env.GITHUB_RUN_ATTEMPT);
  process.env.VITE_SOURCE_REF = source;
  process.stdout.write(npm(["run", "build", "--workspace", "@arcforges/web-site"]));
  // Only reset this task's generated candidate, after checking its absolute boundary.
  assert.equal(relative(join(root, "artifacts"), candidate), "candidate");
  await rm(candidate, { recursive: true, force: true });
  await mkdir(candidate, { recursive: true });
  const publicRoot = join(candidate, "assets");
  const built = join(root, "apps/site/build/client");
  for (const path of await files(built)) {
    if (path.startsWith(".vite/") || path === "__spa-fallback.html") continue;
    await mkdir(dirname(join(publicRoot, path)), { recursive: true });
    await cp(join(built, path), join(publicRoot, path));
  }
  const html: string[] = [];
  for (const path of await files(publicRoot))
    if (path.endsWith(".html")) {
      // HTML parsing normalizes CRLF before CSP checks: hash the bytes browsers execute.
      const page = (await readFile(join(publicRoot, path), "utf8")).replaceAll("\r\n", "\n");
      await writeFile(join(publicRoot, path), page);
      html.push(page);
    }
  const csp = contentSecurityPolicy(html);
  assert(csp.length < 1800, "CSP exceeds the Workers header line budget");
  await writeFile(
    join(publicRoot, "_headers"),
    `/*\n  Content-Security-Policy: ${csp}\n  X-Content-Type-Options: nosniff\n  Referrer-Policy: no-referrer\n  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()\n  X-Frame-Options: DENY\n  X-Robots-Tag: noindex, nofollow\n  Cache-Control: no-cache\n/assets/*\n  ! Cache-Control\n  Cache-Control: public, max-age=31536000, immutable\n/__build.json\n  ! Cache-Control\n  Cache-Control: no-store\n`,
  );
  await cp(join(root, "LICENSE"), join(publicRoot, "license.txt"));
  await writeFile(join(publicRoot, "third-party-notices.txt"), await notices());
  await save(join(publicRoot, "__build.json"), { version, source });
  const { $schema: _schema, ...config } = await json(join(root, "wrangler.json"));
  config.assets.directory = "./assets";
  await save(join(candidate, "wrangler.json"), config);
  for (const packageName of ["proto", "api-client"])
    for (const path of ["source.json", "LICENSE", "NOTICE", "sbom.cdx.json"]) {
      await mkdir(join(candidate, "contracts", packageName), { recursive: true });
      await cp(
        join(root, "node_modules/@arcforges", packageName, path),
        join(candidate, "contracts", packageName, path),
      );
    }
  await writeFile(join(candidate, "sbom.cdx.json"), npm(["sbom", "--sbom-format", "cyclonedx"]));
  await writeFile(
    join(candidate, "runtime-sbom.cdx.json"),
    npm(["sbom", "--sbom-format", "cyclonedx", "--omit=dev"]),
  );
  await seal(candidate, { version, source, dirty });
  await verify();
  console.log(
    `Verified candidate ${version} at ${source}${dirty ? " (local changes; not deployable)" : ""}`,
  );
}
async function policy() {
  assert.equal(
    process.version,
    `v${(await readFile(join(root, ".node-version"), "utf8")).trim()}`,
    "Select the pinned Node version",
  );
  const packagePaths = ["package.json", "apps/site/package.json", "packages/ui/package.json"];
  for (const path of packagePaths) {
    const manifest = await json(join(root, path));
    assert.equal(manifest.private, true, `${path} must not be published to npm`);
    assert.equal(manifest.license, "AGPL-3.0-only");
    for (const section of ["dependencies", "devDependencies", "peerDependencies"])
      for (const [name, version] of Object.entries(manifest[section] ?? {})) {
        assert(
          /^\d+\.\d+\.\d+(?:-[\w.-]+)?$/.test(String(version)),
          `${path}: pin ${name} exactly`,
        );
      }
  }
  const lock = await json(join(root, "package-lock.json"));
  assert.equal(lock.lockfileVersion, 3);
  for (const [path, entry] of Object.entries(lock.packages) as [
    string,
    { link?: boolean; integrity?: string; resolved?: string },
  ][]) {
    if (!path.includes("node_modules/")) continue;
    if (entry.link)
      assert(
        ["apps/site", "packages/ui"].includes(entry.resolved ?? ""),
        "Unexpected workspace link",
      );
    else
      assert(
        entry.integrity?.startsWith("sha512-") &&
          entry.resolved?.startsWith("https://registry.npmjs.org/"),
        `Unverified registry dependency: ${path}`,
      );
  }
  assert.equal((await json(join(root, "wrangler.json"))).assets.not_found_handling, "404-page");
  assert.equal(
    (await json(join(root, "package.json"))).packageManager,
    `npm@${npm(["--version"]).trim()}`,
  );
  console.log("Toolchain, exact dependencies, lock provenance and static routing policy passed.");
}
async function main() {
  switch (process.argv[2]) {
    case "build":
      await build();
      break;
    case "verify": {
      const manifest = await verify();
      console.log(`Candidate verified: ${manifest.version} ${manifest.source}`);
      break;
    }
    case "policy":
      await policy();
      break;
    case "hooks":
      run("git", ["config", "extensions.worktreeConfig", "true"]);
      run("git", ["config", "--worktree", "core.hooksPath", ".githooks"]);
      console.log("Hooks enabled for this worktree.");
      break;
    default:
      throw new Error("Use build, verify, policy or hooks.");
  }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
