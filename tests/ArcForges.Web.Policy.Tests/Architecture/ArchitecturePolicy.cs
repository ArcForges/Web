// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ArcForges.Web.Policy.Tests.Architecture;

/// <summary>
/// The Web architecture rules of the GOV.11 successor: repository shape, exact pins, npm manifests and scripts, MSBuild
/// targets, C# and TypeScript import and wire rules, desktop scope and the release route graph. Each rule family
/// reports through <see cref="PolicyContext"/>; the fixtures in the test project keep one passing and one failing
/// example for every rule.
/// </summary>
public static partial class ArchitecturePolicy
{
    internal static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+(?:-[\w.-]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex ExactSdk = new(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex ExactDownload = new(@"^\[\d+\.\d+\.\d+(?:-[\w.-]+)?\]$", RegexOptions.CultureInvariant);
    private static readonly Regex NpmManagerPin = new(@"^npm@\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex NpmImplicitInstall = new(
        @"\b(?:npm\s+(?:install|i|ci)|npx\b|(?:vite|react-router|wrangler)\s+dev|--hmr)\b", RegexOptions.CultureInvariant);
    private static readonly Regex NpmProductionReach = new(
        @"\b(?:npm\s+(?:install|i|ci|exec)|npx\b|(?:vite|react-router|wrangler)\s+(?:dev|serve)|--hmr)\b", RegexOptions.CultureInvariant);
    private static readonly Regex NpmRunCall = new(@"\bnpm\s+run\s+([\w:-]+)", RegexOptions.CultureInvariant);
    private static readonly Regex NpmWorkspaceFlag = new(@"(?:--workspace(?:=|\s+)|-w\s+)([\w@/.-]+)", RegexOptions.CultureInvariant);
    private static readonly Regex ManifestSpecialWorkspace = new(@"[*?\\]|^\.", RegexOptions.CultureInvariant);
    private static readonly Regex PrivateNpmOwner = new(@"^@arcforges/(?!proto$|api-client$|web-)", RegexOptions.CultureInvariant);
    private static readonly Regex NpmPrivateSpecifier = new(
        @"^@arcforges/(?!proto$|api-client$|web-)|(?:^|/)(?:private|server|local-rpc|internal)(?:/|$)|^(?:https?:|file:|[A-Za-z]:[\\/])",
        RegexOptions.CultureInvariant);
    private static readonly string[] DependencySections =
        ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];
    private static readonly string[] DevelopmentScripts = ["dev", "preview", "test:e2e"];
    private static readonly string[] NpmLockNames =
        ["package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "bun.lock", "bun.lockb"];
    private static readonly string[] SolutionFiles = ["win.slnx"];
    private static readonly string[] PackageLockOnly = ["package-lock.json"];

    /// <summary>Audits the repository text inputs and returns every refusal, in a stable order.</summary>
    public static IReadOnlyList<PolicyFinding> Audit(IReadOnlyDictionary<string, string> sources)
    {
        var context = new PolicyContext(sources);
        AuditWorkspace(context);
        AuditPins(context);
        AuditNpmManifests(context);
        AuditMsBuildProjects(context);
        AuditReleaseGraph(context);
        AuditCSharpSources(context);
        AuditTypeScriptSources(context);
        AuditWhitespace(context);
        return context.Findings
            .OrderBy(finding => finding.File, StringComparer.Ordinal)
            .ThenBy(finding => finding.Rule, StringComparer.Ordinal)
            .ThenBy(finding => finding.Detail, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AuditWorkspace(PolicyContext context)
    {
        var solutions = context.Find(name => name.EndsWith(".sln", StringComparison.Ordinal)
            || name.EndsWith(".slnx", StringComparison.Ordinal));
        if (!solutions.SequenceEqual(SolutionFiles, StringComparer.Ordinal))
            context.Report("workspace", "win.slnx", "Exactly one solution, win.slnx, is allowed.");

        foreach (var required in new[]
                 {
                     "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                     "NuGet.config", ".node-version",
                 })
            RequireRootFile(context, required);

        foreach (var file in context.Find(name => string.Equals(PolicyText.BaseName(name), "packages.config", StringComparison.OrdinalIgnoreCase)))
            context.Report("workspace", file, "Legacy packages.config is not allowed; use central package management.");

        var nugetConfig = context.Text("NuGet.config") ?? string.Empty;
        var nugetSources = Regex.Matches(nugetConfig, "<add\\s[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => PolicyText.Attribute(match.Value, "value"))
            .ToArray();
        if (!Regex.IsMatch(nugetConfig, "<clear\\s*/>", RegexOptions.CultureInvariant)
            || nugetSources.Length != 1
            || nugetSources[0] != "https://api.nuget.org/v3/index.json")
            context.Report("workspace", "NuGet.config", "Only the nuget.org source, after a clear, is admitted.");

        var global = PolicyText.ParseObject(context.Text("global.json"));
        var sdk = global?["sdk"] as JsonObject;
        if (sdk is null
            || !ExactSdk.IsMatch(JsonString(sdk, "version"))
            || JsonString(sdk, "rollForward") != "disable"
            || JsonString(sdk, "allowPrerelease") != "false")
            context.Report("pins", "global.json", "An exact .NET SDK pin with rollForward disabled is required.");

        foreach (var project in context.Find(name => name.EndsWith(".csproj", StringComparison.Ordinal)))
            if (!context.Sources.ContainsKey(SiblingPath(project, "packages.lock.json")))
                context.Report("workspace", project, "Every C# project needs its sibling packages.lock.json.");

        foreach (var locked in context.Find(name => PolicyText.BaseName(name) == "packages.lock.json"))
            if (!HasSiblingProject(context, locked))
                context.Report("workspace", locked, "A packages.lock.json without a sibling C# project is an orphan.");

        var npmLocks = context.Find(name => NpmLockNames.Contains(PolicyText.BaseName(name), StringComparer.Ordinal));
        if (!npmLocks.SequenceEqual(PackageLockOnly, StringComparer.Ordinal))
            context.Report("workspace", "package-lock.json", "One root npm v3 lock, and no alternate npm lock, is allowed.");
        var npmLock = PolicyText.ParseObject(context.Text("package-lock.json"));
        if (npmLock is null || JsonString(npmLock, "lockfileVersion") != "3")
            context.Report("workspace", "package-lock.json", "The npm lock must be lockfileVersion 3.");

        var root = PolicyText.ParseObject(context.Text("package.json"));
        if (root is null)
        {
            context.Report("workspace", "package.json", "The root package.json is missing or malformed.");
            return;
        }
        if (root["workspaces"] is not JsonArray workspaceArray)
        {
            context.Report("workspace", "package.json", "One explicit root workspace array is required.");
            return;
        }
        var workspaces = workspaceArray.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();
        var expected = new[] { "package.json" }.Concat(workspaces.Select(name => name + "/package.json"))
            .Order(StringComparer.Ordinal).ToArray();
        var manifests = context.Find(name => PolicyText.BaseName(name) == "package.json");
        if (!expected.SequenceEqual(manifests.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || workspaces.Any(name => ManifestSpecialWorkspace.IsMatch(name)))
            context.Report("workspace", "package.json", "Workspace manifest inventory must be exact.");

        foreach (var manifest in manifests)
        {
            var parsed = PolicyText.ParseObject(context.Text(manifest));
            if (parsed is null)
            {
                context.Report("workspace", manifest, "The npm manifest is malformed.");
                continue;
            }
            if (manifest != "package.json" && parsed["workspaces"] is not null)
                context.Report("workspace", manifest, "Nested workspace.");
            var entry = npmLock?["packages"]?[PolicyText.DirectoryOf(manifest)] as JsonObject;
            foreach (var section in DependencySections)
                if (SectionText(parsed, section) != SectionText(entry, section))
                    context.Report("workspace", manifest, "Lock differs: " + section);
        }
    }

    private static void RequireRootFile(PolicyContext context, string name)
    {
        var found = context.Find(file => string.Equals(PolicyText.BaseName(file), name, StringComparison.OrdinalIgnoreCase));
        if (found.Count != 1 || found[0] != name)
            context.Report("workspace", name, "Exactly one root " + name + " is required.");
    }

    private static void AuditPins(PolicyContext context)
    {
        var node = context.Text(".node-version")?.Trim();
        if (node is null || !ExactVersion.IsMatch(node))
            context.Report("pins", ".node-version", "An exact Node pin is required.");

        var root = PolicyText.ParseObject(context.Text("package.json"));
        var devDependencies = root?["devDependencies"] as JsonObject;
        if (!ExactVersion.IsMatch(JsonString(devDependencies, "typescript"))
            || !ExactVersion.IsMatch(JsonString(devDependencies, "wrangler")))
            context.Report("pins", "package.json", "Exact compiler and static generator pins are required.");

        var packageManager = JsonString(root, "packageManager");
        var engines = root?["engines"] as JsonObject;
        if (node is null
            || !ExactVersion.IsMatch(node)
            || !NpmManagerPin.IsMatch(packageManager)
            || !JsonString(engines, "node").Contains(node, StringComparison.Ordinal)
            || !JsonString(engines, "npm").Contains(packageManager[4..], StringComparison.Ordinal))
            context.Report("pins", "package.json", "Exact Node and npm pins with matching engines are required.");

        foreach (var file in context.Find(name => name.EndsWith(".props", StringComparison.Ordinal)
            || name.EndsWith(".csproj", StringComparison.Ordinal)
            || name.EndsWith(".targets", StringComparison.Ordinal)))
            foreach (Match download in Regex.Matches(context.Text(file) ?? string.Empty, "<PackageDownload\\b([^>]*)>", RegexOptions.CultureInvariant))
                if (!ExactDownload.IsMatch(PolicyText.Attribute(download.Value, "Version") ?? string.Empty))
                    context.Report("pins", file, "Every PackageDownload must name an exact bracketed version.");

        var central = context.Text("Directory.Packages.props") ?? string.Empty;
        if (!Regex.IsMatch(central, "<ManagePackageVersionsCentrally>\\s*true\\s*<", RegexOptions.CultureInvariant))
            context.Report("pins", "Directory.Packages.props", "Central package management must be enabled.");
        foreach (Match version in Regex.Matches(central, "<PackageVersion\\b[^>]*>", RegexOptions.CultureInvariant))
            if (!ExactVersion.IsMatch(PolicyText.Attribute(version.Value, "Version") ?? string.Empty))
                context.Report("pins", "Directory.Packages.props", "Every central PackageVersion must be exact: " + PolicyText.Attribute(version.Value, "Include"));

        foreach (var file in context.Find(name => name.EndsWith(".csproj", StringComparison.Ordinal)
            || name.EndsWith(".props", StringComparison.Ordinal)
            || name.EndsWith(".targets", StringComparison.Ordinal)))
        {
            var xml = PolicyText.StripXmlComments(context.Text(file) ?? string.Empty);
            if (Regex.IsMatch(xml, "<PackageReference\\b[^>]*\\bVersion\\s*=", RegexOptions.CultureInvariant)
                || Regex.IsMatch(xml, "<PackageReference\\b[^/>]*>[\\s\\S]*?<Version>", RegexOptions.CultureInvariant))
                context.Report("pins", file, "PackageReference must not carry a version; versions are central.");
        }
    }

    private static void AuditNpmManifests(PolicyContext context)
    {
        var manifests = context.Find(name => PolicyText.BaseName(name) == "package.json");
        var parsed = manifests.ToDictionary(name => name, name => PolicyText.ParseObject(context.Text(name)), StringComparer.Ordinal);
        var workspaceByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            var name = JsonString(parsed[manifest], "name");
            if (name.Length > 0)
                workspaceByName[name] = PolicyText.DirectoryOf(manifest);
        }

        foreach (var manifest in manifests)
        {
            var document = parsed[manifest];
            if (document is null)
                continue;
            foreach (var section in DependencySections)
                foreach (var (dependency, version) in Entries(document[section]))
                {
                    var value = version?.ToJsonString().Trim('"') ?? string.Empty;
                    if (!ExactVersion.IsMatch(value))
                        context.Report("pins", manifest, "Nonexact dependency " + dependency);
                    if (dependency.Contains("blazor", StringComparison.OrdinalIgnoreCase))
                        context.Report("obsolete-target", manifest, dependency);
                    if (PrivateNpmOwner.IsMatch(dependency))
                        context.Report("private-import", manifest, dependency);
                    if (JsonString(document, "license") == "Apache-2.0"
                        && dependency is "@arcforges/web-ui" or "react-dom")
                        context.Report("sdk-ui", manifest, dependency);
                }
            foreach (var (script, command) in Entries(document["scripts"]))
                if (!DevelopmentScripts.Contains(script, StringComparer.Ordinal)
                    && NpmImplicitInstall.IsMatch(command?.ToJsonString().Trim('"') ?? string.Empty))
                    context.Report("production-command", manifest, script + ": implicit install or development server");
            if (manifest != "package.json" && document["workspaces"] is not null)
                context.Report("workspace", manifest, "Nested workspace.");
        }

        foreach (var manifest in manifests)
        {
            var document = parsed[manifest];
            if (document is null)
                continue;
            foreach (var (script, _) in Entries(document["scripts"]))
                if (!DevelopmentScripts.Contains(script, StringComparer.Ordinal))
                    CheckNpmScript(context, parsed, workspaceByName, manifest, script, []);
        }

        foreach (var manifest in manifests)
        {
            var document = parsed[manifest];
            if (document is null || JsonString(document, "license") != "Apache-2.0")
                continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(manifest);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!seen.Add(current))
                    continue;
                var currentDocument = parsed.TryGetValue(current, out var found) ? found : PolicyText.ParseObject(context.Text(current));
                if (JsonString(currentDocument, "license") != "Apache-2.0")
                    context.Report("sdk-ui", manifest, "Apache workspace reaches " + current);
                foreach (var section in DependencySections)
                    foreach (var (dependency, _) in Entries(currentDocument?[section]))
                        if (workspaceByName.TryGetValue(dependency, out var directory))
                            pending.Push(PolicyText.ManifestPath(directory));
            }
        }
    }

    private static void CheckNpmScript(
        PolicyContext context,
        IReadOnlyDictionary<string, JsonObject?> manifests,
        IReadOnlyDictionary<string, string> workspaceByName,
        string file,
        string name,
        HashSet<string> ancestors)
    {
        var key = file + ":" + name;
        if (ancestors.Contains(key))
        {
            context.Report("production-command", file, "Script cycle: " + key);
            return;
        }
        var scripts = manifests.TryGetValue(file, out var manifest) ? manifest?["scripts"] as JsonObject : null;
        if (scripts?[name]?.GetValue<string>() is not { } script)
        {
            context.Report("production-command", file, "Unknown script: " + name);
            return;
        }
        if (NpmProductionReach.IsMatch(script))
            context.Report("production-command", file, "Production script reaches dev/install: " + name);
        var next = new HashSet<string>(ancestors, StringComparer.Ordinal) { key };
        foreach (var command in script.Split(["&&", "||", ";"], StringSplitOptions.None))
        {
            var call = NpmRunCall.Match(command);
            if (!call.Success)
                continue;
            var workspace = NpmWorkspaceFlag.Match(command);
            string? directory = null;
            if (workspace.Success && !workspaceByName.TryGetValue(workspace.Groups[1].Value, out directory))
            {
                context.Report("production-command", file, "Unknown script workspace: " + workspace.Groups[1].Value);
                continue;
            }
            var target = workspace.Success ? PolicyText.ManifestPath(directory!) : file;
            CheckNpmScript(context, manifests, workspaceByName, target, call.Groups[1].Value, next);
        }
    }

    /// <summary>Returns the JSON text of one dependency section, or the empty object text when it is absent.</summary>
    private static string SectionText(JsonObject? document, string section) =>
        document?[section] is { } node ? node.ToJsonString() : "{}";

    /// <summary>Returns a string member, or the literal text of a number or boolean member, or the empty string.</summary>
    private static string JsonString(JsonNode? document, string name)
    {
        var node = document?[name];
        if (node is not JsonValue value)
            return string.Empty;
        return value.TryGetValue(out string? text) ? text ?? string.Empty : value.ToJsonString();
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Entries(JsonNode? node) =>
        node is JsonObject entries ? entries : Array.Empty<KeyValuePair<string, JsonNode?>>();

    private static string SiblingPath(string file, string name)
    {
        var directory = PolicyText.DirectoryOf(file);
        return directory.Length == 0 ? name : directory + "/" + name;
    }

    private static bool HasSiblingProject(PolicyContext context, string lockPath) =>
        context.Find(name => name.EndsWith(".csproj", StringComparison.Ordinal)
            && PolicyText.DirectoryOf(name) == PolicyText.DirectoryOf(lockPath)).Count > 0;
}
