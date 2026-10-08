// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Policy.Tests.Fixtures;

/// <summary>One negative fixture: the rule that must refuse the mutated repository, and the mutation.</summary>
public sealed record PolicyCase(string Rule, Action<Dictionary<string, string>> Mutate);

/// <summary>
/// The negative examples of the GOV.11 successor, one for every rule family and sub-rule the Node/TypeScript mechanism
/// enforced, plus the accepted forms that the successor must not refuse (the passing examples).
/// </summary>
public static class PolicyCases
{
    private const string Ui = "src/ArcForges.Web.Ui/ArcForges.Web.Ui.csproj";
    private const string Root = "package.json";
    private const string Site = "apps/site/package.json";
    private const string RootTs = "apps/site/app/root.ts";

    /// <summary>Every refusal case, keyed by its stable identifier.</summary>
    public static IReadOnlyDictionary<string, PolicyCase> Refusals { get; } = new Dictionary<string, PolicyCase>(StringComparer.Ordinal)
    {
        // whitespace and final newline: the text-format rules that replace the removed Prettier and Biome gates
        ["whitespace-trailing-space"] = new("whitespace", s => s[Ui] = s[Ui].Replace("\n", " \n", StringComparison.Ordinal)),
        ["final-newline-missing"] = new("final-newline", s => s[Ui] = s[Ui].TrimEnd('\n')),
        ["final-newline-double"] = new("final-newline", s => s[Ui] += "\n"),
        // workspace: one solution, one lock per project, one npm lock and manifest inventory
        ["workspace-second-solution"] = new("workspace", s => s["ArcForges.sln"] = "Microsoft Visual Studio Solution File"),
        ["workspace-unlocked-project"] = new("workspace", s => s["src/ArcForges.Web.Site/ArcForges.Web.Site.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />"),
        ["workspace-orphan-lock"] = new("workspace", s => s["tests/Orphan/packages.lock.json"] = "{}"),
        ["workspace-packages-config"] = new("workspace", s => s["src/ArcForges.Web.Ui/packages.config"] = "<packages />"),
        ["workspace-yarn-lock"] = new("workspace", s => s["yarn.lock"] = "alternate"),
        ["workspace-nested-manifest"] = new("workspace", s => s["hidden/package.json"] = "{\"name\":\"hidden\"}"),
        ["workspace-nested-workspaces"] = new("workspace", s => s["apps/site/package.json"] = "{\"name\":\"site\",\"workspaces\":[\"x\"],\"license\":\"AGPL-3.0-only\",\"arcforges\":{\"licenceBoundary\":\"AGPL\"}}"),
        ["workspace-lock-differs"] = new("workspace", s => PolicyBaseline.EditJson(s, Site, o => o["devDependencies"] = new System.Text.Json.Nodes.JsonObject { ["left-pad"] = "1.3.0" })),
        ["workspace-restore-without-lock"] = new("workspace", s => PolicyBaseline.Replace(s, "Directory.Build.props", "<RestorePackagesWithLockFile>true", "<RestorePackagesWithLockFile>false")),
        ["workspace-alternate-nuget-source"] = new("workspace", s => PolicyBaseline.Replace(s, "NuGet.config", "</packageSources>", "<add key=\"other\" value=\"https://example.invalid/index.json\" /></packageSources>")),
        ["workspace-alternate-restore-source"] = new("workspace", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<PropertyGroup><RestoreSources>https://example.invalid/index.json</RestoreSources></PropertyGroup></Project>")),

        // pins: exact SDK, Node and npm, central versions, no inline versions, exact downloads
        ["pins-sdk-floating"] = new("pins", s => PolicyBaseline.Replace(s, "global.json", "\"version\":\"10.0.401\"", "\"version\":\"10.0.*\"")),
        ["pins-sdk-roll-forward"] = new("pins", s => PolicyBaseline.Replace(s, "global.json", "\"rollForward\":\"disable\"", "\"rollForward\":\"latestMinor\"")),
        ["pins-inline-version"] = new("pins", s => PolicyBaseline.Replace(s, Ui, "<PackageReference Include=\"ArcForges.Contracts.PublicApi\" />", "<PackageReference Include=\"ArcForges.Contracts.PublicApi\" Version=\"1.0.0-ci.287.1\" />")),
        ["pins-floating-central-version"] = new("pins", s => PolicyBaseline.Replace(s, "Directory.Packages.props", "Version=\"1.0.0-ci.287.1\"", "Version=\"1.0.*\"")),
        ["pins-central-management-off"] = new("pins", s => PolicyBaseline.Replace(s, "Directory.Packages.props", "<ManagePackageVersionsCentrally>true", "<ManagePackageVersionsCentrally>false")),
        ["pins-download-unbracketed"] = new("pins", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<ItemGroup><PackageDownload Include=\"Microsoft.NETCore.App.Runtime.Mono.browser-wasm\" Version=\"10.0.12\" /></ItemGroup></Project>")),
        ["pins-npm-caret"] = new("pins", s => PolicyBaseline.EditJson(s, Root, o => ((System.Text.Json.Nodes.JsonObject)o["devDependencies"]!)["typescript"] = "^7.0.2")),
        ["pins-package-manager-floating"] = new("pins", s => PolicyBaseline.EditJson(s, Root, o => o["packageManager"] = "npm@latest")),
        ["pins-node-version-floating"] = new("pins", s => s[".node-version"] = "24\n"),

        // obsolete-target: Blazor WebAssembly standalone only
        ["obsolete-blazor-server-package"] = new("obsolete-target", s => PolicyBaseline.Replace(s, Ui, "<PackageReference Include=\"ArcForges.Contracts.PublicApi\" />", "<PackageReference Include=\"Microsoft.AspNetCore.Components.Server\" Version=\"10.0.10\" />")),
        ["obsolete-interactive-server-markup"] = new("obsolete-target", s => s["src/ArcForges.Web.Ui/Greeting.razor"] = "@rendermode InteractiveServer\n<p>Hello</p>\n"),
        ["obsolete-circuit-code"] = new("obsolete-target", s => s["src/ArcForges.Web.Ui/Host.cs"] = "namespace ArcForges.Web.Ui;\npublic static class Host { public static void Start(IServiceCollection services) => services.AddInteractiveServerComponents(); }\n"),
        ["obsolete-npm-blazor-dependency"] = new("obsolete-target", s => PolicyBaseline.EditJson(s, Site, o => o["dependencies"] = new System.Text.Json.Nodes.JsonObject { ["blazor-server-kit"] = "1.0.0" })),

        // portable-reference and production-command
        ["portable-esproj-reference"] = new("portable-reference", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<ItemGroup><ProjectReference Include=\"..\\..\\ArcForges.Web.esproj\" /></ItemGroup></Project>")),
        ["production-esproj-install"] = new("production-command", s => s["ArcForges.Web.esproj"] = "<Project><PropertyGroup><ShouldRunNpmInstall>true</ShouldRunNpmInstall></PropertyGroup></Project>"),
        ["production-esproj-dev-build"] = new("production-command", s => s["ArcForges.Web.esproj"] = "<Project><PropertyGroup><BuildCommand>npm run dev</BuildCommand></PropertyGroup></Project>"),
        ["production-exec-npm-install"] = new("production-command", s => PolicyBaseline.Replace(s, "Directory.Build.targets", "<Project />", "<Project><Target Name=\"X\"><Exec Command=\"npm install\" /></Target></Project>")),
        ["production-exec-dotnet-restore"] = new("production-command", s => PolicyBaseline.Replace(s, "Directory.Build.targets", "<Project />", "<Project><Target Name=\"X\"><Exec Command=\"dotnet restore\" /></Target></Project>")),
        ["production-spa-proxy"] = new("production-command", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<PropertyGroup><SpaProxyLaunchCommand>npm run dev</SpaProxyLaunchCommand></PropertyGroup></Project>")),
        ["production-script-vite-dev"] = new("production-command", s => PolicyBaseline.EditJson(s, Root, o => o["scripts"] = new System.Text.Json.Nodes.JsonObject { ["build"] = "vite dev" })),
        ["production-script-install"] = new("production-command", s => PolicyBaseline.EditJson(s, Root, o => o["scripts"] = new System.Text.Json.Nodes.JsonObject { ["build"] = "npm install && vite build" })),
        ["production-script-cycle"] = new("production-command", s => PolicyBaseline.EditJson(s, Root, o => o["scripts"] = new System.Text.Json.Nodes.JsonObject { ["build"] = "npm run other", ["other"] = "npm run build" })),
        ["production-script-unknown"] = new("production-command", s => PolicyBaseline.EditJson(s, Root, o => o["scripts"] = new System.Text.Json.Nodes.JsonObject { ["build"] = "npm run missing" })),

        // sdk-ui: an Apache project never reaches an AGPL project
        ["sdk-apache-csharp-reaches-agpl"] = new("sdk-ui", s => s["packages/sdk/Sdk.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><LicenceBoundary>Apache</LicenceBoundary><PackageLicenseExpression>Apache-2.0</PackageLicenseExpression></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/ArcForges.Web.Ui/ArcForges.Web.Ui.csproj\" /></ItemGroup></Project>"),
        ["sdk-apache-npm-reaches-agpl"] = new("sdk-ui", s => s["packages/sdk/package.json"] = "{\"name\":\"@arcforges/web-sdk\",\"license\":\"Apache-2.0\",\"dependencies\":{\"site\":\"1.0.0\"}}"),
        ["sdk-ts-imports-web-ui"] = new("sdk-ui", s => s["packages/sdk/src/index.ts"] = "export * from \"@arcforges/web-ui\";\n"),

        // wire-source: generated wire types only, the admitted codec and transport
        ["wire-csharp-dto-declaration"] = new("wire-source", s => s["src/ArcForges.Web.Ui/LoginRequest.cs"] = "namespace ArcForges.Web.Ui;\npublic sealed record LoginRequest(string Name);\n"),
        ["wire-csharp-json-codec"] = new("wire-source", s => s["src/ArcForges.Web.Ui/Codec.cs"] = "namespace ArcForges.Web.Ui;\npublic static class Codec { public static string Text() => System.Text.Json.JsonSerializer.Serialize(1); }\n"),
        ["wire-csharp-direct-http"] = new("wire-source", s => s["src/ArcForges.Web.Ui/Fetcher.cs"] = "namespace ArcForges.Web.Ui;\npublic static class Fetcher { public static HttpClient Make() => new HttpClient(); }\n"),
        ["wire-ts-json-codec"] = new("wire-source", s => s["apps/site/app/codec.ts"] = "export const text = JSON.stringify({ a: 1 });\n"),
        ["wire-ts-direct-fetch"] = new("wire-source", s => s["apps/site/app/net.ts"] = "export const reply = fetch(\"/api\");\n"),
        ["wire-ts-declared-request"] = new("wire-source", s => s["apps/site/app/model.ts"] = "export interface LoginRequest { token: string }\n"),
        ["wire-ts-protobuf-codegen"] = new("wire-source", s => s["apps/site/app/codecs.ts"] = "import { messageDesc } from \"@bufbuild/protobuf/codegen\";\nexport const d = messageDesc;\n"),

        // private-import: no private, server or local-RPC namespace or package
        // The import text is split so that the raw fixture file never contains the contiguous private import that the
        // dependency gate scans for; the fixture value at run time is the same refused import.
        ["private-csharp-namespace"] = new("private-import", s => s["src/ArcForges.Web.Ui/Bridge.cs"] = "using ArcForges" + ".Contracts.Internal;\nnamespace ArcForges.Web.Ui;\npublic static class Bridge { }\n"),
        ["private-package-reference"] = new("private-import", s => PolicyBaseline.Replace(s, Ui, "<PackageReference Include=\"ArcForges.Contracts.PublicApi\" />", "<PackageReference Include=\"ArcForges.Operator.Client\" />")),
        ["private-npm-scope"] = new("private-import", s => PolicyBaseline.EditJson(s, Site, o => o["dependencies"] = new System.Text.Json.Nodes.JsonObject { ["@arcforges/operator-client"] = "1.0.0" })),
        ["private-ts-package"] = new("private-import", s => s[RootTs] = "import \"@arcforges/operator-client\";\n"),
        ["private-ts-local-rpc"] = new("private-import", s => s[RootTs] = "type T = import(\"@arcforges/local-rpc\").T;\n"),
        ["private-ts-server-path"] = new("private-import", s => s[RootTs] = "const x = import(\"../server/private.ts\");\n"),
        ["private-ts-sibling-escape"] = new("private-import", s => s[RootTs] = "import \"../../../../outside\";\n"),

        // desktop-dom: the desktop graph has no DOM or JavaScript interop
        ["desktop-ts-dom"] = new("desktop-dom", s => s["packages/desktop/src/index.ts"] = "document.createElement('div');\n"),
        ["desktop-csharp-js-interop"] = new("desktop-dom", s => s["desktop/Shell.cs"] = "namespace Desktop;\npublic sealed class Shell { public Microsoft.JSInterop.IJSRuntime? Js { get; set; } }\n"),

        // computed-import and unresolved-import
        ["computed-ts-dynamic"] = new("computed-import", s => s[RootTs] = "const x = import(variable);\n"),
        ["computed-csproj-reference"] = new("computed-import", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<ItemGroup><ProjectReference Include=\"$(SharedRoot)/Shared.csproj\" /></ItemGroup></Project>")),
        ["unresolved-ts-relative"] = new("unresolved-import", s => s[RootTs] = "import \"./missing\";\n"),
        ["unresolved-csproj-reference"] = new("unresolved-import", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<ItemGroup><ProjectReference Include=\"../Missing/Missing.csproj\" /></ItemGroup></Project>")),

        // release-fixture: no test, fixture, double or debug route in the release route graph
        ["release-project-reference-tests"] = new("release-fixture", s =>
        {
            s["tests/ArcForges.Web.Policy.Tests/ArcForges.Web.Policy.Tests.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
            PolicyBaseline.Replace(s, Ui, "</Project>", "<ItemGroup><ProjectReference Include=\"../../tests/ArcForges.Web.Policy.Tests/ArcForges.Web.Policy.Tests.csproj\" /></ItemGroup></Project>");
        }),
        ["release-csharp-test-double"] = new("release-fixture", s => s["src/ArcForges.Web.Ui/TestDoubles/Scripted.cs"] = "namespace ArcForges.Web.Ui.TestDoubles;\npublic sealed class Scripted { }\n"),
        ["release-debug-route"] = new("release-fixture", s => s["src/ArcForges.Web.Ui/Greeting.razor"] = "@page \"/fixtures/greeting\"\n<p>Hello</p>\n"),
        ["release-ts-test-helper"] = new("release-fixture", s =>
        {
            s[RootTs] = "import \"../bridge\";\n";
            s["apps/site/bridge.ts"] = "import \"../../tests/helper\";\nexport const b = 1;\n";
            s["tests/helper.ts"] = "export const fake = true;\n";
        }),
        ["release-ts-fixture-file"] = new("release-fixture", s =>
        {
            s[RootTs] = "import \"./fixtures/secret\";\n";
            s["apps/site/app/fixtures/secret.ts"] = "export const fake = true;\n";
        }),

        // licence-boundary: inventory, boundary, overrides, references and first-party owners
        ["licence-inventory-drift"] = new("inventory", s => s["unregistered/package.json"] = "{\"name\":\"unregistered\",\"license\":\"AGPL-3.0-only\",\"arcforges\":{\"licenceBoundary\":\"AGPL\"}}"),
        ["licence-missing-boundary"] = new("boundary", s => PolicyBaseline.Replace(s, Ui, "<LicenceBoundary>AGPL</LicenceBoundary>", string.Empty)),
        ["licence-apache-spdx"] = new("spdx", s => PolicyBaseline.Replace(s, Ui, "<PackageLicenseExpression>AGPL-3.0-only", "<PackageLicenseExpression>Apache-2.0")),
        ["licence-props-override"] = new("override", s => PolicyBaseline.Replace(s, "Directory.Build.props", "<LicenceBoundary>AGPL", "<LicenceBoundary>Apache")),
        ["licence-escaped-reference"] = new("reference", s => PolicyBaseline.Replace(s, Ui, "</Project>", "<ItemGroup><ProjectReference Include=\"../../../outside/Outside.csproj\" /></ItemGroup></Project>")),
        ["licence-comment-split-boundary"] = new("boundary", s => PolicyBaseline.Replace(s, Ui, "<LicenceBoundary>AGPL</LicenceBoundary>", "<LicenceB<!-- split -->oundary>AGPL</LicenceBoundary>")),
        ["licence-npm-unknown-owner"] = new("owner", s => PolicyBaseline.EditJson(s, Site, o => o["dependencies"] = new System.Text.Json.Nodes.JsonObject { ["@arcforges/unknown"] = "1.0.0" })),
        ["licence-npm-alias-owner"] = new("owner", s => PolicyBaseline.EditJson(s, Site, o => o["dependencies"] = new System.Text.Json.Nodes.JsonObject { ["alias"] = "npm:@arcforges/unknown@1.0.0" })),
        ["licence-npm-unpublished-source"] = new("source-reference", s => PolicyBaseline.EditJson(s, Site, o => o["dependencies"] = new System.Text.Json.Nodes.JsonObject { ["local-lib"] = "file:../lib" })),
        ["licence-locked-unknown-project"] = new("owner", s => PolicyBaseline.Replace(s, "src/ArcForges.Web.Ui/packages.lock.json", "\"ArcForges.Contracts.PublicApi\":{\"type\":\"Direct\"", "\"ArcForges.Contracts.Unknown\":{\"type\":\"Project\"},\"ArcForges.Contracts.PublicApi\":{\"type\":\"Direct\"")),
        ["licence-locked-unknown-transitive"] = new("owner", s => PolicyBaseline.Replace(s, "src/ArcForges.Web.Ui/packages.lock.json", "\"ArcForges.Contracts.PublicApi\":{\"type\":\"Direct\"", "\"ArcForges.Contracts.Unknown\":{\"type\":\"Transitive\"},\"ArcForges.Contracts.PublicApi\":{\"type\":\"Direct\"")),
        ["licence-lock-unknown-owner"] = new("owner", s => s["package-lock.json"] = "{\"lockfileVersion\":3,\"packages\":{\"node_modules/@arcforges/unknown\":{}}}"),
        ["licence-gradle-declaration"] = new("gradle", s => s["build.gradle.kts"] = "extra[\"spdxLicense\"] = \"AGPL-3.0-only\"\nextra[\"licenceBoundary\"] = \"Apache\"\n"),
    };

    /// <summary>Accepted forms: each mutation keeps the repository admitted, so the audit must report nothing.</summary>
    public static IReadOnlyDictionary<string, Action<Dictionary<string, string>>> Accepted { get; } = new Dictionary<string, Action<Dictionary<string, string>>>(StringComparer.Ordinal)
    {
        ["whitespace-crlf-working-copy"] = s => s[Ui] = s[Ui].Replace("\n", "\r\n", StringComparison.Ordinal),
        ["blazor-webassembly-standalone"] = s => PolicyBaseline.Replace(s, Ui, "<PackageReference Include=\"ArcForges.Contracts.PublicApi\" />", "<PackageReference Include=\"ArcForges.Contracts.PublicApi\" /><PackageReference Include=\"Microsoft.AspNetCore.Components.WebAssembly\" />"),
        ["npm-ci-with-ignored-scripts-exec"] = s => PolicyBaseline.Replace(s, "Directory.Build.targets", "<Project />", "<Project><Target Name=\"Restore\"><Exec Command=\"npm ci --ignore-scripts\" /></Target></Project>"),
        ["admitted-json-codec"] = s => s["src/ArcForges.Web.App/Probe/WireJson.cs"] = "using System.Text.Json;\nnamespace ArcForges.Web.App.Probe;\npublic static class WireJson { public static object? Read(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<object>(bytes); }\n",
        ["admitted-http-probe"] = s => s["src/ArcForges.Web.App/Probe/SessionProbe.cs"] = "namespace ArcForges.Web.App.Probe;\npublic sealed class SessionProbe { public System.Net.Http.HttpRequestMessage Request() => new(System.Net.Http.HttpMethod.Get, \"/\"); }\n",
        ["generated-alias-type"] = s => s["apps/site/app/alias.ts"] = "import type { SayHelloRequest } from \"@arcforges/proto\";\ntype HelloRequest = SayHelloRequest;\ntype OtherRequest = import(\"@arcforges/proto\").SayHelloRequest;\nexport type Keep = HelloRequest | OtherRequest;\n",
        ["comments-and-strings-are-not-code"] = s => s[RootTs] = "// import x from \"@arcforges/private\";\nconst text = \"document @arcforges/private\";\ndocument.title = text;\n",
        ["comment-mentions-json-and-http"] = s => s["src/ArcForges.Web.Ui/Notes.cs"] = "namespace ArcForges.Web.Ui;\n// JsonSerializer and new HttpClient are described here, not used.\npublic static class Notes { }\n",
    };
}
