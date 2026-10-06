// SPDX-License-Identifier: AGPL-3.0-only
// Builds the Account and Chat production profiles with the pinned toolchain, then measures and gates them.
//   node scripts/profiles.ts build [--twice] [--write-baseline] [account] [chat]
// --twice rebuilds each profile from scratch and requires identical bytes (determinism proof).
// --write-baseline records the measured values as the new regression baseline (a reviewed change).
// When both profiles are built, the two builds are also written as one deterministic, digest-named bundle
// (artifacts/profiles/web-profiles-<sha256>.tar, see bundle.ts) that the main-push CI publishes for the Cloud proof
// deployment. Selecting one profile writes no bundle.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { contentSecurityPolicy } from "../../../tooling/project.ts";
import { isProfile, type Profile, profiles } from "../profile.ts";
import { defaultClients, defaultOutput, writeBundle } from "./bundle.ts";
import {
  type Budgets,
  checkBudgets,
  type Measurement,
  measureProfile,
  pageFile,
  toBaseline,
  verifyProfile,
} from "./measure.ts";

const app = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const reactRouter = resolve(app, "../../node_modules/@react-router/dev/bin.cjs");
const budgetsPath = join(app, "budgets.json");

function buildOnce(profile: Profile): Measurement {
  const directory = join(app, "build", profile);
  // Only this task's generated profile directory is reset, after checking its absolute boundary.
  assert.equal(resolve(directory), resolve(app, "build", profile));
  rmSync(directory, { recursive: true, force: true });
  const result = spawnSync(process.execPath, [reactRouter, "build"], {
    cwd: app,
    stdio: "inherit",
    windowsHide: true,
    env: { ...process.env, ARCFORGES_PROFILE: profile },
  });
  assert.equal(result.status, 0, `react-router build failed for the ${profile} profile`);
  const client = join(directory, "client");
  const problems = verifyProfile(client, profile);
  assert.deepEqual(problems, [], `Profile ${profile} violates its build gates`);
  // The policy of the profile's own page only: the root index.html of the build is an unserved fallback shell.
  const html = [readFileSync(join(client, pageFile(profile)), "utf8").replaceAll("\r\n", "\n")];
  const policy = contentSecurityPolicy(html);
  assert(policy.includes("connect-src 'self'"), "Profiles may connect only to their own origin");
  assert(!policy.includes("unsafe-inline"), "Profiles allow no unsafe inline script or style");
  return measureProfile(client, profile);
}

function main(argv: string[]) {
  const [command, ...rest] = argv;
  assert.equal(command, "build", "Use: build [--twice] [--write-baseline] [profile...]");
  const flags = new Set(rest.filter((item) => item.startsWith("--")));
  for (const flag of flags)
    assert(["--twice", "--write-baseline"].includes(flag), `Unknown flag ${flag}`);
  const named = rest.filter((item) => !item.startsWith("--"));
  for (const name of named) assert(isProfile(name), `Unknown profile ${name}`);
  const selected = (named.length > 0 ? named : [...profiles]) as Profile[];
  const measurements: Measurement[] = [];
  for (const profile of selected) {
    const first = buildOnce(profile);
    if (flags.has("--twice"))
      assert.equal(
        buildOnce(profile).digest,
        first.digest,
        `Profile ${profile} is not deterministic`,
      );
    measurements.push(first);
  }
  const output = join(app, "build", "measurements.json");
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, `${JSON.stringify(measurements, null, 2)}\n`);
  for (const measurement of measurements)
    console.log(
      `${measurement.profile}: ${measurement.files} files, initial ${measurement.initialRequests} requests, ` +
        `js ${measurement.initialJs.gzip} B gzip, css ${measurement.initialCss.gzip} B gzip, digest ${measurement.digest.slice(0, 12)}`,
    );
  if (flags.has("--write-baseline")) {
    const budgets: Budgets = JSON.parse(readFileSync(budgetsPath, "utf8"));
    for (const measurement of measurements)
      budgets.profiles[measurement.profile] = toBaseline(measurement);
    writeFileSync(budgetsPath, `${JSON.stringify(budgets, null, 2)}\n`);
    console.log("Baseline written; commit it only through a reviewed change.");
    return;
  }
  const budgets: Budgets = JSON.parse(readFileSync(budgetsPath, "utf8"));
  const problems = measurements.flatMap((measurement) => checkBudgets(measurement, budgets));
  assert.deepEqual(problems, [], "A profile exceeds its regression budget");
  if (selected.length === profiles.length) {
    const bundle = writeBundle(defaultClients(), defaultOutput());
    console.log(
      `bundle ${bundle.name}: ${Object.keys(bundle.manifest.files).length} files, ${bundle.archive.byteLength} bytes`,
    );
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  main(process.argv.slice(2));
