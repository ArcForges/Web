// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace ArcForges.Web.Policy.Tests.Architecture;

/// <summary>
/// The MSBuild rules: obsolete Blazor targets, portable references, production and install commands, restore sources,
/// imports, the Apache-to-AGPL project graph and the release route graph.
/// </summary>
public static partial class ArchitecturePolicy
{
    /// <summary>Paths that name test, fixture or double code. Such files must never be in the release route graph.</summary>
    internal static readonly Regex FixturePath = new(
        @"(?:^|/)(?:tests?|__tests__|fixtures?|test-?helpers|testdoubles)(?:/|\.)|\.(?:test|spec)\.|(?:^|/)[^/]*(?:Fixtures?|TestDoubles?)\.(?:cs|razor)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectReferenceInclude = new(
        "<ProjectReference\\s[^>]*?Include=[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant);
    private static readonly Regex NonLiteralReference = new(@"[$@%*?;]", RegexOptions.CultureInvariant);
    private static readonly Regex BlazorServerTarget = new(
        @"AddInteractiveServer|InteractiveServer|Microsoft\.AspNetCore\.Components\.Server\b|Microsoft\.AspNetCore\.Components\.WebAssembly\.Server\b|AddRazorComponents|MapRazorComponents|\bCircuit\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex DevelopmentServer = new(
        "<SpaProxy(?:ServerUrl|LaunchCommand)\\b|Microsoft\\.AspNetCore\\.SpaProxy", RegexOptions.CultureInvariant);
    private static readonly Regex ExecCommand = new("<Exec\\b[^>]*?\\bCommand=\"([^\"]*)\"", RegexOptions.CultureInvariant);
    private static readonly Regex ExecForbidden = new(
        @"\b(?:npm\s+(?:install|i|exec)|npx\b|dotnet\s+(?:restore|add)|(?:vite|react-router|wrangler)\s+(?:dev|serve)|--hmr)\b|\bnpm\s+ci\b(?![^""]*--ignore-scripts)",
        RegexOptions.CultureInvariant);
    private static readonly Regex DevelopmentBuildCommand = new(
        @"(?:\b(?:install|ci|dev|serve)\b|--hmr)", RegexOptions.CultureInvariant);
    private static readonly Regex NpmInstallSwitch = new(
        "<ShouldRunNpmInstall>\\s*(?!false\\s*<)[^<]+", RegexOptions.CultureInvariant);
    private static readonly Regex BuildCommandElement = new("<BuildCommand>[^<]*", RegexOptions.CultureInvariant);
    private static readonly Regex LockRestoreDisabled = new(
        "<(?:RestorePackagesWithLockFile|RestoreLockedMode)>\\s*false\\s*<", RegexOptions.CultureInvariant);
    private static readonly Regex AlternateRestoreSource = new(
        "<(?:RestoreSources|RestoreAdditionalProjectSources)\\b", RegexOptions.CultureInvariant);
    private static readonly Regex UsingInclude = new("<Using\\s[^>]*?Include=\"([^\"]+)\"", RegexOptions.CultureInvariant);
    private static readonly Regex LicenceBoundaryValue = new(
        "<LicenceBoundary(?:\\s[^>]*)?>([^<]*)</LicenceBoundary>", RegexOptions.CultureInvariant);
    private static readonly Regex ReleaseItem = new(
        "<(?:Compile|Content|None|EmbeddedResource)\\s[^>]*?Include=\"([^\"]+)\"", RegexOptions.CultureInvariant);
    private static readonly Regex PrivateNamespace = new(
        @"^ArcForges(?:\.[\w]+)*\.(?:Private|Server|LocalRpc|Internal|Operator|AIInternal|Storage)(?:\.|$)|^ArcForges\.Contracts\.(?:Internal|Storage|AIInternal)(?:\.|$)",
        RegexOptions.CultureInvariant);

    private static readonly string[] ReleaseRoots = ["src/"];

    private static void AuditMsBuildProjects(PolicyContext context)
    {
        var projects = context.Find(name => name.EndsWith(".csproj", StringComparison.Ordinal)
            || name.EndsWith(".esproj", StringComparison.Ordinal)
            || name.EndsWith(".props", StringComparison.Ordinal)
            || name.EndsWith(".targets", StringComparison.Ordinal));
        var targets = context.Find(name => name.EndsWith(".slnx", StringComparison.Ordinal)
            || name.EndsWith(".csproj", StringComparison.Ordinal)
            || name.EndsWith(".esproj", StringComparison.Ordinal)
            || name.EndsWith(".props", StringComparison.Ordinal)
            || name.EndsWith(".targets", StringComparison.Ordinal));

        foreach (var file in targets)
        {
            var xml = PolicyText.StripXmlComments(context.Text(file) ?? string.Empty);
            if (BlazorServerTarget.IsMatch(xml))
                context.Report("obsolete-target", file, "Blazor Server render modes and circuits are forbidden; Blazor WebAssembly standalone is the only Blazor target.");
            if (DevelopmentServer.IsMatch(xml))
                context.Report("production-command", file, "A development server (SpaProxy) is not allowed in a production project.");
            if (LockRestoreDisabled.IsMatch(xml))
                context.Report("workspace", file, "Implicit restore is refused: restore uses the locked NuGet closure.");
            if (AlternateRestoreSource.IsMatch(xml))
                context.Report("workspace", file, "Alternate restore sources are not allowed; the nuget.org source is the only one.");
        }

        foreach (var file in projects)
        {
            var xml = PolicyText.StripXmlComments(context.Text(file) ?? string.Empty);
            if (file.EndsWith(".esproj", StringComparison.Ordinal) || file.EndsWith(".props", StringComparison.Ordinal) || file.EndsWith(".targets", StringComparison.Ordinal))
            {
                if (NpmInstallSwitch.IsMatch(xml))
                    context.Report("production-command", file, "Implicit IDE npm install is not allowed.");
            }
            foreach (var build in BuildCommandElement.Matches(xml).Select(match => match.Value))
                if (DevelopmentBuildCommand.IsMatch(build))
                    context.Report("production-command", file, "The build command installs or starts a development server.");
            foreach (Match exec in ExecCommand.Matches(xml))
                if (ExecForbidden.IsMatch(exec.Groups[1].Value))
                    context.Report("production-command", file, "An Exec command installs, restores or starts a development server implicitly.");
            foreach (Match use in UsingInclude.Matches(xml))
                if (PrivateNamespace.IsMatch(use.Groups[1].Value))
                    context.Report("private-import", file, "Private or server namespace: " + use.Groups[1].Value);
            foreach (Match package in Regex.Matches(xml, "<Package(?:Reference|Version)\\s[^>]*?(?:Include|Update)=\"([^\"]+)\"", RegexOptions.CultureInvariant))
                if (PrivateNamespace.IsMatch(package.Groups[1].Value))
                    context.Report("private-import", file, "Private or server package: " + package.Groups[1].Value);
            if (!file.EndsWith(".csproj", StringComparison.Ordinal) && !file.EndsWith(".props", StringComparison.Ordinal) && !file.EndsWith(".targets", StringComparison.Ordinal))
                continue;
            foreach (Match reference in ProjectReferenceInclude.Matches(xml))
            {
                var include = reference.Groups[1].Value;
                if (NonLiteralReference.IsMatch(include))
                {
                    context.Report("computed-import", file, "Nonliteral project reference: " + include);
                    continue;
                }
                if (include.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase))
                {
                    context.Report("portable-reference", file, "Managed graph references esproj: " + include);
                    continue;
                }
                var target = ProjectTarget(file, include);
                if (!context.Sources.ContainsKey(target) && !target.EndsWith(".esproj", StringComparison.Ordinal))
                    context.Report("unresolved-import", file, "Project reference does not resolve: " + include);
            }
        }

        AuditLicenceBoundaryGraph(context, projects);
    }

    /// <summary>An Apache-declared project must never reach a project of another boundary through project references.</summary>
    private static void AuditLicenceBoundaryGraph(PolicyContext context, IReadOnlyList<string> projects)
    {
        var boundaries = new Dictionary<string, string>(StringComparer.Ordinal);
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var project in projects.Where(name => name.EndsWith(".csproj", StringComparison.Ordinal)))
        {
            var xml = PolicyText.StripXmlComments(context.Text(project) ?? string.Empty);
            boundaries[project] = LicenceBoundaryValue.Match(xml).Groups[1].Value;
            edges[project] = ProjectReferenceInclude.Matches(xml)
                .Select(match => match.Groups[1].Value)
                .Where(include => !NonLiteralReference.IsMatch(include) && !include.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase))
                .Select(include => ProjectTarget(project, include))
                .ToList();
        }
        foreach (var (project, boundary) in boundaries)
        {
            if (boundary != "Apache")
                continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(edges[project]);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!seen.Add(current) || current == project)
                    continue;
                if (boundaries.TryGetValue(current, out var reached) && reached != "Apache")
                    context.Report("sdk-ui", project, "Apache project reaches " + current + " with boundary " + reached);
                if (edges.TryGetValue(current, out var next))
                    foreach (var target in next)
                        pending.Push(target);
            }
        }
    }

    /// <summary>
    /// The release route graph: every project reachable from a source project through project references must be
    /// production code, and no file of a reached project may be a test, fixture or double.
    /// </summary>
    private static void AuditReleaseGraph(PolicyContext context)
    {
        var roots = context.Find(name => name.EndsWith(".csproj", StringComparison.Ordinal)
            && ReleaseRoots.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots);
        while (pending.Count > 0)
        {
            var project = pending.Pop();
            if (!seen.Add(project))
                continue;
            if (FixturePath.IsMatch(project))
                context.Report("release-fixture", project, "Test or fixture project reachable from the release route graph.");
            var directory = PolicyText.DirectoryOf(project) + "/";
            var xml = PolicyText.StripXmlComments(context.Text(project) ?? string.Empty);
            foreach (Match item in ReleaseItem.Matches(xml))
                if (item.Groups[1].Value.Contains("..", StringComparison.Ordinal) || FixturePath.IsMatch(item.Groups[1].Value))
                    context.Report("release-fixture", project, "Release item reaches outside production code: " + item.Groups[1].Value);
            foreach (var file in context.Find(name => name.StartsWith(directory, StringComparison.Ordinal)
                && (name.EndsWith(".cs", StringComparison.Ordinal) || name.EndsWith(".razor", StringComparison.Ordinal))))
                if (FixturePath.IsMatch(file))
                    context.Report("release-fixture", file, "Fixture or test helper in the release route graph.");
            foreach (Match reference in ProjectReferenceInclude.Matches(xml))
            {
                var include = reference.Groups[1].Value;
                if (NonLiteralReference.IsMatch(include) || include.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase))
                    continue;
                var target = ProjectTarget(project, include);
                if (context.Sources.ContainsKey(target))
                    pending.Push(target);
            }
        }
    }

    private static string ProjectTarget(string project, string include) =>
        PolicyText.Join(PolicyText.DirectoryOf(project), include.Replace('\\', '/'));
}
