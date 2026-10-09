// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
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
    public void EveryAdmittedNuspecDigestIsTheRestoredNuspecDigest()
    {
        // Convention: the SHA-256 of the .nuspec entry exactly as the package stores it, UTF-8 BOM included. The CI source
        // job restores every project except the local browser project from nuget.org, so each package it restores must be
        // present here and must match; browser-only test packages are verified wherever the local folder holds them.
        var root = RepositoryRoot.Find();
        var policy = PolicyDocument(root);
        var packages = NuGetPackagesFolder();
        var restoredByCi = CiRestoredKeys(root);
        var packsRoot = DotNetPacksFolder();
        var verified = 0;
        var satisfiedByPacks = new List<string>();
        foreach (var section in new[] { "nugetClosure", "nugetPackDownloads" })
        {
            foreach (var entry in policy[section]!.AsObject())
            {
                var expected = entry.Value!.AsObject()["nuspecSha256"]!.GetValue<string>();
                var mustBePresent = section == "nugetPackDownloads" || restoredByCi.Contains(entry.Key);
                if (TryRestoredNuspecDigest(packages, entry.Key, out var actual))
                {
                    Assert.Equal(expected, actual);
                    verified++;
                }
                else if (SatisfiedByPacksFolder(packsRoot, entry.Key, out _))
                {
                    // The SDK resolved this implicit pack from its own packs folder on this runner (see PacksFolderRows).
                    satisfiedByPacks.Add(entry.Key);
                }
                else
                {
                    Assert.False(mustBePresent, $"Admitted package {entry.Key} is not in the NuGet packages folder; run the locked restore first.");
                }
            }
        }
        Assert.True(verified > 0, "No admitted nuspec was verified against the restored packages.");
        // Every row skipped here is an explicit PacksFolderRows entry for this platform whose packs copy exists, and every
        // other admitted row was verified against its NuGet copy above.
        Assert.All(satisfiedByPacks, key => Assert.Contains(PacksFolderRows, row => row.Key == key && row.Platform == CurrentPlatform()));
    }

    [Fact]
    public void PacksFolderRowsAreAdmittedPackDownloads()
    {
        var policy = PolicyDocument(RepositoryRoot.Find());
        var downloads = policy["nugetPackDownloads"]!.AsObject();
        foreach (var row in PacksFolderRows)
        {
            Assert.True(downloads.ContainsKey(row.Key), $"{row.Key} is not an admitted pack-download row.");
            Assert.False(string.IsNullOrWhiteSpace(row.Reason), $"{row.Key} has no recorded reason.");
        }
    }

    [Fact]
    public void ContractsIdentityNuspecDigestsAreTheRestoredNuspecDigests()
    {
        var root = RepositoryRoot.Find();
        var receipt = ActiveReceipt(root);
        var identity = receipt["con07Identity"]!.AsObject();
        var version = identity["version"]!.GetValue<string>().ToLowerInvariant();
        var packages = NuGetPackagesFolder();
        foreach (var package in identity["packages"]!.AsArray())
        {
            var row = package!.AsObject();
            var key = row["id"]!.GetValue<string>().ToLowerInvariant() + "/" + version;
            if (TryRestoredNuspecDigest(packages, key, out var actual))
                Assert.Equal(row["nuspecSha256"]!.GetValue<string>(), actual);
        }
    }

    [Fact]
    public void AdmittedNuspecRowsEqualTheActiveReceipt()
    {
        var root = RepositoryRoot.Find();
        var policy = PolicyDocument(root);
        var receipt = ActiveReceipt(root);
        Assert.True(JsonNode.DeepEquals(policy["nugetClosure"], receipt["nugetClosure"]), "Policy and receipt nugetClosure rows differ.");
        Assert.True(JsonNode.DeepEquals(policy["nugetPackDownloads"], receipt["nugetPackDownloads"]), "Policy and receipt nugetPackDownloads rows differ.");
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

    /// <summary>
    /// The admitted pack-download rows that the SDK resolves from its own packs folder on one platform, so the locked restore
    /// does not write their NuGet copy there. The SDK adds the implicit Mono browser-wasm runtime pack as a NuGet download only
    /// when the packs folder lacks the bundled runtime version. The explicit PackageDownload rows are always restored to NuGet.
    /// Every other row is required in the NuGet packages folder on every platform. A row is skipped only when its packs copy
    /// exists at the admitted version, so a missing pack still fails.
    /// </summary>
    private static readonly PacksFolderRow[] PacksFolderRows =
    [
        new(
            Key: "microsoft.netcore.app.runtime.mono.browser-wasm/10.0.12",
            Platform: "Windows",
            PacksFolder: "Microsoft.NETCore.App.Runtime.Mono.browser-wasm",
            Version: "10.0.12",
            Reason: "The hosted windows-2025-vs2026 image (runner image 20260925.250.1, CI job 113727560820) preinstalls the wasm.tools workload into C:\\Program Files\\dotnet, which holds this pack at the bundled version 10.0.12 (the same run's build lists the 10.0.12 Emscripten workload packs in that folder), so the SDK adds no NuGet download. Ubuntu, and a local machine whose packs folder lacks 10.0.12, restore the NuGet copy and it is verified by digest there."),
    ];

    private sealed record PacksFolderRow(string Key, string Platform, string PacksFolder, string Version, string Reason);

    private static bool SatisfiedByPacksFolder(string packsRoot, string key, out string reason)
    {
        reason = string.Empty;
        foreach (var row in PacksFolderRows)
        {
            if (row.Key != key || row.Platform != CurrentPlatform())
                continue;
            if (!Directory.Exists(Path.Combine(packsRoot, row.PacksFolder, row.Version)))
                return false;
            reason = row.Reason;
            return true;
        }
        return false;
    }

    private static string CurrentPlatform() => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other";

    private static string DotNetPacksFolder()
    {
        // The SDK reads NetCoreTargetingPackRoot when it is set, the same environment property that overrides its packs
        // folder. Otherwise the packs folder is <dotnet root>/packs, where the dotnet root holds the running shared runtime
        // (<dotnet root>/shared/Microsoft.NETCore.App/<version>/). The restoring SDK and the test host share that root.
        var configured = Environment.GetEnvironmentVariable("NetCoreTargetingPackRoot");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        var runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        return Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "packs"));
    }

    private static JsonObject AdmittedClosure(string root)
    {
        return PolicyDocument(root)["nugetClosure"]!.AsObject();
    }

    private static JsonObject PolicyDocument(string root)
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eng", "policy", "dependency-policy.json")))!.AsObject();
    }

    private static JsonObject ActiveReceipt(string root)
    {
        var reviewRecord = PolicyDocument(root)["reviewRecord"]!.GetValue<string>();
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root, reviewRecord)))!.AsObject();
    }

    private static string NuGetPackagesFolder()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
    }

    private static bool TryRestoredNuspecDigest(string packages, string key, out string digest)
    {
        var parts = key.Split('/');
        var nuspec = Path.Combine(packages, parts[0], parts[1], parts[0] + ".nuspec");
        digest = string.Empty;
        if (!File.Exists(nuspec))
            return false;
        digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nuspec))).ToLowerInvariant();
        return true;
    }

    private static HashSet<string> CiRestoredKeys(string root)
    {
        // The source job restores every project except the local opt-in browser project under tests/browser.
        var browser = "tests" + Path.DirectorySeparatorChar + "browser" + Path.DirectorySeparatorChar;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lockPath in Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !Path.GetRelativePath(root, path).StartsWith(browser, StringComparison.Ordinal)))
        {
            var document = JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject();
            foreach (var framework in document["dependencies"]!.AsObject())
                foreach (var package in framework.Value!.AsObject())
                {
                    var entry = package.Value!.AsObject();
                    if (entry["type"]!.GetValue<string>() == "Project")
                        continue;
                    keys.Add(package.Key.ToLowerInvariant() + "/" + entry["resolved"]!.GetValue<string>());
                }
        }
        Assert.NotEmpty(keys);
        return keys;
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
