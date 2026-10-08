// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.Site;
using ArcForges.Web.Tooling.Candidate;
using ArcForges.Web.Tooling.Profiles;

namespace ArcForges.Web.Tooling;

/// <summary>
/// Build tooling entry point of the C# Web. The TypeScript tooling ports here in steps; every command not listed below is
/// refused with exit code 2. Exit code 1 is a failed verification, 2 a usage error.
/// </summary>
public static class Program
{
    private const string SiteBuildUsage = "usage: site build --out <new-directory> [--source-ref <40-hex lower-case commit>]";
    private const string CandidateBuildUsage =
        "usage: candidate build --out <new-directory> --repo <root> --source-ref <40-hex> --version <version> --identity <file> --worker <file> [--dirty true|false]";
    private const string CandidateVerifyUsage = "usage: candidate verify --dir <candidate> [--repo <root>] [--expected-source <40-hex>]";
    private const string ProfilesBundleUsage = "usage: profiles bundle --publish <publish-directory> --out <new-or-existing-directory>";
    private const string ProfilesVerifyUsage = "usage: profiles verify --bundle <file> [--expected-digest <sha256>]";

    /// <summary>Runs a ported command: <c>site build</c>, <c>candidate build|verify</c> and <c>profiles bundle|verify</c>.</summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["site", "build", .. var site])
                return await SiteBuildAsync(site).ConfigureAwait(false);
            if (args is ["candidate", "build", .. var build])
                return await CandidateBuildAsync(build).ConfigureAwait(false);
            if (args is ["candidate", "verify", .. var verify])
                return await CandidateVerifyAsync(verify).ConfigureAwait(false);
            if (args is ["profiles", "bundle", .. var bundle])
                return ProfilesBundle(bundle);
            if (args is ["profiles", "verify", .. var check])
                return ProfilesVerify(check);
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine("Verification failed: " + error.Message);
            return 1;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }

        Console.Error.WriteLine(
            "ArcForges.Web.Tooling: unknown or unported command. Ported commands: site build, candidate build, candidate verify, profiles bundle, profiles verify.");
        return 2;
    }

    /// <summary>Builds the static Site and writes every file to a directory that must not exist yet.</summary>
    private static async Task<int> SiteBuildAsync(string[] options)
    {
        if (!TryParse(options, ["--out", "--source-ref"], out var values))
        {
            Console.Error.WriteLine(SiteBuildUsage);
            return 2;
        }
        if (!values.TryGetValue("--out", out var outDirectory) || string.IsNullOrWhiteSpace(outDirectory))
        {
            Console.Error.WriteLine(SiteBuildUsage);
            return 2;
        }
        values.TryGetValue("--source-ref", out var sourceRef);

        // Fail closed: a stale file from an earlier build must never be published by a later one.
        var root = Path.GetFullPath(outDirectory);
        if (Directory.Exists(root) || File.Exists(root))
        {
            Console.Error.WriteLine("Refusing to write into an existing path: " + root);
            return 2;
        }

        var output = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = sourceRef }).ConfigureAwait(false);
        Directory.CreateDirectory(root);
        foreach (var file in output.Files)
        {
            var target = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Site path escapes the output directory: " + file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, file.Content).ConfigureAwait(false);
        }

        Console.WriteLine($"Wrote {output.Files.Count} Site files to {root}");
        return 0;
    }

    /// <summary>Builds the sealed public candidate (the C# Site, its identity, legal files, Worker, SBOMs and receipt).</summary>
    private static async Task<int> CandidateBuildAsync(string[] options)
    {
        if (!TryParse(options, ["--out", "--repo", "--source-ref", "--version", "--identity", "--worker", "--dirty"], out var values)
            || !Has(values, "--out", "--repo", "--source-ref", "--version", "--identity", "--worker"))
        {
            Console.Error.WriteLine(CandidateBuildUsage);
            return 2;
        }
        var dirty = false;
        if (values.TryGetValue("--dirty", out var dirtyText))
        {
            if (dirtyText is not ("true" or "false"))
            {
                Console.Error.WriteLine(CandidateBuildUsage);
                return 2;
            }
            dirty = dirtyText == "true";
        }
        var root = Path.GetFullPath(values["--out"]);
        if (Directory.Exists(root) || File.Exists(root))
            throw new ArgumentException("Refusing to write into an existing path: " + root);

        var members = await CandidateBuilder.BuildAsync(new CandidateOptions
        {
            Repository = Path.GetFullPath(values["--repo"]),
            SourceRef = values["--source-ref"],
            Version = values["--version"],
            Dirty = dirty,
            Identity = await File.ReadAllBytesAsync(values["--identity"]).ConfigureAwait(false),
            Worker = await File.ReadAllBytesAsync(values["--worker"]).ConfigureAwait(false),
        }).ConfigureAwait(false);
        CandidateBuilder.Write(root, members);
        Console.WriteLine($"Wrote candidate {values["--version"]} {values["--source-ref"]} ({members.Count} members) to {root}");
        return 0;
    }

    /// <summary>Verifies a sealed candidate. The repository, when given, binds the licence, notices, SBOM and policy receipt.</summary>
    private static async Task<int> CandidateVerifyAsync(string[] options)
    {
        if (!TryParse(options, ["--dir", "--repo", "--expected-source"], out var values) || !Has(values, "--dir"))
        {
            Console.Error.WriteLine(CandidateVerifyUsage);
            return 2;
        }
        values.TryGetValue("--repo", out var repository);
        values.TryGetValue("--expected-source", out var expectedSource);
        if (expectedSource is not null && !System.Text.RegularExpressions.Regex.IsMatch(expectedSource, "^[a-f0-9]{40}$"))
            throw new ArgumentException("The expected source must be a 40-character lower-case commit id.");
        var summary = await CandidateVerifier.VerifyAsync(
            Path.GetFullPath(values["--dir"]),
            repository is null ? null : Path.GetFullPath(repository),
            expectedSource).ConfigureAwait(false);
        Console.WriteLine($"Candidate verified: {summary.Version} {summary.Source} ({summary.Members} members{(summary.Dirty ? ", local changes; not deployable" : "")})");
        return 0;
    }

    /// <summary>Builds the Web profile bundle from a publish of ArcForges.Web.App and writes it to the output directory.</summary>
    private static int ProfilesBundle(string[] options)
    {
        if (!TryParse(options, ["--publish", "--out"], out var values) || !Has(values, "--publish", "--out"))
        {
            Console.Error.WriteLine(ProfilesBundleUsage);
            return 2;
        }
        var bundle = ProfileBundle.Build(values["--publish"]);
        ProfileBundle.Write(values["--out"], bundle);
        Console.WriteLine($"Profile bundle {bundle.Name}: {bundle.Members} members, {bundle.Archive.Length} bytes");
        return 0;
    }

    /// <summary>Verifies a profile bundle archive against its own name, layout, manifest and reviewed rules.</summary>
    private static int ProfilesVerify(string[] options)
    {
        if (!TryParse(options, ["--bundle", "--expected-digest"], out var values) || !Has(values, "--bundle"))
        {
            Console.Error.WriteLine(ProfilesVerifyUsage);
            return 2;
        }
        values.TryGetValue("--expected-digest", out var expectedDigest);
        var summary = ProfileBundle.Verify(File.ReadAllBytes(values["--bundle"]), expectedDigest);
        Console.WriteLine($"Profile bundle verified: {summary.Name} ({summary.Members} members)");
        return 0;
    }

    /// <summary>Parses <c>--name value</c> pairs. Each name must be allowed once; a bare value or a repeated name fails.</summary>
    private static bool TryParse(string[] args, string[] allowed, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !allowed.Contains(args[index], StringComparer.Ordinal) || values.ContainsKey(args[index]))
                return false;
            values[args[index]] = args[index + 1];
        }
        return true;
    }

    private static bool Has(Dictionary<string, string> values, params string[] names) => names.All(values.ContainsKey);
}
