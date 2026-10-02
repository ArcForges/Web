// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import {
  axes,
  candidateEnvironment,
  expectedIdentity,
  resolveAxes,
  sourceBuild,
  verifyContractSources,
  verifyHealthIdentity,
  verifyIdentity,
} from "../../tooling/build-identity.ts";

function fixture() {
  const catalog = JSON.parse(
    readFileSync(new URL("../../eng/version-sources.json", import.meta.url), "utf8"),
  ) as {
    schemaVersion: number;
    owner: string;
    axes: Record<string, { kind: string; sources?: string[]; alias?: string }>;
  };
  const sources: Record<string, string> = {};
  for (const name of axes) {
    const kind = catalog.axes[name]?.kind;
    assert(kind);
    catalog.axes[name] = { kind, sources: [`${name}.json`] };
    sources[`${name}.json`] =
      kind === "contracts"
        ? JSON.stringify({
            schema: "fixture.rpc.v1",
            dirty: false,
            descriptorSha256: "a".repeat(64),
          })
        : kind === "native-abi"
          ? "#define ARC_ABI_MAJOR 1\n#define ARC_ABI_MINOR 0\n"
          : kind === "packages"
            ? JSON.stringify({
                dependencies: { net10: { fixture: { type: "Direct", resolved: "1.0" } } },
              })
            : JSON.stringify({
                [kind === "migrations" ? "migrations" : "versions"]: [
                  { subject: "fixture", version: "1.0" },
                ],
              });
  }
  return {
    catalog,
    sources,
    read: (name: string) => {
      const value = sources[name];
      assert(value);
      return value;
    },
  };
}

test("each of nine independently sourced axes changes alone and deterministically", () => {
  const f = fixture();
  const initial = resolveAxes(f.catalog, f.read);
  assert.deepEqual(Object.keys(initial), axes);
  assert.deepEqual(resolveAxes(f.catalog, f.read), initial);
  for (const name of axes) {
    const key = `${name}.json`;
    const original = f.read(key);
    f.sources[key] = original.replace(
      name === "ContractSet" ? "rpc.v1" : name === "NativeAbiVersion" ? "MAJOR 1" : "1.0",
      name === "ContractSet" ? "rpc.v2" : name === "NativeAbiVersion" ? "MAJOR 2" : "2.0",
    );
    const changed = resolveAxes(f.catalog, f.read);
    for (const axis of axes) {
      if (axis === name) assert.notDeepEqual(initial[axis], changed[axis]);
      else assert.deepEqual(initial[axis], changed[axis]);
    }
    f.sources[key] = original;
  }
});

test("catalog rejects missing, unknown, aliased and unsafe sources", () => {
  for (const mode of ["missing", "unknown", "alias", "path", "duplicate", "version"]) {
    const f = fixture();
    if (mode === "missing") delete f.catalog.axes.AppVersion;
    if (mode === "unknown") f.catalog.axes.OtherVersion = { kind: "release" };
    if (mode === "alias")
      f.catalog.axes.AppVersion = {
        kind: "release",
        sources: ["AppVersion.json"],
        alias: "PackageVersion",
      };
    if (mode === "path") f.catalog.axes.AppVersion = { kind: "release", sources: ["../secret"] };
    if (mode === "duplicate")
      f.sources["AppVersion.json"] = JSON.stringify({
        versions: [
          { subject: "app", version: "1" },
          { subject: "app", version: "2" },
        ],
      });
    if (mode === "version")
      f.sources["AppVersion.json"] = JSON.stringify({
        versions: [{ subject: "app", version: "ContractSet" }],
      });
    assert.throws(() => resolveAxes(f.catalog, f.read));
  }
});

test("published ContractSet preserves protobuf and JSON schema axes independently of package version", () => {
  const f = fixture();
  const receipt = JSON.parse(
    readFileSync(
      new URL("../../node_modules/@arcforges/api-client/build-identity.json", import.meta.url),
      "utf8",
    ),
  );
  f.sources["ContractSet.json"] = JSON.stringify(receipt);
  const result = resolveAxes(f.catalog, f.read).ContractSet as {
    values: { subject: string; version: string; descriptorSha256?: string }[];
  };
  const declared = receipt.axes.ContractSet.values as { subject: string }[];
  const protobuf = declared.filter((value) => !value.subject.startsWith("json:"));
  assert(protobuf.length > 0 && protobuf.length < declared.length);
  assert.equal(result.values.length, declared.length);
  assert(result.values.every((value) => value.version === "1"));
  assert.deepEqual(
    result.values
      .filter((value) => value.descriptorSha256)
      .map((value) => value.subject)
      .sort(),
    protobuf.map((value) => value.subject).sort(),
  );
  for (const mutate of [
    (r: typeof receipt) => {
      r.owner = "Web";
    },
    (r: typeof receipt) => {
      r.build.dirty = true;
    },
    (r: typeof receipt) => {
      r.axes.ContractSet.status = "not-produced";
    },
    (r: typeof receipt) => {
      r.axes.ContractSet.values = [];
    },
    (r: typeof receipt) => {
      r.axes.ContractSet.values[0].descriptorSha256 = "bad";
    },
    (r: typeof receipt) => {
      r.axes.ContractSet.values.push(r.axes.ContractSet.values[0]);
    },
  ]) {
    const changed = structuredClone(receipt);
    mutate(changed);
    f.sources["ContractSet.json"] = JSON.stringify(changed);
    assert.throws(() => resolveAxes(f.catalog, f.read));
  }
});

test("npm lock versions retain coordinate identity and exclude the workspace release", () => {
  const f = fixture();
  f.sources["PackageVersion.json"] = JSON.stringify({
    lockfileVersion: 3,
    packages: {
      "": { version: "99.0" },
      "apps/site": { version: "0.0.0" },
      "node_modules/site": { link: true, resolved: "apps/site" },
      "node_modules/example": { version: "2.3.4" },
    },
  });
  const packages = resolveAxes(f.catalog, f.read).PackageVersion as {
    values: { subject: string; version: string }[];
  };
  assert.equal(packages.values.length, 1);
  assert.equal(packages.values[0]?.subject, "pkg:npm/node_modules/example");
  assert.equal(packages.values[0]?.version, "2.3.4");
});

test("dirty, wrong-source, missing and malformed CI inputs fail closed", () => {
  const commit = "a".repeat(40);
  const env = {
    GITHUB_ACTIONS: "true",
    GITHUB_SHA: commit,
    GITHUB_REPOSITORY: "ArcForges/Web",
    GITHUB_RUN_ID: "123",
    GITHUB_RUN_ATTEMPT: "2",
  };
  assert.equal(sourceBuild(commit, 100, false, env).buildId, "123.2");
  assert.throws(() => sourceBuild(commit, 100, true, env));
  assert.throws(() => sourceBuild(commit, 0, false, env));
  for (const key of ["GITHUB_SHA", "GITHUB_REPOSITORY", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"])
    assert.throws(() => sourceBuild(commit, 100, false, { ...env, [key]: "" }));
  assert.equal(sourceBuild(commit, 100, true, {}).kind, "local");
});

test("rehashed identity and runtime health cannot substitute any source, axis or run", () => {
  const version =
    process.env.GITHUB_ACTIONS === "true"
      ? `0.1.0-ci.${process.env.GITHUB_RUN_NUMBER}.${process.env.GITHUB_RUN_ATTEMPT}`
      : "0.1.0-local";
  const expected = expectedIdentity(version);
  verifyIdentity(expected, version);
  verifyHealthIdentity({ artifact: expected.artifact, build: expected.build }, expected);
  for (const field of [
    "sourceCommit",
    "dirty",
    "buildId",
    "pipelineRun",
    "sourceDateEpoch",
    "kind",
  ]) {
    const changed = structuredClone(expected);
    Object.assign(changed.build, {
      [field]:
        field === "dirty" ? !changed.build.dirty : field === "sourceDateEpoch" ? 1 : "invalid",
    });
    assert.throws(() => verifyIdentity(changed, version));
    assert.throws(() =>
      verifyHealthIdentity({ artifact: changed.artifact, build: changed.build }, expected),
    );
  }
  const changed = structuredClone(expected);
  changed.axes.ContractSet = { status: "present", values: [] };
  assert.throws(() => verifyIdentity(changed, version));
});

test("deployment retry retains the original candidate attempt without accepting another run", () => {
  const env = { GITHUB_ACTIONS: "true", GITHUB_RUN_NUMBER: "14", GITHUB_RUN_ATTEMPT: "2" };
  assert.equal(candidateEnvironment("0.1.0-ci.14.1", env).GITHUB_RUN_ATTEMPT, "1");
  assert.equal(candidateEnvironment("0.1.0-ci.14.2", env).GITHUB_RUN_ATTEMPT, "2");
  assert.throws(() => candidateEnvironment("0.1.0-ci.14.3", env));
  assert.throws(() => candidateEnvironment("0.1.0-ci.13.1", env));
  assert.throws(() => candidateEnvironment("0.1.0-local.1", env));
  assert.equal(env.GITHUB_RUN_ATTEMPT, "2");
});

function contractSources() {
  const descriptor = "d".repeat(64);
  const digest = (character: string) => character.repeat(64);
  const contracts = {
    descriptorSha256: descriptor,
    schemaSources: {
      "public/proto/a/v1/a.proto": digest("1"),
      "public/proto/b/v1/b1.proto": digest("2"),
      "public/proto/b/v1/b2.proto": digest("3"),
      "public/http/v1/c.schema.json": digest("4"),
    },
  };
  const receipt = () => ({
    values: [
      {
        subject: "a",
        version: "1",
        descriptorSha256: descriptor,
        source: { path: "public/proto/a/v1/a.proto", sha256: digest("1") },
      },
      {
        subject: "b",
        version: "1",
        descriptorSha256: descriptor,
        sources: [
          { path: "public/proto/b/v1/b1.proto", sha256: digest("2") },
          { path: "public/proto/b/v1/b2.proto", sha256: digest("3") },
        ],
      },
      {
        subject: "json:c",
        version: "1",
        source: { path: "public/http/v1/c.schema.json", sha256: digest("4") },
      },
    ],
  });
  return { contracts, receipt };
}

test("a contract subject may span several schema sources and every source is accounted for once", () => {
  const f = contractSources();
  verifyContractSources(f.receipt(), f.contracts);
  const refusals: [string, (value: ReturnType<typeof f.receipt>) => void][] = [
    [
      "names both source and sources",
      (value) => {
        Object.assign(value.values[0] as object, { sources: [] });
      },
    ],
    [
      "names neither source nor sources",
      (value) => {
        delete (value.values[0] as { source?: unknown }).source;
      },
    ],
    [
      "names an empty source list",
      (value) => {
        (value.values[1] as unknown as { sources: unknown[] }).sources = [];
      },
    ],
    [
      "mixes protobuf and JSON schema sources",
      (value) => {
        (value.values[1] as unknown as { sources: { path: string; sha256: string }[] }).sources[1] =
          {
            path: "public/http/v1/c.schema.json",
            sha256: "4".repeat(64),
          };
      },
    ],
    [
      "changes a multi-file source digest",
      (value) => {
        const [, second] = (value.values[1] as unknown as { sources: { sha256: string }[] })
          .sources;
        assert(second);
        second.sha256 = "9".repeat(64);
      },
    ],
    [
      "names a source twice",
      (value) => {
        (value.values[1] as unknown as { sources: unknown[] }).sources.push({
          path: "public/proto/b/v1/b1.proto",
          sha256: "2".repeat(64),
        });
      },
    ],
    [
      "omits a producer schema source",
      (value) => {
        (value.values[1] as unknown as { sources: unknown[] }).sources.pop();
      },
    ],
    [
      "changes a protobuf descriptor digest",
      (value) => {
        (value.values[1] as { descriptorSha256: string }).descriptorSha256 = "e".repeat(64);
      },
    ],
    [
      "gives a JSON schema a descriptor digest",
      (value) => {
        Object.assign(value.values[2] as object, { descriptorSha256: "d".repeat(64) });
      },
    ],
  ];
  for (const [name, mutate] of refusals) {
    const changed = f.receipt();
    mutate(changed);
    assert.throws(() => verifyContractSources(changed, f.contracts), Error, name);
  }
});
