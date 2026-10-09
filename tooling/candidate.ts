// SPDX-License-Identifier: AGPL-3.0-only
// Node glue of the C# candidate. The C# tool (tools/ArcForges.Web.Tooling) builds, seals and verifies the public candidate
// and the Blazor profile bundle. Node produces the two inputs only Node can produce: the Worker JavaScript emitted from
// worker/index.ts by type stripping, and the build identity derived from git and the reviewed version sources. Before any
// deployment Node also re-derives the identity from the checkout, so a candidate with a foreign identity cannot ship.
//   node tooling/candidate.ts worker <out.js>
//   node tooling/candidate.ts identity <version> <out.json>
//   node tooling/candidate.ts verify [candidate-directory]
import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { expectedIdentity, verifyIdentity } from "./build-identity.ts";
import { candidate, root, run, save, workerScript } from "./project.ts";

export const toolLibrary = join(
  root,
  "tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll",
);

export interface SealedManifest {
  version: string;
  source: string;
  dirty: boolean;
  files: Record<string, string>;
}

/** Runs one command of the C# tool and returns its standard output. The tool is built by `dotnet build` beforehand. */
export function tool(args: string[]): string {
  return run("dotnet", [toolLibrary, ...args]);
}

/**
 * Verifies a sealed candidate before anything is uploaded: the C# verifier checks the seal, the members, the wrangler
 * configuration, the pinned Worker, the regenerated Site and the reviewed inputs; then the identity is re-derived here.
 */
export async function verifyCandidate(
  directory: string = candidate,
  expectedSource: string | undefined = process.env.GITHUB_SHA,
): Promise<SealedManifest> {
  const args = ["candidate", "verify", "--dir", directory, "--repo", root];
  if (expectedSource) args.push("--expected-source", expectedSource);
  process.stdout.write(tool(args));
  const manifest = JSON.parse(
    await readFile(join(directory, "manifest.json"), "utf8"),
  ) as SealedManifest;
  const identity = JSON.parse(await readFile(join(directory, "assets/__build-info.json"), "utf8"));
  verifyIdentity(identity, manifest.version);
  assert.equal(
    identity.build.dirty,
    manifest.dirty,
    "The identity dirty flag differs from the seal",
  );
  return manifest;
}

async function main() {
  const [command, first, second] = process.argv.slice(2);
  switch (command) {
    case "worker": {
      assert(first, "Use: worker <out.js>");
      await writeFile(first, await workerScript());
      break;
    }
    case "identity": {
      assert(first && second, "Use: identity <version> <out.json>");
      await save(second, expectedIdentity(first));
      break;
    }
    case "verify": {
      const manifest = await verifyCandidate(first ? resolve(first) : candidate);
      console.log(`Candidate verified: ${manifest.version} ${manifest.source}`);
      break;
    }
    default:
      throw new Error("Use worker, identity or verify.");
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
