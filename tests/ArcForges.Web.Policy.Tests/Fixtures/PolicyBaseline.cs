// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;

namespace ArcForges.Web.Policy.Tests.Fixtures;

/// <summary>
/// The passing in-memory Web repository of the policy fixtures. It has the same shape as the real repository (one
/// solution, central packages, one locked C# project, one npm workspace, one TypeScript production root) and no
/// finding of any rule. Each refusal fixture changes one copy of it.
/// </summary>
public static class PolicyBaseline
{
    private const string Solution = """
        <Solution>
          <Folder Name="/src/">
            <Project Path="src/ArcForges.Web.Ui/ArcForges.Web.Ui.csproj" />
          </Folder>
        </Solution>
        """;

    private const string NuGetConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </packageSources>
        </configuration>
        """;

    private const string BuildProps = """
        <Project>
          <PropertyGroup>
            <LicenceBoundary>AGPL</LicenceBoundary>
            <PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>
            <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
          </PropertyGroup>
        </Project>
        """;

    private const string CentralPackages = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="ArcForges.Contracts.PublicApi" Version="1.0.0-ci.287.1" />
          </ItemGroup>
        </Project>
        """;

    private const string UiProject = """
        <Project Sdk="Microsoft.NET.Sdk.Razor">
          <ItemGroup>
            <PackageReference Include="ArcForges.Contracts.PublicApi" />
          </ItemGroup>
          <PropertyGroup>
            <LicenceBoundary>AGPL</LicenceBoundary>
            <PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>
          </PropertyGroup>
        </Project>
        """;

    private const string UiLock = """
        {"version":2,"dependencies":{"net10.0":{"ArcForges.Contracts.PublicApi":{"type":"Direct","requested":"[1.0.0-ci.287.1, )","resolved":"1.0.0-ci.287.1"}}}}
        """;

    private const string RootManifest = """
        {"name":"web","workspaces":["apps/site"],"packageManager":"npm@11.19.0","engines":{"node":">=24.21.0 <25","npm":">=11.19.0 <12"},"devDependencies":{"typescript":"7.0.2","wrangler":"4.143.1"},"license":"AGPL-3.0-only","arcforges":{"licenceBoundary":"AGPL"}}
        """;

    private const string SiteManifest = """
        {"name":"site","license":"AGPL-3.0-only","arcforges":{"licenceBoundary":"AGPL"}}
        """;

    private const string RootLock = """
        {"lockfileVersion":3,"packages":{"":{"devDependencies":{"typescript":"7.0.2","wrangler":"4.143.1"}},"apps/site":{}}}
        """;

    private const string Inventory = """
        {"schemaVersion":1,"repository":"Web","spdxLicense":"AGPL-3.0-only","licenceBoundary":"AGPL","projects":[{"path":"apps/site/package.json","kind":"npm"},{"path":"package.json","kind":"npm"},{"path":"src/ArcForges.Web.Ui/ArcForges.Web.Ui.csproj","kind":"msbuild"}]}
        """;

    /// <summary>Returns a new mutable copy of the passing repository.</summary>
    public static Dictionary<string, string> Create()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["win.slnx"] = Solution,
            ["global.json"] = """{"sdk":{"version":"10.0.401","rollForward":"disable","allowPrerelease":false},"test":{"runner":"Microsoft.Testing.Platform"}}""",
            ["NuGet.config"] = NuGetConfig,
            ["Directory.Build.props"] = BuildProps,
            ["Directory.Build.targets"] = "<Project />",
            ["Directory.Packages.props"] = CentralPackages,
            [".node-version"] = "24.21.0\n",
            ["package.json"] = RootManifest,
            ["package-lock.json"] = RootLock,
            ["apps/site/package.json"] = SiteManifest,
            ["apps/site/app/root.ts"] = "import type { Message } from \"@arcforges/proto\";\n\ndocument.title = \"Web DOM is allowed\";\n",
            ["eng/policy/licence-boundary.json"] = Inventory,
            ["src/ArcForges.Web.Ui/ArcForges.Web.Ui.csproj"] = UiProject,
            ["src/ArcForges.Web.Ui/packages.lock.json"] = UiLock,
            ["src/ArcForges.Web.Ui/Greeting.cs"] = "namespace ArcForges.Web.Ui;\n\npublic sealed class Greeting\n{\n}\n",
            ["src/ArcForges.Web.Ui/Greeting.razor"] = "<p>Hello</p>\n",
        };
        // A file on disk ends with a line feed; C# raw literals omit the final line break of their content.
        foreach (var file in sources.Keys.ToArray())
            if (!sources[file].EndsWith('\n'))
                sources[file] += "\n";
        return sources;
    }

    /// <summary>Edits one JSON file of a copy in place.</summary>
    public static void EditJson(Dictionary<string, string> sources, string file, Action<JsonObject> edit)
    {
        var document = JsonNode.Parse(sources[file])!.AsObject();
        edit(document);
        sources[file] = document.ToJsonString() + "\n";
    }

    /// <summary>Edits one XML-like file of a copy by replacing a fragment, failing when the fragment is absent.</summary>
    public static void Replace(Dictionary<string, string> sources, string file, string from, string to)
    {
        var text = sources[file];
        if (!text.Contains(from, StringComparison.Ordinal))
            throw new InvalidOperationException("The fixture fragment is absent from " + file + ": " + from);
        sources[file] = text.Replace(from, to, StringComparison.Ordinal);
    }
}
