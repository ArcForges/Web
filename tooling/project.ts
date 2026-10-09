// SPDX-License-Identifier: AGPL-3.0-only
// The Node tooling that remains after the React removal (WEB.40 unit U5). The public Site, the Blazor profiles, the sealed
// candidate and the profile bundle are built and verified by the C# tool (tools/ArcForges.Web.Tooling). What stays here is
// the Worker emission, the reviewed policy checks and the npm lock provenance rule.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { stripTypeScriptTypes } from "node:module";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { auditLicences, evaluatedManagedLicences } from "./licence-boundary.ts";
import { auditProvenance, gitEnvironment } from "./provenance.ts";

export const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
export const candidate = join(root, "artifacts/candidate");
// The canonical-host Worker is authored in TypeScript (worker/index.ts). The candidate ships its JavaScript form, produced
// by Node type stripping: no bundling, no import, no added code.
export const workerSource = join(root, "worker/index.ts");
export async function workerScript(): Promise<string> {
  const source = (await readFile(workerSource, "utf8")).replaceAll("\r\n", "\n");
  return stripTypeScriptTypes(source);
}
export function run(command: string, args: string[], cwd = root): string {
  const result = spawnSync(command, args, {
    cwd,
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024,
    windowsHide: true,
    env: gitEnvironment(),
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
export const digest = (value: string | Uint8Array) => createHash("sha256").update(value).digest("hex");
export async function json(path: string) {
  return JSON.parse(await readFile(path, "utf8"));
}
export async function save(path: string, value: unknown) {
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, `${JSON.stringify(value, null, 2)}\n`);
}
export function releaseVersion(runNumber: string | undefined, attempt: string | undefined) {
  if (!runNumber) return "0.1.0-local";
  assert(
    /^[1-9]\d*$/.test(runNumber) && /^[1-9]\d*$/.test(attempt ?? ""),
    "Invalid CI version counters",
  );
  return `0.1.0-ci.${runNumber}.${attempt}`;
}
/** The C# candidate tool emits the Worker's expected bytes; the candidate must carry exactly them. */
export async function verifyWorkerScript(directory: string) {
  assert.equal(
    await readFile(join(directory, "worker/index.js"), "utf8"),
    await workerScript(),
    "Worker script changed from the reviewed source",
  );
}

type LockEntry = { link?: boolean; inBundle?: boolean; integrity?: string; resolved?: string };

function requireRegistryArtifact(path: string, entry: LockEntry) {
  assert(
    entry.integrity?.startsWith("sha512-") &&
      entry.resolved?.startsWith("https://registry.npmjs.org/"),
    `Unverified registry dependency: ${path}`,
  );
}

/** The lock may hold registry artifacts only. The repository has no npm workspaces, so no link is admitted. */
export function verifyLockProvenance(packages: Record<string, LockEntry>) {
  for (const [path, entry] of Object.entries(packages)) {
    if (!path.includes("node_modules/")) continue;
    if (entry.link) {
      assert.fail(`Unexpected workspace link: ${path}`);
    }
    let artifactPath = path;
    let artifact = entry;
    // npm extracts inBundle entries from their enclosing tarball. Its hash covers them.
    while (artifact.inBundle) {
      if (artifact.resolved !== undefined || artifact.integrity !== undefined)
        requireRegistryArtifact(artifactPath, artifact);
      const boundary = artifactPath.lastIndexOf("/node_modules/");
      const parentPath = artifactPath.slice(0, boundary);
      const parent = packages[parentPath];
      assert(
        boundary > 0 && parent && !parent.link,
        `Bundled dependency has no registry package ancestor: ${path}`,
      );
      artifactPath = parentPath;
      artifact = parent;
    }
    requireRegistryArtifact(artifactPath, artifact);
  }
}

export async function policy() {
  await save(join(root, "artifacts/evidence/licence-boundary.json"), auditLicences(root));
  await save(join(root, "artifacts/evidence/source-provenance.json"), auditProvenance(root));
  const naming = await json(join(root, "eng/policy/naming-candidate.json"));
  assert.equal(naming.packageSource, "NuGet", "The naming authority is the NuGet Contracts publication");
  const namingRoot = join(root, naming.root);
  for (const [asset, expected] of Object.entries(naming.assets))
    assert.equal(
      digest(await readFile(join(namingRoot, asset))),
      expected,
      `Naming asset changed: ${asset}`,
    );
  run("python", [
    "-I",
    join(namingRoot, "tools/naming/eng/check_naming.py"),
    "--repository",
    `Web=${root}`,
    "--report",
    join(root, "artifacts/evidence/naming.json"),
  ]);
  assert.equal(
    process.version,
    `v${(await readFile(join(root, ".node-version"), "utf8")).trim()}`,
    "Select the pinned Node version",
  );
  const manifest = await json(join(root, "package.json"));
  assert.equal(manifest.private, true, "package.json must not be published to npm");
  assert.equal(manifest.license, "AGPL-3.0-only");
  for (const section of ["dependencies", "devDependencies", "peerDependencies"])
    for (const [name, version] of Object.entries(manifest[section] ?? {}))
      assert(
        /^\d+\.\d+\.\d+(?:-[\w.-]+)?$/.test(String(version)),
        `package.json: pin ${name} exactly`,
      );
  const lock = await json(join(root, "package-lock.json"));
  assert.equal(lock.lockfileVersion, 3);
  verifyLockProvenance(lock.packages);
  assert.equal((await json(join(root, "wrangler.json"))).assets.not_found_handling, "404-page");
  assert.equal(manifest.packageManager, `npm@${npm(["--version"]).trim()}`);
  console.log("Toolchain, exact dependencies, lock provenance and static routing policy passed.");
}

async function main() {
  switch (process.argv[2]) {
    case "policy":
      await policy();
      break;
    case "licence-evaluated":
      await save(
        join(root, "artifacts/evidence/licence-evaluated.json"),
        evaluatedManagedLicences(root),
      );
      break;
    case "hooks":
      run("git", ["config", "extensions.worktreeConfig", "true"]);
      run("git", ["config", "--worktree", "core.hooksPath", ".githooks"]);
      console.log("Hooks enabled for this worktree.");
      break;
    default:
      throw new Error("Use policy, licence-evaluated or hooks.");
  }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
