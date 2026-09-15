// SPDX-License-Identifier: AGPL-3.0-only
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { expect, test } from "vitest";
import { contentSecurityPolicy, releaseVersion, seal, verify } from "../../tooling/project.ts";
import {
  deploymentUrl,
  requireCustomDomain,
  waitForDelivery,
  waitForIdentity,
} from "../../tooling/cloudflare.ts";
test("CSP authorizes exact prerendered scripts without unsafe inline/eval", () => {
  const script = "console.log('hello')";
  const csp = contentSecurityPolicy([`<script>${script}</script><script src='/x.js'></script>`]);
  expect(csp).toContain(createHash("sha256").update(script).digest("base64"));
  expect(csp).not.toMatch(/unsafe-inline|unsafe-eval/);
  expect(csp).toContain("form-action 'none'");
  const variant = contentSecurityPolicy([
    `<!-- <script>not executable</script> --><SCRIPT data-note='>'>\r\n${script}</SCRIPT \t>`,
  ]);
  expect(variant).toContain(createHash("sha256").update(`\n${script}`).digest("base64"));
  expect(variant).not.toContain(createHash("sha256").update("not executable").digest("base64"));
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
    await expect(verify(dir, "a".repeat(40))).rejects.toThrow("Candidate changed");
    await writeFile(join(dir, "unexpected.txt"), "extra");
    await expect(verify(dir, "a".repeat(40))).rejects.toThrow("Candidate file set changed");
    await expect(verify(dir, "b".repeat(40))).rejects.toThrow("Candidate source");
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});
test("live verification tolerates propagation with read-only requests", async () => {
  const identity = { source: "a".repeat(40), version: "0.1.0-ci.1.1" };
  let calls = 0;
  await waitForIdentity(deploymentUrl, identity, {
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
    waitForIdentity(deploymentUrl, identity, {
      attempts: 2,
      pause: async () => {},
      fetcher: async () => Response.json({ version: "old" }),
    }),
  ).rejects.toThrow("No redeployment");
  await expect(waitForIdentity("https://other.example.com", identity)).rejects.toThrow(
    "Unexpected deployment host",
  );
});

test("deployment requires the custom domain to belong to this production Worker", () => {
  const domain = {
    hostname: "arcforges.com",
    service: "arcforges-web",
    environment: "production",
  };
  expect(() => requireCustomDomain([domain])).not.toThrow();
  for (const domains of [
    [],
    [{ ...domain, service: "another-worker" }],
    [{ ...domain, hostname: "other.example.com" }],
    [{ ...domain, environment: "staging" }],
  ])
    expect(() => requireCustomDomain(domains)).toThrow("Attach arcforges.com");
});

test("asset verification waits for headers as well as identity and fails boundedly", async () => {
  let reads = 0;
  await waitForDelivery(
    async (signal) => {
      expect(signal.aborted).toBe(false);
      reads++;
      if (reads === 1) throw new Error("Missing no-transform: /404.css");
    },
    { timeoutMs: 1000, intervalMs: 0 },
  );
  expect(reads).toBe(2);
  await expect(
    waitForDelivery(
      async () => {
        throw new Error("Deployed bytes differ: /hello/");
      },
      { timeoutMs: 10, intervalMs: 0 },
    ),
  ).rejects.toThrow("no redeployment was attempted. Error: Deployed bytes differ: /hello/");
});
