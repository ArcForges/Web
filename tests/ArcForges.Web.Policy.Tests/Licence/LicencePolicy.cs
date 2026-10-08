// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArcForges.Web.Policy.Tests.Architecture;

namespace ArcForges.Web.Policy.Tests.Licence;

/// <summary>
/// The licence-boundary rules of the GOV.11 successor, over the same inventory file (eng/policy/licence-boundary.json).
/// The inventory must match the tracked project files exactly; each project declares the AGPL boundary itself; imported
/// overrides, escaped or unregistered references, non-literal references, unknown first-party owners and unpublished
/// sources are refused. Comments are removed before any value is read, so a comment cannot join two fragments.
/// </summary>
public static class LicencePolicy
{
    private const string InventoryPath = "eng/policy/licence-boundary.json";
    private static readonly string[] MsBuildExtensions = [".csproj", ".fsproj", ".vbproj", ".vcxproj", ".esproj"];
    private static readonly string[] ImportedExtensions = [".props", ".targets"];
    private static readonly string[] SectionNames = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];
    private static readonly string[] InventoryKeys = ["licenceBoundary", "projects", "repository", "schemaVersion", "spdxLicense"];

    private static readonly HashSet<string> NpmOwners = new(StringComparer.Ordinal)
    {
        "@arcforges/proto", "@arcforges/api-client", "@arcforges/contract-fixtures", "@arcforges/ai-internal",
        "@arcforges/ai", "@arcforges/cloud-workspace", "@arcforges/web-workspace", "@arcforges/web-app",
        "@arcforges/web-site", "@arcforges/web-ui",
    };

    private static readonly HashSet<string> ArcForgesOwners = new(StringComparer.Ordinal)
    {
        "arcforges.contracts.publicapi", "arcforges.contracts.foundation", "arcforges.build.policy",
    };

    private static readonly HashSet<string> GithubOwners = new(StringComparer.Ordinal)
    {
        "io.github.arcforges:contracts-proto", "io.github.arcforges:contracts-client",
        "io.github.arcforges:contracts-connect-client", "io.github.arcforges:contract-fixtures",
    };

    private static readonly Regex ProjectInclude = new(
        "<ProjectReference\\s[^>]*?Include=[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant);
    private static readonly Regex PackageIdentity = new(
        "<Package(?:Reference|Version)\\s[^>]*?(?:Include|Update)=[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant);
    private static readonly Regex NonLiteral = new(@"[$@*?;]", RegexOptions.CultureInvariant);
    private static readonly Regex UnpublishedSource = new(@"^(?:file:|link:|git\+|\.\.?/)", RegexOptions.CultureInvariant);
    private static readonly Regex GradleSource = new(@"includeBuild\s*\(|mavenLocal\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex GradleProject = new(@"\bproject\(\s*[""']:", RegexOptions.CultureInvariant);
    private static readonly Regex GradleOwner = new(@"io\.github\.arcforges:[A-Za-z0-9_.-]+", RegexOptions.CultureInvariant);
    private static readonly Regex GradleFile = new(@"^(?:build|settings)\.gradle(?:\.kts)?$", RegexOptions.CultureInvariant);

    /// <summary>Audits the licence boundary of every audited project and returns every refusal.</summary>
    public static IReadOnlyList<PolicyFinding> Audit(IReadOnlyDictionary<string, string> sources)
    {
        var findings = new List<PolicyFinding>();
        void Report(string rule, string file, string detail) => findings.Add(new PolicyFinding(rule, file, detail));

        var policy = PolicyText.ParseObject(sources.GetValueOrDefault(InventoryPath));
        if (policy is null)
        {
            Report("inventory", InventoryPath, "The licence inventory is missing or malformed.");
            return findings;
        }
        if (!policy.Select(entry => entry.Key).Order(StringComparer.Ordinal).SequenceEqual(InventoryKeys, StringComparer.Ordinal)
            || Scalar(policy, "schemaVersion") != "1"
            || Scalar(policy, "repository") != "Web"
            || Scalar(policy, "spdxLicense") != "AGPL-3.0-only"
            || Scalar(policy, "licenceBoundary") != "AGPL")
            Report("inventory", InventoryPath, "The licence inventory header is not the Web AGPL boundary.");

        var actual = sources.Keys
            .Select(name => (Path: name, Kind: Kind(name)))
            .Where(row => row.Kind is not null)
            .Select(row => (row.Path, Kind: row.Kind!))
            .OrderBy(row => row.Path, StringComparer.Ordinal)
            .ToArray();
        if (actual.Length == 0)
            Report("inventory", InventoryPath, "No licence-bearing project is tracked.");
        var declared = (policy["projects"] as JsonArray ?? new JsonArray())
            .Select(node => node as JsonObject)
            .Where(row => row is not null)
            .Select(row => (Path: Scalar(row, "path"), Kind: Scalar(row, "kind")))
            .OrderBy(row => row.Path, StringComparer.Ordinal)
            .ToArray();
        if (!declared.SequenceEqual(actual))
            Report("inventory", InventoryPath, "Project licence inventory drift.");

        var npmNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var projectNames = new HashSet<string>(StringComparer.Ordinal);
        var msbuildPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, kind) in actual)
        {
            if (kind == "msbuild")
            {
                msbuildPaths.Add(path);
                projectNames.Add(PolicyText.BaseName(path)[..PolicyText.BaseName(path).LastIndexOf('.')].ToLowerInvariant());
            }
            if (kind == "npm")
            {
                var name = Scalar(PolicyText.ParseObject(sources[path]), "name");
                if (name.Length > 0 && !npmNames.TryAdd(name, path))
                    Report("inventory", path, "Duplicate npm identity: " + name);
            }
        }

        void CheckPackage(string name, string file)
        {
            var lower = name.ToLowerInvariant();
            if ((lower.StartsWith("@arcforges/", StringComparison.Ordinal) && !NpmOwners.Contains(name))
                || (lower.StartsWith("arcforges.", StringComparison.Ordinal) && !ArcForgesOwners.Contains(lower))
                || (lower.StartsWith("io.github.arcforges:", StringComparison.Ordinal) && !GithubOwners.Contains(name)))
                Report("owner", file, "Unknown first-party package owner: " + name);
        }

        foreach (var (path, kind) in actual)
        {
            var text = sources[path];
            switch (kind)
            {
                case "npm":
                    AuditManifest(path, text, npmNames, CheckPackage, Report);
                    break;
                case "msbuild":
                    var xml = PolicyText.StripXmlComments(text);
                    ExpectSingle(PolicyText.Values(xml, "PackageLicenseExpression"), "AGPL-3.0-only", () => Report("spdx", path, "Incorrect SPDX licence expression."));
                    ExpectSingle(PolicyText.Values(xml, "LicenceBoundary"), "AGPL", () => Report("boundary", path, "Incorrect licence boundary."));
                    foreach (var name in PolicyText.Values(xml, "AssemblyName").Concat(PolicyText.Values(xml, "PackageId")))
                        if (name.Length > 0)
                            projectNames.Add(name.ToLowerInvariant());
                    break;
                case "gradle":
                    foreach (var (key, value) in new[] { ("spdxLicense", "AGPL-3.0-only"), ("licenceBoundary", "AGPL") })
                        if (!Regex.Matches(text, "extra\\[\"" + key + "\"\\]\\s*=\\s*\"([^\"\\n]+)\"", RegexOptions.CultureInvariant)
                                .Select(match => match.Groups[1].Value)
                                .SequenceEqual(new[] { value }, StringComparer.Ordinal))
                            Report("gradle", path, "Incorrect Gradle declaration: " + key);
                    break;
                default:
                    Report("build-system", path, "A new build system needs a reviewed licence verifier.");
                    break;
            }
        }

        foreach (var file in sources.Keys.Order(StringComparer.Ordinal))
        {
            var text = sources[file];
            var name = PolicyText.BaseName(file);
            if (MsBuildExtensions.Any(extension => name.EndsWith(extension, StringComparison.Ordinal))
                || ImportedExtensions.Any(extension => name.EndsWith(extension, StringComparison.Ordinal)))
            {
                var xml = PolicyText.StripXmlComments(text);
                if (!PolicyText.Values(xml, "PackageLicenseExpression").All(value => value == "AGPL-3.0-only")
                    || !PolicyText.Values(xml, "LicenceBoundary").All(value => value == "AGPL"))
                    Report("override", file, "Imported licence override.");
                foreach (Match reference in ProjectInclude.Matches(xml))
                {
                    var include = reference.Groups[1].Value;
                    if (NonLiteral.IsMatch(include))
                    {
                        Report("reference", file, "Nonliteral reference: " + include);
                        continue;
                    }
                    var target = PolicyText.Join(PolicyText.DirectoryOf(file), include.Replace('\\', '/'));
                    if (!msbuildPaths.Contains(target))
                        Report("reference", file, "Escaped/unregistered reference: " + include);
                }
                foreach (Match package in PackageIdentity.Matches(xml))
                    CheckPackage(package.Groups[1].Value, file);
            }
            else if (name == "package-lock.json")
            {
                foreach (var (key, entry) in Entries(PolicyText.ParseObject(text)?["packages"]))
                {
                    var identity = Scalar(entry as JsonObject, "name");
                    if (identity.Length == 0)
                        identity = key.Split("node_modules/", StringSplitOptions.None).Last();
                    if (!npmNames.ContainsKey(identity))
                        CheckPackage(identity, file);
                }
            }
            else if (name == "packages.lock.json")
            {
                foreach (var (_, framework) in Entries(PolicyText.ParseObject(text)?["dependencies"]))
                    foreach (var (dependency, entry) in Entries(framework))
                        if (string.Equals(Scalar(entry as JsonObject, "type"), "project", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!projectNames.Contains(dependency.ToLowerInvariant()))
                                Report("owner", file, "Unknown locked project: " + dependency);
                        }
                        else
                        {
                            CheckPackage(dependency, file);
                        }
            }
            else if (file.EndsWith(".lockfile", StringComparison.Ordinal))
            {
                foreach (var line in text.Split('\n').Where(line => line.Length > 0 && !line.StartsWith('#')))
                    CheckPackage(string.Join(':', line.Split(':').Take(2)), file);
            }
            else if (GradleFile.IsMatch(name))
            {
                if (GradleSource.IsMatch(text))
                    Report("gradle", file, "Unpublished Gradle source.");
                if (GradleProject.IsMatch(text))
                    Report("gradle", file, "New Gradle project graph requires licence review.");
                foreach (Match owner in GradleOwner.Matches(text))
                    CheckPackage(owner.Value, file);
            }
        }
        return findings;
    }

    private static void AuditManifest(
        string path,
        string text,
        IReadOnlyDictionary<string, string> npmNames,
        Action<string, string> checkPackage,
        Action<string, string, string> report)
    {
        var manifest = PolicyText.ParseObject(text);
        if (manifest is null)
        {
            report("inventory", path, "The npm manifest is malformed.");
            return;
        }
        if (Scalar(manifest, "license") != "AGPL-3.0-only")
            report("spdx", path, "Incorrect SPDX licence expression.");
        if (manifest["arcforges"] is not JsonObject boundary || boundary.Count != 1 || Scalar(boundary, "licenceBoundary") != "AGPL")
            report("boundary", path, "Incorrect licence boundary.");
        foreach (var section in SectionNames)
            foreach (var (name, node) in Entries(manifest[section]))
            {
                var version = node?.ToJsonString().Trim('"') ?? string.Empty;
                if (UnpublishedSource.IsMatch(version))
                    report("source-reference", path, "Unpublished source reference: " + name);
                if (!npmNames.ContainsKey(name))
                    checkPackage(name, path);
                if (version.StartsWith("npm:", StringComparison.Ordinal))
                {
                    var alias = version[4..];
                    var end = alias.IndexOf('@', 1);
                    checkPackage(end < 0 ? alias : alias[..end], path);
                }
            }
    }

    private static void ExpectSingle(IEnumerable<string> values, string expected, Action report)
    {
        var list = values.ToArray();
        if (list.Length != 1 || list[0] != expected)
            report();
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Entries(JsonNode? node) =>
        node is JsonObject entries ? entries : Array.Empty<KeyValuePair<string, JsonNode?>>();

    private static string Scalar(JsonNode? node, string name)
    {
        if (node?[name] is not JsonValue value)
            return string.Empty;
        return value.TryGetValue(out string? text) ? text ?? string.Empty : value.ToJsonString();
    }

    private static string? Kind(string file)
    {
        var name = PolicyText.BaseName(file);
        if (name == "package.json")
            return "npm";
        if (name is "build.gradle.kts" or "build.gradle")
            return "gradle";
        if (name == "CMakeLists.txt")
            return "cmake";
        return MsBuildExtensions.Any(extension => name.EndsWith(extension, StringComparison.Ordinal)) ? "msbuild" : null;
    }
}
