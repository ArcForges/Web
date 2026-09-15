// SPDX-License-Identifier: AGPL-3.0-only
import { expect, test } from "vitest";
import { verifyLockProvenance } from "../../tooling/project.ts";

const registry = {
  resolved: "https://registry.npmjs.org/outer/-/outer-1.0.0.tgz",
  integrity: `sha512-${Buffer.alloc(64).toString("base64")}`,
};
const bundled = { inBundle: true };

test("accepts the bundled optional dependency layout reported by Dependabot", () => {
  // Preserve the failing layout as a fixture even if a future Tailwind release stops bundling it.
  expect(() =>
    verifyLockProvenance({
      "node_modules/@tailwindcss/oxide-wasm32-wasi": {
        resolved:
          "https://registry.npmjs.org/@tailwindcss/oxide-wasm32-wasi/-/oxide-wasm32-wasi-4.3.3.tgz",
        integrity:
          "sha512-jx1+rPhY/5Ympkktd656HBWEBLxP7dH06losBLjjf5vgCODXvi9KhtftWcMIwTFIDqBr7cRnQkdLnAG+IOlGvQ==",
      },
      "node_modules/@tailwindcss/oxide-wasm32-wasi/node_modules/@emnapi/core": bundled,
    }),
  ).not.toThrow();
});

test("nested bundled dependencies inherit the enclosing registry artifact", () => {
  expect(() =>
    verifyLockProvenance({
      "node_modules/outer": registry,
      "node_modules/outer/node_modules/@scope/inner": bundled,
      "node_modules/outer/node_modules/@scope/inner/node_modules/leaf": bundled,
    }),
  ).not.toThrow();
});

test("rejects orphaned bundles, workspace ancestors and incomplete or foreign artifacts", () => {
  for (const packages of [
    { "node_modules/leaf": bundled },
    { "node_modules/missing/node_modules/leaf": bundled },
    {
      "node_modules/outer": { resolved: registry.resolved },
      "node_modules/outer/node_modules/leaf": bundled,
    },
    {
      "node_modules/outer": { ...registry, resolved: "https://example.com/outer.tgz" },
      "node_modules/outer/node_modules/leaf": bundled,
    },
    {
      "node_modules/outer": { link: true, resolved: "apps/site" },
      "node_modules/outer/node_modules/leaf": bundled,
    },
    {
      "node_modules/outer": registry,
      "node_modules/outer/node_modules/leaf": {
        ...bundled,
        ...registry,
        resolved: "https://example.com/leaf.tgz",
      },
    },
  ])
    expect(() => verifyLockProvenance(packages)).toThrow();
});

test("ordinary downloads and workspace links keep their provenance requirements", () => {
  expect(() =>
    verifyLockProvenance({
      "node_modules/outer": registry,
      "node_modules/@arcforges/web-site": { link: true, resolved: "apps/site" },
    }),
  ).not.toThrow();
  for (const entry of [
    { resolved: registry.resolved },
    { ...registry, resolved: "https://example.com/outer.tgz" },
    { link: true, resolved: "../other-repo" },
  ])
    expect(() => verifyLockProvenance({ "node_modules/outer": entry })).toThrow();
});
