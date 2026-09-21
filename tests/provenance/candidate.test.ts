// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { cp, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";
import { candidate, digest, files, json, save, seal, verify } from "../../tooling/project.ts";

// A positive case must consume a real built candidate. There is no synthetic positive shortcut.
const baseline = await verify();
test("the actual compiled candidate has the complete browser and legal closure", async () => {
  const receipt = await json(path.join(candidate, "provenance/receipt.json"));
  assert.equal(receipt.parsedModules, 303);
  assert.equal(receipt.emittedModules, 136);
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
    const file = (await files(root)).find((file) => /\/hello_pb-.*\.js$/u.test(file));
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
    const file = "provenance/records/browser-resources-r1.json";
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
    await edit(root, "provenance/records/browser-resources-r1.json", (text) => `${text}\n`);
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
    await edit(root, "contracts/proto/source.json", (text) =>
      text.replace("1fb1dfaaaaa7a9f2f4c64a6e1c6a2b7de47d67b0", "a".repeat(40)),
    );
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
