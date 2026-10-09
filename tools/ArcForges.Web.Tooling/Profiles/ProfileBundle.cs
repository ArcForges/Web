// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArcForges.Web.Tooling.Candidate;
using ArcForges.Web.Ui;

namespace ArcForges.Web.Tooling.Profiles;

/// <summary>The result of one bundle build: the archive name is the digest of the archive bytes.</summary>
public sealed record ProfileBundleResult(string Name, string Digest, byte[] Archive, int Members);

/// <summary>The facts of a verified bundle.</summary>
public sealed record ProfileBundleSummary(string Name, string Digest, int Members);

/// <summary>
/// The Web profile bundle that the Cloud proof deployment consumes: the Blazor WebAssembly standalone application
/// (ArcForges.Web.App, published once and serving the Account and Chat routes) as one deterministic, digest-named archive.
/// The archive keeps the React bundle's name pattern (<c>web-profiles-&lt;sha256&gt;.tar</c>), its first entry
/// (<c>manifest.json</c>), its root <c>_headers</c> with one Content-Security-Policy per profile path, and its
/// <c>&lt;profile&gt;/index.html</c> pages. Blazor's framework files are root-relative under the page's <c>base href="/"</c>,
/// so the published tree is served at its own root paths and <c>assets/</c> is empty in this bundle. The shell and its
/// precompressed siblings (<c>index.html.br</c>, <c>index.html.gz</c>) are not served at the root; every other file is.
/// </summary>
public static partial class ProfileBundle
{
    /// <summary>The manifest schema of the bundle.</summary>
    public const int Schema = 1;

    /// <summary>The first entry of the archive.</summary>
    public const string ManifestPath = "manifest.json";

    /// <summary>The Cloudflare headers file at the archive root.</summary>
    public const string HeadersPath = "_headers";

    /// <summary>The application shell. It is the source of every profile page and is not served at the root.</summary>
    public const string ShellPath = "index.html";

    /// <summary>
    /// The shell's precompressed siblings that the SDK writes. They are encodings of the shell, so they are not served at the
    /// root either. Every other file of the publish is kept.
    /// </summary>
    public static readonly string[] ShellEncodingPaths = ["index.html.br", "index.html.gz"];

    /// <summary>The profile paths. The one published application serves both routes.</summary>
    public static readonly string[] Profiles = ["account", "chat"];

    /// <summary>The Cloudflare header line budget each profile policy must fit within.</summary>
    public const int HeaderLineBudget = 1800;

    /// <summary>
    /// The two framework loader scripts whose names carry no content fingerprint. They are revalidated on every load
    /// (no-cache); every other framework file must carry a fingerprint and is cached as immutable.
    /// </summary>
    public static readonly string[] UnfingerprintedFrameworkLoaders =
    [
        "_framework/blazor.webassembly.js",
        "_framework/dotnet.js",
    ];

    /// <summary>The framework path prefix served from the bundle root (Blazor's base href is "/").</summary>
    public const string FrameworkPrefix = "_framework/";

    /// <summary>The bundle file name for a digest.</summary>
    public static string BundleName(string digest) => "web-profiles-" + digest + ".tar";

    /// <summary>Builds the bundle from a publish directory (the output of <c>dotnet publish</c> of ArcForges.Web.App).</summary>
    public static ProfileBundleResult Build(string publishDirectory)
    {
        var publish = Path.GetFullPath(publishDirectory);
        var wwwroot = Path.Combine(publish, "wwwroot");
        if (!Directory.Exists(wwwroot))
            throw new InvalidOperationException("The publish output has no wwwroot directory.");
        var tree = CandidateCore.ReadTree(wwwroot);
        if (!tree.TryGetValue(ShellPath, out var shell))
            throw new InvalidOperationException("The publish output has no application shell.");
        RequireFingerprintedFramework(tree.Keys);
        VerifyStaticGraph(publish, tree);

        var page = CandidateCore.Lf(shell);
        var pageText = Encoding.UTF8.GetString(page);
        var served = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, bytes) in tree)
        {
            if (path == ShellPath || ShellEncodingPaths.Contains(path, StringComparer.Ordinal))
                continue;
            if (path == HeadersPath || path == ManifestPath || Profiles.Any(profile => path.StartsWith(profile + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("The publish output uses a reserved bundle path: " + path);
            served[path] = bytes;
        }
        var policies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            served[profile + "/index.html"] = page;
            policies[profile] = WasmContentSecurityPolicy.FromHostPages([pageText]);
        }
        served[HeadersPath] = Encoding.UTF8.GetBytes(HeadersText(policies));

        var buildDigest = BuildDigest(tree);
        var profileRows = new JsonObject();
        foreach (var profile in Profiles)
            profileRows[profile] = new JsonObject
            {
                ["page"] = profile + "/index.html",
                ["buildDigest"] = buildDigest,
                ["csp"] = policies[profile],
            };
        var fileRows = new JsonObject();
        foreach (var (path, bytes) in served)
            fileRows[path] = new JsonObject { ["sha256"] = CandidateCore.Sha256(bytes), ["bytes"] = bytes.Length };
        var manifest = new JsonObject
        {
            ["schema"] = Schema,
            ["profiles"] = profileRows,
            ["files"] = fileRows,
        };

        var entries = new List<TarArchive.Entry> { new(ManifestPath, CandidateCore.Json(manifest)) };
        entries.AddRange(served.Select(pair => new TarArchive.Entry(pair.Key, pair.Value)));
        var archive = TarArchive.Write(entries);
        var digest = CandidateCore.Sha256(archive);
        return new ProfileBundleResult(BundleName(digest), digest, archive, entries.Count);
    }

    /// <summary>
    /// Every framework file is either one of the named loaders or carries a content fingerprint (ten lower-case
    /// characters before its extension, as the SDK writes them). Its precompressed siblings (.br, .gz) follow the same rule.
    /// Anything else fails the build, so the immutable cache rule can only ever cover fingerprinted content.
    /// </summary>
    public static void RequireFingerprintedFramework(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!path.StartsWith(FrameworkPrefix, StringComparison.Ordinal))
                continue;
            var core = PrecompressedSuffix().Replace(path, string.Empty);
            if (UnfingerprintedFrameworkLoaders.Contains(core, StringComparer.Ordinal))
                continue;
            if (!FingerprintedFile().IsMatch(core))
                throw new InvalidOperationException("Unfingerprinted framework file: " + path);
        }
    }

    [GeneratedRegex(@"\.(?:br|gz)$", RegexOptions.CultureInvariant)]
    private static partial Regex PrecompressedSuffix();

    [GeneratedRegex(@"^_framework/[^/]+\.[a-z0-9]{10}\.(?:js|wasm|dat)$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintedFile();

    /// <summary>
    /// Writes the bundle into a directory. Earlier bundles of this name pattern are removed first, so the directory holds
    /// exactly one bundle afterwards.
    /// </summary>
    public static void Write(string outputDirectory, ProfileBundleResult bundle)
    {
        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        foreach (var name in Directory.EnumerateFiles(directory))
            if (BundleFilePattern().IsMatch(Path.GetFileName(name)))
                File.Delete(name);
        File.WriteAllBytes(Path.Combine(directory, bundle.Name), bundle.Archive);
    }

    /// <summary>
    /// Verifies a bundle archive: its name, its strict tar layout, its manifest and every member's digest, the profile
    /// pages, the policy of each profile path against its page, and the exact headers file.
    /// </summary>
    public static ProfileBundleSummary Verify(byte[] archive, string? expectedDigest = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var digest = CandidateCore.Sha256(archive);
        if (expectedDigest is not null)
            Require(digest == expectedDigest, "Bundle digest mismatch.");
        var entries = TarArchive.Read(archive);
        Require(entries.Count > 0 && entries[0].Path == ManifestPath, "The manifest must be the first entry.");
        var manifest = JsonNode.Parse(entries[0].Bytes)?.AsObject() ?? throw new InvalidOperationException("Invalid bundle manifest.");
        Require(manifest["schema"]?.GetValue<int>() == Schema, "Unsupported bundle schema.");
        var profileRows = manifest["profiles"]?.AsObject() ?? throw new InvalidOperationException("The manifest has no profiles.");
        Require(profileRows.Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(Profiles.OrderBy(name => name, StringComparer.Ordinal)),
            "The bundle does not hold exactly the reviewed profiles.");
        var fileRows = manifest["files"]?.AsObject() ?? throw new InvalidOperationException("The manifest has no files.");
        var rest = entries.Skip(1).ToArray();
        Require(rest.Select(entry => entry.Path).SequenceEqual(fileRows.Select(pair => pair.Key)),
            "The archive does not hold exactly the manifest's files in order.");
        foreach (var entry in rest)
        {
            var row = fileRows[entry.Path]?.AsObject() ?? throw new InvalidOperationException("Unlisted file " + entry.Path);
            Require(row["bytes"]?.GetValue<int>() == entry.Bytes.Length, "Size of " + entry.Path);
            Require(row["sha256"]?.GetValue<string>() == CandidateCore.Sha256(entry.Bytes), "Content of " + entry.Path);
        }
        Require(rest.All(entry => entry.Path != ShellPath), "The application shell must not be served at the root.");
        Require(rest.All(entry => !ShellEncodingPaths.Contains(entry.Path, StringComparer.Ordinal)),
            "The application shell's encodings must not be served at the root.");

        var members = rest.ToDictionary(entry => entry.Path, entry => entry.Bytes, StringComparer.Ordinal);
        var policies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            var pagePath = profile + "/index.html";
            Require(members.TryGetValue(pagePath, out var page), "Missing profile page " + pagePath);
            var pageText = Encoding.UTF8.GetString(page!);
            Require(pageText.Contains("<div id=\"app\"", StringComparison.Ordinal), "The profile page has no application root.");
            Require(pageText.Contains("_framework/blazor.webassembly.js", StringComparison.Ordinal),
                "The profile page does not load the WebAssembly runtime.");
            var policy = WasmContentSecurityPolicy.FromHostPages([pageText]);
            var row = profileRows[profile]?.AsObject() ?? throw new InvalidOperationException("No row for " + profile);
            Require(row["page"]?.GetValue<string>() == pagePath, "The profile row names another page.");
            Require(row["csp"]?.GetValue<string>() == policy, "The profile policy differs from its page.");
            policies[profile] = policy;
        }
        Require(members.TryGetValue(HeadersPath, out var headers), "Missing _headers.");
        Require(Encoding.UTF8.GetString(headers!) == HeadersText(policies), "The headers file differs from the reviewed rules.");
        return new ProfileBundleSummary(BundleName(digest), digest, entries.Count);
    }

    /// <summary>The exact headers file: the security headers, one policy per profile path, and the assets cache rule.</summary>
    public static string HeadersText(IReadOnlyDictionary<string, string> policies)
    {
        var lines = new List<string>
        {
            "/*",
            "  X-Content-Type-Options: nosniff",
            "  Referrer-Policy: no-referrer",
            "  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()",
            "  X-Robots-Tag: noindex, nofollow",
        };
        foreach (var profile in Profiles)
        {
            Require(policies.TryGetValue(profile, out var policy), "No policy for " + profile);
            Require(policy!.Length < HeaderLineBudget, "The " + profile + " policy exceeds the header line budget.");
            lines.Add("/" + profile + "/*");
            lines.Add("  Content-Security-Policy: " + policy);
            lines.Add("  X-Frame-Options: DENY");
            lines.Add("  Cache-Control: public, no-cache, no-transform");
        }
        lines.Add("/assets/*");
        lines.Add("  ! Cache-Control");
        lines.Add("  Cache-Control: public, max-age=31536000, immutable, no-transform");
        // Fingerprinted framework files are immutable; the two unfingerprinted loaders are revalidated on every load.
        lines.Add("/" + FrameworkPrefix + "*");
        lines.Add("  ! Cache-Control");
        lines.Add("  Cache-Control: public, max-age=31536000, immutable, no-transform");
        foreach (var loader in UnfingerprintedFrameworkLoaders)
        {
            lines.Add("/" + loader);
            lines.Add("  ! Cache-Control");
            lines.Add("  Cache-Control: public, no-cache, no-transform");
        }
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// The browser-graph check of the Blazor output: the publish's static web assets manifest names every served file
    /// with the integrity of its original resource. Each file must decode (Brotli or gzip variants included) to that
    /// integrity, and every published file must be named by the manifest. A changed, missing or unlisted file fails.
    /// </summary>
    private static void VerifyStaticGraph(string publish, SortedDictionary<string, byte[]> tree)
    {
        var manifests = Directory.GetFiles(publish, "*.staticwebassets.endpoints.json", SearchOption.TopDirectoryOnly);
        Require(manifests.Length == 1, "The publish output needs exactly one static web assets manifest.");
        var document = JsonNode.Parse(File.ReadAllBytes(manifests[0]))?.AsObject()
            ?? throw new InvalidOperationException("Invalid static web assets manifest.");
        Require(document["Version"]?.GetValue<int>() == 1, "Unsupported static web assets manifest version.");
        var endpoints = document["Endpoints"]?.AsArray() ?? throw new InvalidOperationException("No endpoints in the manifest.");
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in endpoints)
        {
            var endpoint = node?.AsObject() ?? throw new InvalidOperationException("Invalid endpoint.");
            var assetFile = endpoint["AssetFile"]?.GetValue<string>() ?? throw new InvalidOperationException("An endpoint has no asset file.");
            Require(tree.TryGetValue(assetFile, out var stored), "The manifest names a missing file: " + assetFile);
            covered.Add(assetFile);
            var integrity = endpoint["EndpointProperties"]?.AsArray()
                .Select(property => property?.AsObject())
                .FirstOrDefault(property => property?["Name"]?.GetValue<string>() == "integrity")?["Value"]?.GetValue<string>()
                ?? throw new InvalidOperationException("An endpoint has no integrity: " + assetFile);
            // An uncompressed file must carry the integrity of its own original bytes. A compressed variant carries either
            // the original resource's integrity or the integrity of its own compressed bytes (the ETag form the publish
            // records); both are bound to the served bytes, and the original is always recovered by decoding.
            var plain = assetFile.EndsWith(".br", StringComparison.Ordinal) ? Decompress(stored!, brotli: true)
                : assetFile.EndsWith(".gz", StringComparison.Ordinal) ? Decompress(stored!, brotli: false)
                : stored!;
            var original = "sha256-" + Convert.ToBase64String(SHA256.HashData(plain));
            var own = "sha256-" + Convert.ToBase64String(SHA256.HashData(stored!));
            if (plain == stored)
                Require(integrity == original, "Served bytes differ from the build's integrity: " + assetFile);
            else
                Require(integrity == original || integrity == own, "Served bytes differ from the build's integrity: " + assetFile);
        }
        Require(covered.SetEquals(tree.Keys), "A published file is not named by the build's static web assets manifest.");
    }

    private static byte[] Decompress(byte[] stored, bool brotli)
    {
        using var source = new MemoryStream(stored);
        using var output = new MemoryStream();
        Stream reader = brotli ? new BrotliStream(source, CompressionMode.Decompress) : new GZipStream(source, CompressionMode.Decompress);
        using (reader)
            reader.CopyTo(output);
        return output.ToArray();
    }

    private static string BuildDigest(SortedDictionary<string, byte[]> tree)
    {
        var text = new StringBuilder();
        foreach (var (path, bytes) in tree)
            text.Append(path).Append('\n').Append(CandidateCore.Sha256(bytes)).Append('\n');
        return CandidateCore.Sha256(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    [GeneratedRegex(@"^web-profiles-[0-9a-f]{64}\.tar$", RegexOptions.CultureInvariant)]
    private static partial Regex BundleFilePattern();
}
