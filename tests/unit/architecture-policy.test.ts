// SPDX-License-Identifier: AGPL-3.0-only
import { describe, expect, it } from "vitest";
import { auditArchitecture, type Sources } from "../../eng/policy/architecture.ts";

function baseline(): Sources {
  const manifest = {
    name: "web",
    workspaces: ["apps/site"],
    packageManager: "npm@11.19.0",
    engines: { node: ">=24.21.0 <25", npm: ">=11.19.0 <12" },
    devDependencies: { typescript: "7.0.2", "@react-router/dev": "8.4.0" },
  };
  return {
    "package.json": JSON.stringify(manifest),
    "apps/site/package.json": JSON.stringify({ name: "site", license: "AGPL-3.0-only" }),
    "package-lock.json": JSON.stringify({
      lockfileVersion: 3,
      packages: { "": { devDependencies: manifest.devDependencies }, "apps/site": {} },
    }),
    ".node-version": "24.21.0",
    "apps/site/app/root.ts":
      'import { Message } from "@arcforges/proto"; document.title = "Web DOM is allowed";',
  };
}
function alterManifest(sources: Sources, update: (manifest: Record<string, unknown>) => void) {
  const manifest = JSON.parse(sources["package.json"] ?? "{}");
  update(manifest);
  sources["package.json"] = JSON.stringify(manifest);
}
describe("GOV.11 architecture refusal fixtures", () => {
  const cases: [string, (sources: Sources) => void][] = [
    [
      "wire-source",
      (s) => {
        s["apps/site/app/root.ts"] =
          'interface LoginPayload { password: string }; JSON.stringify({password:"x"});';
      },
    ],
    [
      "wire-source",
      (s) => {
        s["apps/site/app/root.ts"] = 'fetch("/api/custom", {body:"handwritten"});';
      },
    ],
    [
      "wire-source",
      (s) => {
        s["apps/site/app/root.ts"] = 'import { messageDesc } from "@bufbuild/protobuf/codegenv2";';
      },
    ],
    [
      "private-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'import "../bridge";';
        s["apps/site/bridge.ts"] = 'import "@arcforges/operator-client";';
      },
    ],
    [
      "release-fixture",
      (s) => {
        s["apps/site/app/root.ts"] = 'import "../bridge";';
        s["apps/site/bridge.ts"] = 'import "../../tests/helper";';
        s["tests/helper.ts"] = "export const fake = true;";
      },
    ],
    [
      "production-command",
      (s) =>
        alterManifest(s, (m) => {
          m.scripts = { build: "npm run dev", dev: "vite dev" };
        }),
    ],
    [
      "production-command",
      (s) =>
        alterManifest(s, (m) => {
          m.scripts = { build: "npm run other", other: "npm run build" };
        }),
    ],
    [
      "production-command",
      (s) => {
        s["Web.esproj"] = "<Project><ShouldRunNpmInstall>true</ShouldRunNpmInstall></Project>";
      },
    ],
    [
      "production-command",
      (s) => {
        s["Web.esproj"] = "<Project><BuildCommand>npm run dev</BuildCommand></Project>";
      },
    ],
    [
      "workspace",
      (s) => {
        s["apps/site/package-lock.json"] = "{}";
      },
    ],
    [
      "workspace",
      (s) => {
        s["yarn.lock"] = "alternate";
      },
    ],
    [
      "workspace",
      (s) => {
        s["hidden/package.json"] = '{"name":"hidden"}';
      },
    ],
    [
      "pins",
      (s) =>
        alterManifest(s, (m) => {
          m.packageManager = "npm@latest";
        }),
    ],
    [
      "pins",
      (s) =>
        alterManifest(s, (m) => {
          m.devDependencies = { typescript: "^7.0.2" };
        }),
    ],
    [
      "sdk-ui",
      (s) => {
        s["packages/sdk/src/index.ts"] = 'export * from "@arcforges/web-ui";';
      },
    ],
    [
      "private-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'export * from "@arcforges/operator-client";';
      },
    ],
    [
      "private-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'import type { X } from "@arcforges/proto/internal/x";';
      },
    ],
    [
      "private-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'const x = import("../server/private.ts");';
      },
    ],
    [
      "private-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'type T = import("@arcforges/local-rpc").T;';
      },
    ],
    [
      "private-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'const x = require("@arcforges/ai-internal");';
      },
    ],
    [
      "computed-import",
      (s) => {
        s["apps/site/app/root.ts"] = "const x = import(variable);";
      },
    ],
    [
      "desktop-dom",
      (s) => {
        s["packages/desktop/src/index.ts"] = "document.createElement('div');";
      },
    ],
    [
      "obsolete-target",
      (s) => {
        s["obsolete.csproj"] = '<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly" />';
      },
    ],
    [
      "portable-reference",
      (s) => {
        s["Portable.csproj"] = '<Project><ProjectReference Include="Web.esproj" /></Project>';
      },
    ],
    [
      "production-command",
      (s) =>
        alterManifest(s, (m) => {
          m.scripts = { build: "npm install && vite build" };
        }),
    ],
    [
      "production-command",
      (s) =>
        alterManifest(s, (m) => {
          m.scripts = { start: "vite dev --host" };
        }),
    ],
    [
      "wire-source",
      (s) => {
        s["apps/site/app/root.ts"] = "export interface LoginRequest { token: string }";
      },
    ],
    [
      "release-fixture",
      (s) => {
        s["apps/site/app/root.ts"] = 'import "./bridge";';
        s["apps/site/app/bridge.ts"] = 'export * from "./fixtures/secret";';
        s["apps/site/app/fixtures/secret.ts"] = "export const fake = true";
      },
    ],
    [
      "unresolved-import",
      (s) => {
        s["apps/site/app/root.ts"] = 'import "./missing";';
      },
    ],
  ];
  for (const [rule, mutation] of cases)
    it(`${rule}: accepts valid graph and rejects mutation`, () => {
      const sources = baseline();
      expect(auditArchitecture(sources)).toEqual([]);
      mutation(sources);
      expect(auditArchitecture(sources).some((finding) => finding.rule === rule)).toBe(true);
    });
  it("ignores quoted/commented fake imports and permits local development", () => {
    const sources = baseline();
    sources["apps/site/app/root.ts"] =
      '// import x from "@arcforges/private";\nconst text = "document @arcforges/private";';
    alterManifest(sources, (m) => {
      m.scripts = { dev: "vite dev", build: "vite build" };
    });
    expect(auditArchitecture(sources)).toEqual([]);
  });
  it("permits UI-local models and aliases of imported generated wire types", () => {
    const sources = baseline();
    sources["apps/site/app/root.ts"] =
      'import type { SayHelloRequest } from "@arcforges/proto"; type HelloRequest = SayHelloRequest; type OtherRequest = import("@arcforges/proto").SayHelloRequest; interface ViewState { selected: boolean }';
    expect(auditArchitecture(sources)).toEqual([]);
  });
  it("rejects transitive Apache-to-AGPL manifest dependencies without source imports", () => {
    const sources = baseline();
    const root = JSON.parse(sources["package.json"] ?? "{}");
    root.workspaces.push("packages/sdk", "packages/bridge", "packages/other");
    sources["package.json"] = JSON.stringify(root);
    const lock = JSON.parse(sources["package-lock.json"] ?? "{}");
    for (const [directory, name, license, dependencies] of [
      ["sdk", "@arcforges/web-sdk", "Apache-2.0", { "@arcforges/web-bridge": "1.0.0" }],
      ["bridge", "@arcforges/web-bridge", "Apache-2.0", { "@arcforges/web-other": "1.0.0" }],
      ["other", "@arcforges/web-other", "Apache-2.0", {}],
    ] as const) {
      sources[`packages/${directory}/package.json`] = JSON.stringify({
        name,
        license,
        dependencies,
      });
      lock.packages[`packages/${directory}`] = { dependencies };
    }
    sources["package-lock.json"] = JSON.stringify(lock);
    expect(auditArchitecture(sources)).toEqual([]);
    sources["packages/other/package.json"] = (sources["packages/other/package.json"] ?? "").replace(
      "Apache-2.0",
      "AGPL-3.0-only",
    );
    expect(auditArchitecture(sources).some((f) => f.rule === "sdk-ui")).toBe(true);
  });
});
