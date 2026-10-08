// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using ArcForges.Web.Site;

namespace ArcForges.Web.Tooling.Candidate;

/// <summary>The inputs of one candidate build. Every value is explicit; nothing is read from the environment or the clock.</summary>
public sealed record CandidateOptions
{
    /// <summary>The repository root that holds the reviewed inputs (LICENSE, NOTICE, lock files, the policy, wrangler.json).</summary>
    public required string Repository { get; init; }

    /// <summary>The 40-hex lower-case source commit of the build.</summary>
    public required string SourceRef { get; init; }

    /// <summary>The release version: 0.1.0-local, or 0.1.0-ci.RUN.ATTEMPT on CI.</summary>
    public required string Version { get; init; }

    /// <summary>True when the source tree had uncommitted changes. A dirty candidate is never deployed.</summary>
    public bool Dirty { get; init; }

    /// <summary>The build identity document (<c>assets/__build-info.json</c>), produced by the Node identity emitter.</summary>
    public required byte[] Identity { get; init; }

    /// <summary>The emitted Worker script (<c>worker/index.js</c>), produced by the Node emitter from worker/index.ts.</summary>
    public required byte[] Worker { get; init; }
}

/// <summary>Builds the sealed public candidate in memory, then writes it. Two builds of the same inputs are the same bytes.</summary>
public static partial class CandidateBuilder
{
    /// <summary>Builds every member of the candidate, including the sealed manifest, keyed by candidate path.</summary>
    public static async Task<SortedDictionary<string, byte[]>> BuildAsync(CandidateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!VersionPattern().IsMatch(options.Version))
            throw new ArgumentException("Invalid candidate version.", nameof(options));
        if (!SourcePattern().IsMatch(options.SourceRef))
            throw new ArgumentException("The source ref must be a 40-character lower-case commit id.", nameof(options));
        CandidateCore.RequireWorker(options.Worker);
        RequireIdentity(options.Identity, options.SourceRef, options.Version, options.Dirty);

        var site = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = options.SourceRef }, cancellationToken)
            .ConfigureAwait(false);
        var repository = options.Repository;
        var members = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        void Add(string path, byte[] bytes)
        {
            if (members.ContainsKey(path))
                throw new InvalidOperationException("Duplicate candidate member: " + path);
            members[path] = bytes;
        }

        foreach (var file in site.Files)
            Add(CandidateCore.AssetsPrefix + file.Path, file.Content);
        Add(CandidateCore.AssetsPrefix + "license.txt", CandidateCore.Licence(repository));
        Add(CandidateCore.AssetsPrefix + "third-party-notices.txt", CandidateCore.Notices(repository));
        Add(CandidateCore.AssetsPrefix + "source-provenance.txt", CandidateCore.SourceNotice(repository));
        Add(CandidateCore.AssetsPrefix + "__build.json", CandidateCore.BuildJson(options.Version, options.SourceRef));
        Add(CandidateCore.AssetsPrefix + "__build-info.json", options.Identity);
        Add(CandidateCore.WranglerPath, CandidateCore.Json(CandidateCore.CandidateWrangler(repository)));
        Add(CandidateCore.WorkerPath, options.Worker);
        Add(CandidateCore.SbomPath, CandidateCore.BuildSbom(repository));
        Add(CandidateCore.RuntimeSbomPath, CandidateCore.RuntimeSbom());
        Add(
            CandidateCore.ReceiptPath,
            CandidateCore.Receipt(options.SourceRef, options.Version, CandidateCore.PolicySha256(repository), members));

        var listed = new JsonObject();
        foreach (var (path, bytes) in members.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            listed[path] = CandidateCore.Sha256(bytes);
        var manifest = new JsonObject
        {
            ["schema"] = CandidateCore.ManifestSchema,
            ["version"] = options.Version,
            ["source"] = options.SourceRef,
            ["dirty"] = options.Dirty,
            ["files"] = listed,
        };
        Add(CandidateCore.ManifestPath, CandidateCore.Json(manifest));
        return members;
    }

    /// <summary>Writes the built members to a directory that must not exist yet (fail closed on a stale candidate).</summary>
    public static void Write(string directory, IReadOnlyDictionary<string, byte[]> members)
    {
        var root = Path.GetFullPath(directory);
        if (Directory.Exists(root) || File.Exists(root))
            throw new InvalidOperationException("Refusing to write into an existing path: " + root);
        foreach (var (path, bytes) in members)
        {
            var target = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Candidate path escapes the output directory: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    /// <summary>The identity must name this source, this version and this dirty state; a mismatch is refused before sealing.</summary>
    internal static void RequireIdentity(byte[] identity, string sourceRef, string version, bool dirty)
    {
        var document = JsonNode.Parse(identity)?.AsObject()
            ?? throw new InvalidOperationException("The build identity must be a JSON object.");
        var build = document["build"]?.AsObject()
            ?? throw new InvalidOperationException("The build identity has no build section.");
        if (build["sourceCommit"]?.GetValue<string>() != sourceRef)
            throw new InvalidOperationException("The build identity names another source commit.");
        if (build["dirty"]?.GetValue<bool>() != dirty)
            throw new InvalidOperationException("The build identity names another dirty state.");
        if (document["artifact"]?["version"]?.GetValue<string>() != version)
            throw new InvalidOperationException("The build identity names another version.");
    }

    [GeneratedRegex(@"^0\.1\.0-(?:local|ci\.[1-9]\d*\.[1-9]\d*)$", RegexOptions.CultureInvariant)]
    internal static partial Regex VersionPattern();

    [GeneratedRegex(@"^[a-f0-9]{40}$", RegexOptions.CultureInvariant)]
    internal static partial Regex SourcePattern();
}
