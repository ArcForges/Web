// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { parse } from "@babel/parser";
import type { Node } from "@babel/types";

export type Sources = Record<string, string>;
export interface Finding {
  rule: string;
  file: string;
  detail: string;
}
const code = /\.[cm]?[jt]sx?$/u;
const fixture = /(?:^|\/)(?:tests?|__tests__|fixtures?|test-helpers)(?:\/|\.)|\.(?:test|spec)\./u;
const production = /^(?:apps\/[^/]+\/app|packages\/[^/]+\/src|worker)\//u;
const sections = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];
const exact = /^\d+\.\d+\.\d+(?:-[\w.-]+)?$/u;

export function auditArchitecture(sources: Sources): Finding[] {
  const findings: Finding[] = [];
  const report = (rule: string, file: string, detail: string) =>
    findings.push({ rule, file, detail });
  const read = (file: string) => JSON.parse(sources[file] ?? "null");
  const root = read("package.json");
  const lock = read("package-lock.json");
  const manifests = Object.keys(sources).filter((file) => file.endsWith("package.json"));
  const locks = Object.keys(sources).filter((file) =>
    /(?:^|\/)(?:package-lock\.json|npm-shrinkwrap\.json|yarn\.lock|pnpm-lock\.yaml|bun\.lockb?)$/u.test(
      file,
    ),
  );
  if (
    !root ||
    !Array.isArray(root.workspaces) ||
    locks.length !== 1 ||
    locks[0] !== "package-lock.json" ||
    lock?.lockfileVersion !== 3
  )
    report("workspace", "package.json", "One explicit root workspace and npm v3 lock required");
  const workspaces: string[] = root?.workspaces ?? [];
  const expected = ["package.json", ...workspaces.map((name) => `${name}/package.json`)].sort();
  if (
    JSON.stringify(expected) !== JSON.stringify(manifests.sort()) ||
    workspaces.some((name) => /[*?]|^\.|\\/u.test(name))
  )
    report("workspace", "package.json", "Workspace manifest inventory must be exact");
  const node = sources[".node-version"]?.trim();
  if (
    !exact.test(root?.devDependencies?.typescript ?? "") ||
    !exact.test(root?.devDependencies?.["@react-router/dev"] ?? "")
  )
    report("pins", "package.json", "Exact compiler and static generator pins required");
  if (
    !node ||
    !exact.test(node) ||
    !/^npm@\d+\.\d+\.\d+$/u.test(root?.packageManager ?? "") ||
    !root?.engines?.node?.includes(node) ||
    !root?.engines?.npm?.includes(root?.packageManager?.slice(4))
  )
    report("pins", "package.json", "Exact Node/npm pins and matching engines required");
  const workspaceByName = new Map<string, string>();
  for (const file of manifests) {
    const manifest = read(file);
    workspaceByName.set(manifest.name, path.posix.dirname(file));
    for (const section of sections)
      for (const [name, version] of Object.entries(manifest[section] ?? {})) {
        if (typeof version !== "string" || !exact.test(version))
          report("pins", file, `Nonexact dependency ${name}`);
        if (/blazor/iu.test(name)) report("obsolete-target", file, name);
        if (/@arcforges\/(?!proto$|api-client$|web-)/u.test(name))
          report("private-import", file, name);
        if (
          manifest.license === "Apache-2.0" &&
          (name === "@arcforges/web-ui" || name === "react-dom")
        )
          report("sdk-ui", file, name);
      }
    if (file !== "package.json" && manifest.workspaces)
      report("workspace", file, "Nested workspace");
    for (const [name, script] of Object.entries(manifest.scripts ?? {}) as [string, string][]) {
      if (
        !/^(?:dev|preview|test:e2e)$/u.test(name) &&
        /\b(?:npm\s+(?:install|i|ci)|npx\b|(?:vite|react-router|wrangler)\s+dev|--hmr)\b/u.test(
          script,
        )
      )
        report("production-command", file, `${name}: implicit install or development server`);
    }
    const entry = lock?.packages?.[file === "package.json" ? "" : path.posix.dirname(file)];
    for (const section of sections)
      if (JSON.stringify(manifest[section] ?? {}) !== JSON.stringify(entry?.[section] ?? {}))
        report("workspace", file, `Lock differs: ${section}`);
  }
  const checkScript = (file: string, name: string, ancestors: Set<string>) => {
    const key = `${file}:${name}`;
    if (ancestors.has(key)) {
      report("production-command", file, `Script cycle: ${key}`);
      return;
    }
    const script = read(file)?.scripts?.[name];
    if (typeof script !== "string") {
      report("production-command", file, `Unknown script: ${name}`);
      return;
    }
    if (
      /\b(?:npm\s+(?:install|i|ci|exec)|npx\b|(?:vite|react-router|wrangler)\s+(?:dev|serve)|--hmr)\b/u.test(
        script,
      )
    )
      report("production-command", file, `Production script reaches dev/install: ${name}`);
    const next = new Set([...ancestors, key]);
    for (const command of script.split(/&&|\|\||;/u)) {
      const call = /\bnpm\s+run\s+([\w:-]+)/u.exec(command);
      if (!call?.[1]) continue;
      const workspace = /(?:--workspace(?:=|\s+)|-w\s+)([\w@/.-]+)/u.exec(command)?.[1];
      const directory = workspace ? workspaceByName.get(workspace) : undefined;
      if (workspace && !directory)
        report("production-command", file, `Unknown script workspace: ${workspace}`);
      else checkScript(directory ? `${directory}/package.json` : file, call[1], next);
    }
  };
  for (const file of manifests) {
    for (const name of Object.keys(read(file).scripts ?? {}))
      if (!["dev", "preview", "test:e2e"].includes(name)) checkScript(file, name, new Set());
    if (read(file).license !== "Apache-2.0") continue;
    const seen = new Set<string>();
    const dependencies = (current: string) => {
      if (seen.has(current)) return;
      seen.add(current);
      const manifest = read(current);
      if (manifest.license !== "Apache-2.0")
        report("sdk-ui", file, `Apache workspace reaches ${current}`);
      for (const section of sections)
        for (const name of Object.keys(manifest[section] ?? {})) {
          const directory = workspaceByName.get(name);
          if (directory) dependencies(`${directory}/package.json`);
        }
    };
    dependencies(file);
  }
  const graph = new Map<string, string[]>();
  const local = (from: string, target: string): string | undefined => {
    let stem = target.startsWith(".")
      ? path.posix.normalize(path.posix.join(path.posix.dirname(from), target))
      : target.startsWith("~/")
        ? `${from.split("/").slice(0, 2).join("/")}/app/${target.slice(2)}`
        : "";
    if (!stem)
      for (const [name, directory] of workspaceByName)
        if (target === name || target.startsWith(`${name}/`)) {
          const entry = read(`${directory}/package.json`).exports;
          const key = target === name ? "." : `.${target.slice(name.length)}`;
          const mapped = typeof entry === "string" ? entry : entry?.[key];
          if (typeof mapped === "string") stem = path.posix.join(directory, mapped);
        }
    if (!stem) return undefined;
    if (stem.startsWith("../")) {
      report("private-import", from, "Sibling source escape");
      return undefined;
    }
    const candidates = [
      stem,
      ...[".ts", ".tsx", ".js", ".jsx", "/index.ts", "/index.tsx"].map(
        (ext) => stem.replace(/\.js$/u, "") + ext,
      ),
    ];
    const resolved = candidates.find((file) => Object.hasOwn(sources, file));
    if (!resolved && !target.includes("+types/") && !/\.(?:css|svg|png|woff2?)$/u.test(target))
      report("unresolved-import", from, target);
    return resolved;
  };
  for (const [file, source] of Object.entries(sources)) {
    if (
      /\.(?:esproj|props|targets)$/u.test(file) &&
      (/<ShouldRunNpmInstall>\s*(?!false\s*<)[^<]+/u.test(source) ||
        /<BuildCommand>[^<]*(?:\b(?:install|ci|dev|serve)\b|--hmr)/u.test(source))
    )
      report("production-command", file, "Implicit IDE install or development build command");
    if (/\.(?:csproj|props|targets)$/u.test(file) && /ProjectReference[^>]+\.esproj/iu.test(source))
      report("portable-reference", file, "Managed graph references esproj");
    if (
      /\.(?:csproj|esproj|props|targets|slnx)$/u.test(file) &&
      /Blazor|Microsoft\.AspNetCore\.Components\.WebAssembly/iu.test(source)
    )
      report("obsolete-target", file, "Obsolete target");
    if (!code.test(file)) continue;
    const ast = parse(source, {
      sourceType: "unambiguous",
      plugins: ["typescript", "jsx"],
      createImportExpressions: true,
    });
    const edges: string[] = [];
    const generatedTypes = new Set<string>();
    for (const statement of ast.program.body)
      if (
        statement.type === "ImportDeclaration" &&
        ["@arcforges/proto", "@arcforges/api-client"].includes(statement.source.value)
      )
        for (const item of statement.specifiers) generatedTypes.add(item.local.name);
    const specifier = (value: string) => {
      if (/^(?:protobufjs|@bufbuild\/protobuf\/codegen)/u.test(value))
        report("wire-source", file, "Production cannot author wire descriptors/codecs");
      if (
        /^@arcforges\/(?!proto$|api-client$|web-)|(?:^|\/)(?:private|server|local-rpc|internal)(?:\/|$)|^(?:https?:|file:|[A-Za-z]:[\\/])/u.test(
          value,
        )
      )
        report("private-import", file, value);
      const resolved = local(file, value);
      if (resolved) edges.push(resolved);
      if (
        /^(?:packages\/[^/]*sdk\/|packages\/sdk\/)/u.test(file) &&
        /(?:web-ui|react-dom)/u.test(value)
      )
        report("sdk-ui", file, value);
      if (/^packages\/desktop[^/]*\//u.test(file) && /^(?:react|react-dom)(?:\/|$)/u.test(value))
        report("desktop-dom", file, value);
    };
    const visit = (node: Node) => {
      if (
        node.type === "CallExpression" &&
        node.callee.type === "MemberExpression" &&
        node.callee.object.type === "Identifier" &&
        node.callee.object.name === "JSON" &&
        ((node.callee.property.type === "Identifier" &&
          ["stringify", "parse"].includes(node.callee.property.name)) ||
          node.callee.computed)
      )
        report(
          "wire-source",
          file,
          "Production wire serialization uses published generated codecs; JSON needs explicit HTTP exception admission",
        );
      if (
        node.type === "CallExpression" &&
        node.callee.type === "Identifier" &&
        node.callee.name === "fetch" &&
        !file.startsWith("worker/")
      )
        report(
          "wire-source",
          file,
          "Business network calls must use published generated transport",
        );
      if (
        (node.type === "ImportDeclaration" ||
          node.type === "ExportNamedDeclaration" ||
          node.type === "ExportAllDeclaration") &&
        node.source
      )
        specifier(node.source.value);
      if (node.type === "TSImportType" && node.argument.type === "StringLiteral")
        specifier(node.argument.value);
      if (
        node.type === "ImportExpression" ||
        (node.type === "CallExpression" &&
          node.callee.type === "Identifier" &&
          node.callee.name === "require")
      ) {
        const argument = node.type === "ImportExpression" ? node.source : node.arguments[0];
        if (argument?.type === "StringLiteral") specifier(argument.value);
        else if (argument?.type === "TemplateLiteral" && argument.expressions.length === 0)
          specifier(argument.quasis[0]?.value.cooked ?? "");
        else report("computed-import", file, "Release imports must be statically resolved");
      }
      if (
        /^packages\/desktop[^/]*\//u.test(file) &&
        node.type === "Identifier" &&
        ["window", "document", "HTMLElement"].includes(node.name)
      )
        report("desktop-dom", file, node.name);
      if (
        (node.type === "TSInterfaceDeclaration" ||
          node.type === "TSTypeAliasDeclaration" ||
          node.type === "ClassDeclaration") &&
        /(?:Request|Response|Dto|Message)$/u.test(node.id?.name ?? "") &&
        !(
          node.type === "TSTypeAliasDeclaration" &&
          ((node.typeAnnotation.type === "TSImportType" &&
            node.typeAnnotation.argument.type === "StringLiteral" &&
            ["@arcforges/proto", "@arcforges/api-client"].includes(
              node.typeAnnotation.argument.value,
            )) ||
            (node.typeAnnotation.type === "TSTypeReference" &&
              node.typeAnnotation.typeName.type === "Identifier" &&
              generatedTypes.has(node.typeAnnotation.typeName.name)))
        )
      )
        report("wire-source", file, "Wire shapes must come from the published generated package");
      for (const [key, child] of Object.entries(node)) {
        if (
          [
            "loc",
            "comments",
            "leadingComments",
            "trailingComments",
            "innerComments",
            "tokens",
          ].includes(key)
        )
          continue;
        for (const value of Array.isArray(child) ? child : [child])
          if (
            value &&
            typeof value === "object" &&
            "type" in value &&
            typeof value.type === "string"
          )
            visit(value as Node);
      }
    };
    visit(ast);
    graph.set(file, edges);
  }
  const visit = (file: string, seen: Set<string>) => {
    if (seen.has(file)) return;
    seen.add(file);
    if (fixture.test(file))
      report("release-fixture", file, "Fixture/test helper reachable from release graph");
    for (const next of graph.get(file) ?? []) visit(next, seen);
  };
  const owner = (file: string) =>
    manifests
      .filter((m) => m !== "package.json" && file.startsWith(`${path.posix.dirname(m)}/`))
      .sort((a, b) => b.length - a.length)[0];
  for (const [name, directory] of workspaceByName) {
    if (read(`${directory}/package.json`)?.license !== "Apache-2.0") continue;
    const seen = new Set<string>();
    const check = (file: string) => {
      if (seen.has(file)) return;
      seen.add(file);
      const targetOwner = owner(file);
      if (targetOwner && read(targetOwner).license !== "Apache-2.0")
        report("sdk-ui", file, `Apache SDK ${name} reaches non-Apache workspace`);
      for (const next of graph.get(file) ?? []) check(next);
    };
    for (const file of graph.keys()) if (file.startsWith(`${directory}/`)) check(file);
  }
  const reachable = new Set<string>();
  for (const file of graph.keys()) if (production.test(file)) visit(file, reachable);
  return findings.filter((finding) => !code.test(finding.file) || reachable.has(finding.file));
}

export function auditRepository(root: string) {
  const names = execFileSync(
    "git",
    ["ls-files", "-z", "--cached", "--others", "--exclude-standard"],
    { cwd: root, encoding: "utf8", windowsHide: true },
  )
    .split("\0")
    .filter(Boolean);
  const sources: Sources = {};
  for (const name of names)
    if (
      code.test(name) ||
      /(?:package\.json|lock[^/]*|\.node-version)$|\.(?:csproj|esproj|props|targets|slnx)$/u.test(
        name,
      )
    )
      sources[name] = readFileSync(path.join(root, name), "utf8");
  const findings = auditArchitecture(sources);
  assert.equal(findings.length, 0, JSON.stringify(findings, null, 2));
  return { repository: "Web", sourceFiles: names.length, findings };
}
