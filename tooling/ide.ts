// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { digest, root, save, verify } from "./project.ts";
import { gitEnvironment } from "./provenance.ts";

assert.equal(process.platform, "win32", "The IDE adapter is a Windows entry point.");
const evidence = join(root, "artifacts/evidence");
await mkdir(evidence, { recursive: true });
async function msbuild(name: string, args: string[]) {
  const result = spawnSync("dotnet", ["msbuild", "-nologo", ...args], {
    cwd: root,
    env: gitEnvironment(),
    encoding: "utf8",
    windowsHide: true,
    maxBuffer: 16 * 1024 * 1024,
    timeout: 10 * 60 * 1000,
  });
  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  await writeFile(join(evidence, `ide-${name}.log`), output);
  assert.equal(result.status, 0, `IDE ${name} failed: ${result.error ?? output}`);
  return output;
}

const properties = JSON.parse(
  await msbuild("properties", [
    "ArcForges.Web.esproj",
    "-getProperty:StartupCommand,BuildCommand,ShouldRunNpmInstall,ShouldRunBuildScript,PublishAot,IsAotCompatible",
    "-getItem:ProjectReference",
  ]),
);
assert.deepEqual(properties.Items.ProjectReference, []);
assert.equal(properties.Properties.StartupCommand, "npm run dev");
assert.equal(properties.Properties.BuildCommand, "npm run build");
assert.equal(properties.Properties.ShouldRunNpmInstall, "false");
assert.equal(properties.Properties.ShouldRunBuildScript, "true");
assert.equal(properties.Properties.PublishAot, "");
assert.equal(properties.Properties.IsAotCompatible, "");

const lock = join(root, "package-lock.json");
const lockHash = digest(await readFile(lock));
const restore = await msbuild("restore", ["win.slnx", "-t:Restore"]);
assert(
  restore.includes("ArcForges explicit locked npm restore"),
  "Solution restore skipped npm ci.",
);
assert.equal(digest(await readFile(lock)), lockHash, "IDE restore changed the committed lock.");
const installedLock = join(root, "node_modules/.package-lock.json");
const installed = {
  hash: digest(await readFile(installedLock)),
  mtimeMs: (await stat(installedLock)).mtimeMs,
};
const designTime = await msbuild("design-time", [
  "win.slnx",
  "-t:Restore",
  "-p:DesignTimeBuild=true",
]);
assert(
  !designTime.includes("ArcForges explicit locked npm restore"),
  "Design-time evaluation unexpectedly restored npm.",
);
assert.equal((await stat(installedLock)).mtimeMs, installed.mtimeMs);
const build = await msbuild("build", ["win.slnx", "-t:Build", "-p:Configuration=Release"]);
assert(build.includes("Verified candidate "), "Solution build skipped the npm candidate build.");
assert(
  !build.includes("ArcForges explicit locked npm restore"),
  "Build unexpectedly restored npm.",
);
assert.equal(digest(await readFile(lock)), lockHash, "Build changed the committed lock.");
assert.equal(digest(await readFile(installedLock)), installed.hash);
assert.equal(
  (await stat(installedLock)).mtimeMs,
  installed.mtimeMs,
  "Build reinstalled npm dependencies.",
);
const manifest = await verify();
const startup = JSON.parse(
  await msbuild("startup", [
    "ArcForges.Web.esproj",
    "-t:ComputeRunArguments",
    "-getProperty:RunCommand,RunArguments,RunWorkingDirectory",
  ]),
);
assert(startup.Properties.RunArguments.includes("npm run dev"));
assert.equal((await stat(installedLock)).mtimeMs, installed.mtimeMs);
const launch = JSON.parse(await readFile(join(root, ".vscode/launch.json"), "utf8"));
assert.equal(launch.configurations[0].url, "http://127.0.0.1:5173");
await save(join(evidence, "ide-entrypoints.json"), {
  source: manifest.source,
  candidateVersion: manifest.version,
  properties,
  startup,
  lockSha256: lockHash,
  solutionRestore: "passed",
  solutionBuild: "passed",
  designTimeRestoredDependencies: false,
  buildReinstalledDependencies: false,
  actualVisualStudioF5: "separate local IDE verification; CLI dispatch is not F5 evidence",
});
console.log("IDE solution restore/build and startup dispatch passed.");
