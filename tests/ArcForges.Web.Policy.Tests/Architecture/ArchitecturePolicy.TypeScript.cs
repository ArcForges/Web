// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ArcForges.Web.Policy.Tests.Architecture;

/// <summary>
/// The TypeScript source rules that remain until the React and TypeScript removal unit. They are the lexical port of
/// the Babel-based GOV.11 rules: specifiers and their resolution, generated wire types only, the fetch and JSON
/// prohibitions, the desktop DOM prohibition and the release route graph from the production roots. Only files
/// reachable from a production root are reported.
/// </summary>
public static partial class ArchitecturePolicy
{
    private static readonly string[] TypeScriptExtensions = [".ts", ".tsx", ".js", ".mjs", ".cjs", ".cts", ".mts"];
    private static readonly string[] AssetExtensions = [".css", ".svg", ".png", ".woff", ".woff2"];
    private static readonly string[] ResolutionSuffixes = [".ts", ".tsx", ".js", ".jsx", "/index.ts", "/index.tsx"];

    private static readonly Regex TsProductionRoot = new(
        @"^(?:apps/[^/]+/app|packages/[^/]+/src|worker)/", RegexOptions.CultureInvariant);
    private static readonly Regex TsStaticSpecifier = new(
        @"\b(?:import|export)\b[^;'""`]*?\bfrom\s*(['""])([^'""\r\n]+)\1", RegexOptions.CultureInvariant);
    private static readonly Regex TsBareImport = new(
        @"(?<![\w$.])import\s*(['""])([^'""\r\n]+)\1", RegexOptions.CultureInvariant);
    private static readonly Regex TsDynamicImport = new(
        @"(?<![\w$.])import\s*\(\s*([^()]*?)\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex TsRequire = new(
        @"(?<![\w$.])require\s*\(\s*([^()]*?)\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex TsLiteralArgument = new(@"^(['""`])([^'""`$]*)\1$", RegexOptions.CultureInvariant);
    private static readonly Regex TsGeneratedImport = new(
        @"import\s+(?:type\s+)?\{([^}]*)\}\s*from\s*['""]@arcforges/(?:proto|api-client)['""]", RegexOptions.CultureInvariant);
    private static readonly Regex TsWireInterface = new(
        @"\b(?:interface|class)\s+(\w*(?:Request|Response|Dto|Message))\b", RegexOptions.CultureInvariant);
    private static readonly Regex TsWireAlias = new(
        @"\btype\s+(\w*(?:Request|Response|Dto|Message))\s*=\s*([^;\r\n]*)", RegexOptions.CultureInvariant);
    private static readonly Regex TsGeneratedAliasTarget = new(
        @"^import\(\s*['""]@arcforges/(?:proto|api-client)['""]\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex TsJsonCodec = new(@"\bJSON\s*(?:\.\s*(?:stringify|parse)\b|\[)", RegexOptions.CultureInvariant);
    private static readonly Regex TsFetch = new(@"(?<![\w$.])fetch\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex TsDesktopDom = new(@"(?<![\w$.])(?:window|document|HTMLElement)\b", RegexOptions.CultureInvariant);
    private static readonly Regex TsDesktopPath = new(@"^packages/desktop[^/]*/", RegexOptions.CultureInvariant);
    private static readonly Regex TsSdkPath = new(@"^packages/(?:[^/]*sdk|sdk)/", RegexOptions.CultureInvariant);
    private static readonly Regex TsSdkSpecifier = new(@"web-ui|react-dom", RegexOptions.CultureInvariant);
    private static readonly Regex TsDesktopSpecifier = new(@"^(?:react|react-dom)(?:/|$)", RegexOptions.CultureInvariant);
    private static readonly Regex TsProtobufSpecifier = new(
        @"^(?:protobufjs|@bufbuild/protobuf/codegen)", RegexOptions.CultureInvariant);

    private sealed class TypeScriptFile
    {
        public List<string> Edges { get; } = [];
        public List<PolicyFinding> Findings { get; } = [];
    }

    private static void AuditTypeScriptSources(PolicyContext context)
    {
        var workspaces = WorkspaceDirectories(context);
        var parsed = new Dictionary<string, TypeScriptFile>(StringComparer.Ordinal);
        foreach (var file in context.Find(name => TypeScriptExtensions.Any(extension => name.EndsWith(extension, StringComparison.Ordinal))))
            parsed[file] = ParseTypeScript(context, file, workspaces);

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(parsed.Keys.Where(file => TsProductionRoot.IsMatch(file)));
        while (pending.Count > 0)
        {
            var file = pending.Pop();
            if (!reachable.Add(file) || !parsed.TryGetValue(file, out var info))
                continue;
            foreach (var edge in info.Edges)
                pending.Push(edge);
        }

        foreach (var file in reachable.Order(StringComparer.Ordinal))
        {
            if (FixturePath.IsMatch(file))
                context.Report("release-fixture", file, "Fixture or test helper reachable from the release route graph.");
            if (parsed.TryGetValue(file, out var info))
                foreach (var finding in info.Findings)
                    context.Report(finding.Rule, finding.File, finding.Detail);
        }
    }

    private static Dictionary<string, string> WorkspaceDirectories(PolicyContext context)
    {
        var workspaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var manifest in context.Find(name => PolicyText.BaseName(name) == "package.json"))
        {
            var name = JsonString(PolicyText.ParseObject(context.Text(manifest)), "name");
            if (name.Length > 0)
                workspaces[name] = PolicyText.DirectoryOf(manifest);
        }
        return workspaces;
    }

    private static TypeScriptFile ParseTypeScript(PolicyContext context, string file, IReadOnlyDictionary<string, string> workspaces)
    {
        var info = new TypeScriptFile();
        var code = PolicyText.StripCode(context.Text(file) ?? string.Empty, templates: true);

        foreach (Match match in TsStaticSpecifier.Matches(code))
            Specifier(context, info, file, match.Groups[2].Value, workspaces);
        foreach (Match match in TsBareImport.Matches(code))
            Specifier(context, info, file, match.Groups[2].Value, workspaces);
        foreach (var pattern in new[] { TsDynamicImport, TsRequire })
            foreach (Match match in pattern.Matches(code))
            {
                var argument = match.Groups[1].Value;
                var literal = TsLiteralArgument.Match(argument);
                if (literal.Success)
                    Specifier(context, info, file, literal.Groups[2].Value, workspaces);
                else
                    info.Findings.Add(new PolicyFinding("computed-import", file, "Release imports must be statically resolved."));
            }

        var generated = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match import in TsGeneratedImport.Matches(code))
            foreach (var item in import.Groups[1].Value.Split(','))
            {
                var parts = item.Split(" as ", StringSplitOptions.TrimEntries);
                var local = parts[^1];
                if (local.Length > 0)
                    generated.Add(local);
            }
        foreach (Match declaration in TsWireInterface.Matches(code))
            info.Findings.Add(new PolicyFinding("wire-source", file, "Wire shapes must come from the published generated package: " + declaration.Groups[1].Value));
        foreach (Match alias in TsWireAlias.Matches(code))
        {
            var target = alias.Groups[2].Value.Trim();
            if (!TsGeneratedAliasTarget.IsMatch(target) && !generated.Contains(target))
                info.Findings.Add(new PolicyFinding("wire-source", file, "Wire shapes must come from the published generated package: " + alias.Groups[1].Value));
        }
        if (TsJsonCodec.IsMatch(code))
            info.Findings.Add(new PolicyFinding("wire-source", file, "Production wire serialization uses published generated codecs; JSON needs explicit HTTP exception admission."));
        if (!file.StartsWith("worker/", StringComparison.Ordinal) && TsFetch.IsMatch(code))
            info.Findings.Add(new PolicyFinding("wire-source", file, "Business network calls must use published generated transport."));
        if (TsDesktopPath.IsMatch(file))
            foreach (Match dom in TsDesktopDom.Matches(code))
                info.Findings.Add(new PolicyFinding("desktop-dom", file, dom.Value));
        return info;
    }

    private static void Specifier(PolicyContext context, TypeScriptFile info, string file, string value, IReadOnlyDictionary<string, string> workspaces)
    {
        if (TsProtobufSpecifier.IsMatch(value))
            info.Findings.Add(new PolicyFinding("wire-source", file, "Production cannot author wire descriptors or codecs."));
        if (NpmPrivateSpecifier.IsMatch(value))
            info.Findings.Add(new PolicyFinding("private-import", file, value));
        if (TsSdkPath.IsMatch(file) && TsSdkSpecifier.IsMatch(value))
            info.Findings.Add(new PolicyFinding("sdk-ui", file, value));
        if (TsDesktopPath.IsMatch(file) && TsDesktopSpecifier.IsMatch(value))
            info.Findings.Add(new PolicyFinding("desktop-dom", file, value));

        var stem = LocalStem(context, file, value, workspaces);
        if (stem is null)
            return;
        if (stem.StartsWith("../", StringComparison.Ordinal))
        {
            info.Findings.Add(new PolicyFinding("private-import", file, "Sibling source escape: " + value));
            return;
        }
        var stripped = stem.EndsWith(".js", StringComparison.Ordinal) ? stem[..^3] : stem;
        var candidates = new[] { stem }.Concat(ResolutionSuffixes.Select(suffix => stripped + suffix)).ToArray();
        var resolved = candidates.FirstOrDefault(candidate => context.Sources.ContainsKey(candidate));
        if (resolved is null)
        {
            if (!value.Contains("+types/", StringComparison.Ordinal) && !AssetExtensions.Any(extension => value.EndsWith(extension, StringComparison.Ordinal)))
                info.Findings.Add(new PolicyFinding("unresolved-import", file, value));
            return;
        }
        info.Edges.Add(resolved);
    }

    private static string? LocalStem(PolicyContext context, string file, string value, IReadOnlyDictionary<string, string> workspaces)
    {
        if (value.StartsWith('.'))
            return PolicyText.Join(PolicyText.DirectoryOf(file), value);
        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            var segments = file.Split('/');
            return PolicyText.Join(string.Join('/', segments.Take(2)) + "/app", value[2..]);
        }
        foreach (var (name, directory) in workspaces)
        {
            if (value != name && !value.StartsWith(name + "/", StringComparison.Ordinal))
                continue;
            var manifest = PolicyText.ParseObject(context.Text(PolicyText.ManifestPath(directory)));
            var exports = manifest?["exports"];
            var key = value == name ? "." : "." + value[name.Length..];
            string? mapped = exports is JsonValue single && single.TryGetValue(out string? text)
                ? text
                : (exports as JsonObject)?[key]?.ToJsonString().Trim('"');
            if (mapped is not null)
                return PolicyText.Join(directory, mapped);
        }
        return null;
    }
}
