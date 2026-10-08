// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace ArcForges.Web.Policy.Tests;

/// <summary>
/// The forbidden-term scanner of WP-05.02 in the C# policy suite. The scanner and its product-name policy are the
/// published Contracts naming authority in node_modules; this suite verifies the pinned identity and runs the scanner
/// under python -I on an isolated local Git fixture, once for the current terms and once for every forbidden term.
/// </summary>
public sealed class NamingScannerTests
{
    private const string Clean = "ArcForges ArcScope";

    [Fact]
    public void PublishedNamingAuthorityMatchesTheAdmittedCandidate()
    {
        var root = RepositoryRoot.Find();
        var candidate = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eng", "policy", "naming-candidate.json")))!.AsObject();
        var package = NamingPackage(root, candidate["package"]!.GetValue<string>());

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "package.json")))!.AsObject();
        Assert.Equal(candidate["version"]!.GetValue<string>(), manifest["version"]!.GetValue<string>());
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "source.json")))!.AsObject();
        Assert.Equal(candidate["sourceCommit"]!.GetValue<string>(), source["commit"]!.GetValue<string>());
        foreach (var (asset, expected) in candidate["assets"]!.AsObject())
            Assert.Equal(expected!.GetValue<string>(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(package, asset)))).ToLowerInvariant());
    }

    [Fact]
    public void PublishedScannerAcceptsCurrentTermsAndDetectsEveryForbiddenTerm()
    {
        var root = RepositoryRoot.Find();
        var package = NamingPackage(root, "@arcforges/proto");
        var script = Path.Combine(package, "tools", "naming", "eng", "check_naming.py");
        var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "tools", "naming", "eng", "policy", "product-names.json")))!.AsObject();

        var repository = Path.Combine(Path.GetTempPath(), "web-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            Git(repository, "init", "-q");
            Git(repository, "remote", "add", "origin", "https://github.com/ArcForges/Web.git");
            Git(repository, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-qm", "fixture");
            var probe = Path.Combine(repository, "probe.txt");

            File.WriteAllText(probe, Clean, Encoding.UTF8);
            var accepted = Scan(script, repository);
            Assert.Equal(0, accepted.ExitCode);

            foreach (var term in policy["forbiddenNames"]!.AsArray())
            {
                var name = term!["name"]!.GetValue<string>();
                File.WriteAllText(probe, name, Encoding.UTF8);
                var refused = Scan(script, repository);
                Assert.True(refused.ExitCode == 1, "Forbidden term was not refused: " + name);
                Assert.Contains("\"kind\": \"forbidden content\"", refused.Output, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeleteFixtureRepository(repository);
        }
    }

    /// <summary>Deletes the fixture repository. Git writes its object files read-only on Windows, so they are made writable first.</summary>
    private static void DeleteFixtureRepository(string repository)
    {
        foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(repository, recursive: true);
    }

    private static string NamingPackage(string root, string package)
    {
        var parts = package.Split('/');
        var directory = Path.Combine([root, "node_modules", .. parts]);
        Assert.True(Directory.Exists(directory), "The published naming package is not installed: " + package);
        return directory;
    }

    /// <summary>
    /// The interpreter name for the scanner. Windows images name it python; the GitHub Ubuntu image provides python3
    /// and does not guarantee a python alias.
    /// </summary>
    private static string PythonInterpreter() => OperatingSystem.IsWindows() ? "python" : "python3";

    private static (int ExitCode, string Output) Scan(string script, string repository)
    {
        var start = new ProcessStartInfo(PythonInterpreter())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--repository");
        start.ArgumentList.Add("Web=" + repository);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("python could not be started.");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        errorTask.GetAwaiter().GetResult();
        return (process.ExitCode, output);
    }

    private static void Git(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        var errorTask = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        errorTask.GetAwaiter().GetResult();
        Assert.Equal(0, process.ExitCode);
    }
}
