// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using Xunit;

namespace ArcForges.Web.Policy.Tests;

public sealed class NuGetClosureAdmissionTests
{
    [Fact]
    public void RestoredClosureMatchesTheAdmittedRecords()
    {
        var root = RepositoryRoot.Find();
        var admitted = AdmittedClosure(root);
        var restored = RestoredClosure(root);
        Assert.Empty(NuGetAdmission.Violations(admitted, restored));
    }

    [Fact]
    public void EveryAdmittedRowHasSourceAndDigestEvidence()
    {
        var admitted = AdmittedClosure(RepositoryRoot.Find());
        Assert.NotEmpty(admitted);
        foreach (var entry in admitted)
        {
            var row = entry.Value!.AsObject();
            var digest = row["nuspecSha256"]?.GetValue<string>() ?? string.Empty;
            Assert.Matches("^[0-9a-f]{64}$", digest);
            var source = row["source"]?.GetValue<string>() ?? string.Empty;
            Assert.StartsWith("https://api.nuget.org/v3-flatcontainer/", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChangedContentHashIsRefused()
    {
        var admitted = AdmittedClosure(RepositoryRoot.Find());
        var restored = admitted.ToDictionary(entry => entry.Key, entry => entry.Value!["contentHash"]!.GetValue<string>());
        var first = restored.Keys.First();
        restored[first] = "changed-hash";
        Assert.Contains(NuGetAdmission.Violations(admitted, restored), message => message.StartsWith("Content hash differs", StringComparison.Ordinal));
    }

    [Fact]
    public void UnadmittedPackageIsRefused()
    {
        var admitted = AdmittedClosure(RepositoryRoot.Find());
        var restored = admitted.ToDictionary(entry => entry.Key, entry => entry.Value!["contentHash"]!.GetValue<string>());
        restored["unadmitted.example/1.0.0"] = "hash";
        Assert.Contains(NuGetAdmission.Violations(admitted, restored), message => message.StartsWith("Unadmitted NuGet package", StringComparison.Ordinal));
    }

    [Fact]
    public void CopyleftThirdPartyLicenceIsRefused()
    {
        var admitted = new JsonObject
        {
            ["third.party/1.0.0"] = new JsonObject { ["contentHash"] = "h", ["licence"] = "GPL-3.0-only" },
        };
        var restored = new Dictionary<string, string> { ["third.party/1.0.0"] = "h" };
        Assert.Contains(NuGetAdmission.Violations(admitted, restored), message => message.StartsWith("Forbidden or unreviewed licence", StringComparison.Ordinal));
    }

    private static JsonObject AdmittedClosure(string root)
    {
        var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eng", "policy", "dependency-policy.json")))!.AsObject();
        return policy["nugetClosure"]!.AsObject();
    }

    private static IReadOnlyDictionary<string, string> RestoredClosure(string root)
    {
        var closure = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var lockPath in Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var document = JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject();
            foreach (var framework in document["dependencies"]!.AsObject())
            foreach (var package in framework.Value!.AsObject())
            {
                var entry = package.Value!.AsObject();
                if (entry["type"]!.GetValue<string>() == "Project")
                    continue;
                var key = package.Key.ToLowerInvariant() + "/" + entry["resolved"]!.GetValue<string>();
                var hash = entry["contentHash"]!.GetValue<string>();
                if (closure.TryGetValue(key, out var seen))
                    Assert.Equal(seen, hash);
                closure[key] = hash;
            }
        }
        return closure;
    }
}
