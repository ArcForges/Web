// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { expect, it } from "vitest";

it("published naming scanner accepts current terms and detects every forbidden term", () => {
  const packageRoot = path.resolve("node_modules/@arcforges/proto/tools/naming/eng");
  const policy = JSON.parse(
    readFileSync(path.join(packageRoot, "policy/product-names.json"), "utf8"),
  );
  const root = mkdtempSync(path.join(os.tmpdir(), "web-naming-"));
  const git = (...args: string[]) =>
    execFileSync("git", args, { cwd: root, windowsHide: true, stdio: "pipe" });
  assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
  assert(path.basename(root).startsWith("web-naming-"));
  try {
    git("init", "-q");
    git("remote", "add", "origin", "https://github.com/ArcForges/Web.git");
    git(
      "-c",
      "user.name=Fixture",
      "-c",
      "user.email=fixture@example.invalid",
      "-c",
      "commit.gpgsign=false",
      "commit",
      "--allow-empty",
      "-qm",
      "fixture",
    );
    const probe = path.join(root, "probe.txt");
    const scan = () =>
      spawnSync(
        "python",
        [path.join(packageRoot, "check_naming.py"), "--repository", `Web=${root}`],
        { encoding: "utf8", windowsHide: true },
      );
    writeFileSync(probe, "ArcForges ArcScope");
    expect(scan().status).toBe(0);
    for (const item of policy.forbiddenNames as { name: string }[]) {
      writeFileSync(probe, item.name);
      const result = scan();
      expect(result.status, item.name).toBe(1);
      expect(result.stdout).toContain('"kind": "forbidden content"');
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
