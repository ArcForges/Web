// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using ArcForges.Web.Site;

namespace ArcForges.Web.Tooling.Candidate;

/// <summary>
/// Verifies a sealed public candidate before anything is uploaded. It checks the seal, the exact member set, the wrangler
/// configuration, the pinned Worker, the identity, the public Site (by regenerating it from the sealed source and comparing
/// every byte), the static-page graph (no script without a hash, no event handlers, no frames, every stylesheet present),
/// and, when the repository is given, the licence, notice, SBOM and provenance receipt against the reviewed inputs.
/// </summary>
public static partial class CandidateVerifier
{
    private static readonly string[] ManifestKeys = ["dirty", "files", "schema", "source", "version"];
    private static readonly string[] WranglerKeys = ["assets", "compatibility_date", "main", "name", "no_bundle", "preview_urls", "workers_dev"];
    private static readonly string[] ForbiddenAssetPatterns = ["/.env", "/.dev.vars", "/node_modules", "/server", "/.vite", "/__spa-fallback"];

    /// <summary>Verifies the candidate at <paramref name="directory"/>. Throws <see cref="InvalidOperationException"/> at the first failed rule.</summary>
    /// <returns>The verified manifest's version, source and member count.</returns>
    public static async Task<CandidateSummary> VerifyAsync(
        string directory,
        string? repository,
        string? expectedSource,
        CancellationToken cancellationToken = default)
    {
        var tree = CandidateCore.ReadTree(directory);
        if (!tree.TryGetValue(CandidateCore.ManifestPath, out var manifestBytes))
            throw Fail("The candidate has no manifest.");
        var manifest = JsonNode.Parse(manifestBytes)?.AsObject() ?? throw Fail("The manifest is not a JSON object.");
        Require(manifest.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(ManifestKeys),
            "The manifest keys changed.");
        Require(manifest["schema"]?.GetValue<int>() == CandidateCore.ManifestSchema, "Unsupported manifest schema.");
        var version = manifest["version"]?.GetValue<string>() ?? throw Fail("The manifest has no version.");
        var source = manifest["source"]?.GetValue<string>() ?? throw Fail("The manifest has no source.");
        var dirty = manifest["dirty"]?.GetValue<bool>() ?? throw Fail("The manifest has no dirty flag.");
        Require(CandidateBuilder.VersionPattern().IsMatch(version), "Invalid candidate version.");
        Require(CandidateBuilder.SourcePattern().IsMatch(source), "Invalid source revision.");
        if (expectedSource is not null)
            Require(source == expectedSource, "Candidate source does not match this run.");

        var listed = manifest["files"]?.AsObject() ?? throw Fail("The manifest has no file map.");
        var listedPaths = listed.Select(pair => pair.Key).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var actualPaths = tree.Keys.Where(path => path != CandidateCore.ManifestPath).ToArray();
        Require(listedPaths.SequenceEqual(actualPaths), "Candidate file set changed.");
        foreach (var (path, hash) in listed)
            Require(CandidateCore.Sha256(tree[path]) == hash?.GetValue<string>(), "Candidate changed: " + path);

        foreach (var path in actualPaths.Where(path => path.StartsWith(CandidateCore.AssetsPrefix, StringComparison.Ordinal)))
        {
            Require(!path.EndsWith(".map", StringComparison.Ordinal), "Source map in public assets: " + path);
            foreach (var pattern in ForbiddenAssetPatterns)
                Require(!("/" + path).Contains(pattern, StringComparison.Ordinal), "Private or build file in public assets: " + path);
        }
        foreach (var required in CandidateCore.RequiredMembers)
            Require(tree.ContainsKey(required), "Missing required file: " + required);

        VerifyWrangler(tree, repository);
        CandidateCore.RequireWorker(tree[CandidateCore.WorkerPath]);
        VerifyIdentity(tree, source, version, dirty);
        VerifyReceipt(tree, source, version, repository);
        await VerifySiteAsync(tree, source, cancellationToken).ConfigureAwait(false);
        VerifyStaticGraph(tree);
        if (repository is not null)
            VerifyReviewedInputs(tree, repository);
        return new CandidateSummary(version, source, dirty, tree.Count - 1);
    }

    private static void VerifyWrangler(SortedDictionary<string, byte[]> tree, string? repository)
    {
        var config = JsonNode.Parse(tree[CandidateCore.WranglerPath])?.AsObject() ?? throw Fail("Invalid wrangler.json.");
        Require(config.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(WranglerKeys),
            "Deployment configuration keys changed.");
        Require(config["name"]?.GetValue<string>() == "arcforges-web", "Deployment name changed.");
        Require(config["main"]?.GetValue<string>() == "./worker/index.js", "Deployment entry changed.");
        Require(config["no_bundle"]?.GetValue<bool>() == true, "Deployment must not bundle.");
        Require(config["preview_urls"]?.GetValue<bool>() == false, "Preview URLs must stay disabled.");
        Require(config["workers_dev"]?.GetValue<bool>() == false, "workers.dev must stay disabled.");
        var expectedAssets = JsonNode.Parse("""
            {"directory":"./assets","binding":"ASSETS","run_worker_first":true,"html_handling":"auto-trailing-slash","not_found_handling":"404-page"}
            """);
        Require(JsonNode.DeepEquals(config["assets"], expectedAssets), "Deployment asset routing changed.");
        if (repository is not null)
            Require(JsonNode.DeepEquals(config, CandidateCore.CandidateWrangler(repository)), "Deployment configuration changed.");
    }

    private static void VerifyIdentity(SortedDictionary<string, byte[]> tree, string source, string version, bool dirty)
    {
        var identity = JsonNode.Parse(tree[CandidateCore.AssetsPrefix + "__build.json"])?.AsObject()
            ?? throw Fail("Invalid __build.json.");
        Require(identity["source"]?.GetValue<string>() == source && identity["version"]?.GetValue<string>() == version,
            "The build identity does not match the manifest.");
        var info = JsonNode.Parse(tree[CandidateCore.AssetsPrefix + "__build-info.json"])?.AsObject()
            ?? throw Fail("Invalid __build-info.json.");
        CandidateBuilder.RequireIdentity(tree[CandidateCore.AssetsPrefix + "__build-info.json"], source, version, dirty);
        Require(info["build"]?["sourceCommit"]?.GetValue<string>() == source, "The build info names another source commit.");
    }

    private static void VerifyReceipt(SortedDictionary<string, byte[]> tree, string source, string version, string? repository)
    {
        var receipt = JsonNode.Parse(tree[CandidateCore.ReceiptPath])?.AsObject() ?? throw Fail("Invalid receipt.");
        Require(receipt["schemaVersion"]?.GetValue<int>() == 1 && receipt["repository"]?.GetValue<string>() == "Web",
            "Unsupported receipt.");
        Require(receipt["source"]?.GetValue<string>() == source && receipt["version"]?.GetValue<string>() == version,
            "The receipt does not match the manifest.");
        var members = new JsonObject();
        foreach (var (path, bytes) in tree.Where(pair => pair.Key != CandidateCore.ManifestPath && pair.Key != CandidateCore.ReceiptPath))
            members[path] = CandidateCore.Sha256(bytes);
        Require(JsonNode.DeepEquals(receipt["members"], members), "The receipt members differ from the sealed files.");
        if (repository is not null)
            Require(receipt["policySha256"]?.GetValue<string>() == CandidateCore.PolicySha256(repository),
                "The receipt binds another dependency policy.");
    }

    private static async Task VerifySiteAsync(SortedDictionary<string, byte[]> tree, string source, CancellationToken cancellationToken)
    {
        // The public Site is regenerated from the sealed source and compared byte for byte, so a changed page, header,
        // policy or stylesheet cannot ship under a valid seal.
        var site = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = source }, cancellationToken).ConfigureAwait(false);
        var expected = site.Files.Select(file => CandidateCore.AssetsPrefix + file.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var identity in CandidateCore.IdentityAndLegalFiles)
            expected.Add(CandidateCore.AssetsPrefix + identity);
        var actual = tree.Keys.Where(path => path.StartsWith(CandidateCore.AssetsPrefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        Require(expected.SetEquals(actual), "The public assets differ from the reviewed Site inventory.");
        foreach (var file in site.Files)
            Require(tree[CandidateCore.AssetsPrefix + file.Path].SequenceEqual(file.Content),
                "Public Site bytes differ from the regenerated Site: " + file.Path);
    }

    private static void VerifyStaticGraph(SortedDictionary<string, byte[]> tree)
    {
        // The public pages are static: no external script, no event-handler attribute, no frame or embedded object, and
        // every inline script is covered by the page's policy hash. Each stylesheet must be a published asset.
        var headers = Encoding.UTF8.GetString(tree[CandidateCore.AssetsPrefix + "_headers"]);
        var policyLine = headers.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith("Content-Security-Policy:", StringComparison.Ordinal))
            ?? throw Fail("The headers carry no Content-Security-Policy.");
        var policy = policyLine.Trim()["Content-Security-Policy:".Length..].Trim();
        Require(policy.Contains("script-src 'self'", StringComparison.Ordinal), "The policy has no script-src 'self'.");
        foreach (var forbidden in new[] { "unsafe-inline", "unsafe-eval", "wasm-unsafe-eval" })
            Require(!policy.Contains(forbidden, StringComparison.Ordinal), "The public policy allows " + forbidden + ".");
        var hashes = policy.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.StartsWith("'sha256-", StringComparison.Ordinal))
            .Select(token => token.Trim('\'')["sha256-".Length..])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var page in CandidateCore.RequiredPages)
        {
            var path = CandidateCore.AssetsPrefix + page;
            var html = Encoding.UTF8.GetString(tree[path]);
            Require(!ExternalScript().IsMatch(html), "External script on a public page: " + page);
            Require(!EventAttribute().IsMatch(html), "Event-handler attribute on a public page: " + page);
            Require(!FrameElement().IsMatch(html), "Frame or embedded object on a public page: " + page);
            foreach (Match inline in InlineScript().Matches(html))
            {
                var body = inline.Groups["body"].Value;
                if (body.Length == 0)
                    continue;
                var digest = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)));
                Require(hashes.Contains(digest), "Inline script without a policy hash on " + page);
            }
            var stylesheets = 0;
            foreach (Match tag in LinkTag().Matches(html))
            {
                if (!StylesheetRel().IsMatch(tag.Value))
                    continue;
                stylesheets++;
                var href = HrefAttribute().Match(tag.Value);
                Require(href.Success, "Stylesheet without an href on " + page);
                Require(tree.ContainsKey(CandidateCore.AssetsPrefix + href.Groups["href"].Value.TrimStart('/')),
                    "Stylesheet not published: " + href.Groups["href"].Value);
            }
            Require(stylesheets == 1, "Each public page links exactly one published stylesheet: " + page);
        }
    }

    private static void VerifyReviewedInputs(SortedDictionary<string, byte[]> tree, string repository)
    {
        Require(tree[CandidateCore.AssetsPrefix + "license.txt"].SequenceEqual(CandidateCore.Licence(repository)),
            "Full AGPL legal text changed.");
        Require(tree[CandidateCore.AssetsPrefix + "third-party-notices.txt"].SequenceEqual(CandidateCore.Notices(repository)),
            "Required third-party notices changed.");
        Require(tree[CandidateCore.AssetsPrefix + "source-provenance.txt"].SequenceEqual(CandidateCore.SourceNotice(repository)),
            "Source notice changed.");
        Require(tree[CandidateCore.SbomPath].SequenceEqual(CandidateCore.BuildSbom(repository)),
            "Build SBOM identities or edges changed.");
        Require(tree[CandidateCore.RuntimeSbomPath].SequenceEqual(CandidateCore.RuntimeSbom()),
            "Runtime SBOM changed.");
    }

    private static InvalidOperationException Fail(string message) => new(message);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw Fail(message);
    }

    [GeneratedRegex(@"<script\b[^>]*\bsrc\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalScript();

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventAttribute();

    [GeneratedRegex(@"<(?:iframe|object|embed|frame|applet)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameElement();

    [GeneratedRegex(@"<script\b[^>]*>(?<body>[\s\S]*?)</script>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScript();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"\brel\s*=\s*""[^""]*\bstylesheet\b[^""]*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StylesheetRel();

    [GeneratedRegex(@"\bhref\s*=\s*""(?<href>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefAttribute();
}

/// <summary>The verified facts of a candidate.</summary>
public sealed record CandidateSummary(string Version, string Source, bool Dirty, int Members);
