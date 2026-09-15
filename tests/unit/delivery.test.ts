// SPDX-License-Identifier: AGPL-3.0-only
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { expect, test } from "vitest";
import { contentSecurityPolicy, releaseVersion, seal, verify } from "../../tooling/project.ts";
import { deploymentUrl, waitForIdentity } from "../../tooling/cloudflare.ts";
test("CSP authorizes exact prerendered scripts without unsafe inline/eval", () => {
  const script = "console.log('hello')";
  const csp = contentSecurityPolicy([`<script>${script}</script><script src='/x.js'></script>`]);
  expect(csp).toContain(createHash("sha256").update(script).digest("base64"));
  expect(csp).not.toMatch(/unsafe-inline|unsafe-eval/);
  expect(csp).toContain("form-action 'none'");
});
test("CI versions are unique across runs and reruns", () => {
  expect(releaseVersion("10", "2")).toBe("0.1.0-ci.10.2");
  expect(releaseVersion(undefined, undefined)).toBe("0.1.0-local");
  expect(() => releaseVersion("1", "bad")).toThrow();
});
test("candidate verification rejects tampering and added files before deployment", async () => {
  const dir = await mkdtemp(join(tmpdir(), "arcforges-web-test-"));
  try {
    await writeFile(join(dir, "example.txt"), "original");
    await seal(dir, { source: "a".repeat(40), version: "0.1.0-local", dirty: true });
    await writeFile(join(dir, "example.txt"), "changed");
    await expect(verify(dir)).rejects.toThrow("Candidate changed");
    await writeFile(join(dir, "unexpected.txt"), "extra");
    await expect(verify(dir)).rejects.toThrow("Candidate file set changed");
    await expect(verify(dir, "b".repeat(40))).rejects.toThrow("Candidate source");
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});
test("live verification tolerates propagation with read-only requests", async () => {
  const identity = { source: "a".repeat(40), version: "0.1.0-ci.1.1" };
  let calls = 0;
  await waitForIdentity(deploymentUrl("example"), identity, {
    attempts: 3,
    pause: async () => {},
    fetcher: async (_input, init) => {
      expect(init?.method).toBeUndefined();
      calls++;
      return Response.json(calls < 2 ? { ...identity, version: "old" } : identity);
    },
  });
  expect(calls).toBe(2);
  await expect(
    waitForIdentity(deploymentUrl("example"), identity, {
      attempts: 2,
      pause: async () => {},
      fetcher: async () => Response.json({ version: "old" }),
    }),
  ).rejects.toThrow("No redeployment");
  expect(() => deploymentUrl("bad/host")).toThrow();
});
