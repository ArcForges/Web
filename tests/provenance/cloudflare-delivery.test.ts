// SPDX-License-Identifier: AGPL-3.0-only
// The deployment helpers that remain in Node (tooling/cloudflare.ts) and the CI version rule. Ported from the vitest file
// tests/unit/delivery.test.ts; the candidate seal and CSP cases retire to tests/provenance/csharp-candidate.test.ts and the
// C# Site suite, which verify the same bytes.
import assert from "node:assert/strict";
import { test } from "node:test";
import {
  deploymentUrl,
  requireCustomDomain,
  waitForDelivery,
  waitForIdentity,
} from "../../tooling/cloudflare.ts";
import { releaseVersion } from "../../tooling/project.ts";

test("CI versions are unique across runs and reruns", () => {
  assert.equal(releaseVersion("10", "2"), "0.1.0-ci.10.2");
  assert.equal(releaseVersion(undefined, undefined), "0.1.0-local");
  assert.throws(() => releaseVersion("1", "bad"));
});

test("live verification tolerates propagation with read-only requests", async () => {
  const identity = { source: "a".repeat(40), version: "0.1.0-ci.1.1" };
  let calls = 0;
  await waitForIdentity(deploymentUrl, identity, {
    attempts: 3,
    pause: async () => {},
    fetcher: async (_input, init) => {
      assert.equal(init?.method, undefined);
      calls++;
      return Response.json(calls < 2 ? { ...identity, version: "old" } : identity);
    },
  });
  assert.equal(calls, 2);
  await assert.rejects(
    waitForIdentity(deploymentUrl, identity, {
      attempts: 2,
      pause: async () => {},
      fetcher: async () => Response.json({ version: "old" }),
    }),
    /No redeployment/u,
  );
  await assert.rejects(
    waitForIdentity("https://other.example.com", identity),
    /Unexpected deployment host/u,
  );
});

test("deployment requires the custom domain to belong to this production Worker", () => {
  const domain = {
    hostname: "arcforges.com",
    service: "arcforges-web",
    environment: "production",
  };
  assert.doesNotThrow(() => requireCustomDomain([domain]));
  for (const domains of [
    [],
    [{ ...domain, service: "another-worker" }],
    [{ ...domain, hostname: "other.example.com" }],
    [{ ...domain, environment: "staging" }],
  ])
    assert.throws(() => requireCustomDomain(domains), /Attach arcforges\.com/u);
});

test("asset verification waits for headers as well as identity and fails boundedly", async () => {
  let reads = 0;
  await waitForDelivery(
    async (signal) => {
      assert.equal(signal.aborted, false);
      reads++;
      if (reads === 1) throw new Error("Missing no-transform: /404.css");
    },
    { timeoutMs: 1000, intervalMs: 0 },
  );
  assert.equal(reads, 2);
  await assert.rejects(
    waitForDelivery(
      async () => {
        throw new Error("Deployed bytes differ: /hello/");
      },
      { timeoutMs: 10, intervalMs: 0 },
    ),
    /no redeployment was attempted\. Error: Deployed bytes differ: \/hello\//u,
  );
});
