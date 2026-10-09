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

    [Fact]
    public void TestOnlyCopyleftIsAdmittedOnlyForATestOnlyRow()
    {
        var restored = new Dictionary<string, string> { ["axe.test/1.0.0"] = "h" };
        var testOnly = new JsonObject
        {
            ["axe.test/1.0.0"] = new JsonObject { ["contentHash"] = "h", ["licence"] = "MPL-2.0", ["testOnly"] = true },
        };
        Assert.Empty(NuGetAdmission.Violations(testOnly, restored));

        var productionCopyleft = new JsonObject
        {
            ["axe.test/1.0.0"] = new JsonObject { ["contentHash"] = "h", ["licence"] = "MPL-2.0" },
        };
        Assert.Contains(NuGetAdmission.Violations(productionCopyleft, restored), message => message.StartsWith("Forbidden or unreviewed licence", StringComparison.Ordinal));

        var testOnlyGpl = new JsonObject
        {
            ["axe.test/1.0.0"] = new JsonObject { ["contentHash"] = "h", ["licence"] = "GPL-3.0-only", ["testOnly"] = true },
        };
        Assert.Contains(NuGetAdmission.Violations(testOnlyGpl, restored), message => message.StartsWith("Forbidden or unreviewed licence", StringComparison.Ordinal));
    }

    [Fact]
    public void TestOnlyPackagesAreRestoredByNoProductProject()
    {
        var root = RepositoryRoot.Find();
        var testOnly = AdmittedClosure(root)
            .Where(entry => entry.Value!.AsObject()["testOnly"]?.GetValue<bool>() == true)
            .Select(entry => entry.Key.Split('/')[0])
            .ToList();
        Assert.NotEmpty(testOnly);

        var productLocks = Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !Path.GetRelativePath(root, path).StartsWith($"tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        foreach (var lockPath in productLocks)
        {
            var text = File.ReadAllText(lockPath);
            foreach (var id in testOnly)
                Assert.DoesNotContain($"\"{id}\": {{", text, StringComparison.OrdinalIgnoreCase);
        }
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
