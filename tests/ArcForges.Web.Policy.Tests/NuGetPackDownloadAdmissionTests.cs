// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace ArcForges.Web.Policy.Tests;

/// <summary>
/// Checks the browser-wasm pack downloads (WEB.40 section 10). NuGet records PackageDownload items outside
/// packages.lock.json, so each declared item must be admitted at the exact version, and every admitted download must
/// be either declared in a project or added implicitly by the SDK.
/// </summary>
public sealed class NuGetPackDownloadAdmissionTests
{
    private static readonly Regex DeclaredDownload = new(
        "<PackageDownload\\s+Include=\"(?<id>[^\"]+)\"\\s+Version=\"\\[(?<version>[^\\]]+)\\]\"",
        RegexOptions.CultureInvariant);

    private static readonly string[] PermissiveLicences = ["MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC"];

    [Fact]
    public void EveryDeclaredPackDownloadIsAdmittedAtTheExactVersion()
    {
        var root = RepositoryRoot.Find();
        var admitted = AdmittedDownloads(root);
        var declared = DeclaredDownloads(root);
        Assert.NotEmpty(declared);
        foreach (var entry in declared)
            Assert.True(admitted.ContainsKey(entry.Key), $"Unadmitted PackageDownload {entry.Key} in {entry.Value}.");
    }

    [Fact]
    public void EveryAdmittedPackDownloadIsDeclaredOrAddedImplicitlyBySdk()
    {
        var root = RepositoryRoot.Find();
        var admitted = AdmittedDownloads(root);
        var declared = DeclaredDownloads(root).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in admitted.Select(entry => entry.Key))
        {
            if (declared.Contains(key))
                continue;
            var declaredBy = admitted[key]?["declaredBy"]?.GetValue<string>() ?? string.Empty;
            Assert.StartsWith("implicit:", declaredBy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryAdmittedPackDownloadHasAContentHashSourceAndPermissiveLicence()
    {
        var admitted = AdmittedDownloads(RepositoryRoot.Find());
        Assert.NotEmpty(admitted);
        foreach (var entry in admitted)
        {
            var row = entry.Value?.AsObject() ?? throw new InvalidDataException("Empty admission row: " + entry.Key);
            Assert.Matches("^[A-Za-z0-9+/]{86}==$", row["contentHash"]?.GetValue<string>() ?? string.Empty);
            Assert.Matches("^[0-9a-f]{64}$", row["nuspecSha256"]?.GetValue<string>() ?? string.Empty);
            Assert.StartsWith("https://api.nuget.org/v3-flatcontainer/", row["source"]?.GetValue<string>() ?? string.Empty, StringComparison.Ordinal);
            var licence = row["licence"]?.GetValue<string>() ?? string.Empty;
            Assert.Contains(licence, PermissiveLicences);
            Assert.EndsWith("/10.0.12", entry.Key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AspNetCoreBrowserWasmPackIsNotAdmitted()
    {
        Assert.False(AdmittedDownloads(RepositoryRoot.Find()).ContainsKey("microsoft.aspnetcore.app.runtime.browser-wasm/10.0.12"));
    }

    private static JsonObject AdmittedDownloads(string root)
    {
        var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eng", "policy", "dependency-policy.json")))!.AsObject();
        return policy["nugetPackDownloads"]!.AsObject();
    }

    private static List<KeyValuePair<string, string>> DeclaredDownloads(string root)
    {
        var declared = new List<KeyValuePair<string, string>>();
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match match in DeclaredDownload.Matches(File.ReadAllText(project)))
                declared.Add(new KeyValuePair<string, string>(
                    match.Groups["id"].Value.ToLowerInvariant() + "/" + match.Groups["version"].Value,
                    Path.GetRelativePath(root, project)));
        }
        return declared;
    }
}
