// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArcForges.Web.Tooling.Profiles;
using Xunit;

namespace ArcForges.Web.Tooling.Tests;

/// <summary>
/// The bundle build refusals of the Blazor profile bundle, ported from the two-client merge cases of
/// tests/unit/app-bundle.test.ts. The Blazor application is one publish that serves both profile routes, so each
/// refusal is exercised on a synthetic publish directory with a recorded static web assets manifest: a path that
/// collides with a generated profile or bundle path, a shell that is missing, bytes that differ from the recorded
/// integrity at one path, a file the manifest does not name, a manifest that names a missing file, a missing
/// manifest, and an unfingerprinted framework file. The positive cases are the deterministic digest, the one-archive
/// write and the tamper refusal of a verified archive.
/// </summary>
public sealed class ProfileBundlePublishTests
{
    private const string Shell =
        "<!DOCTYPE html><html><head><base href=\"/\" /></head><body><div id=\"app\">Loading</div>"
        + "<script src=\"_framework/blazor.webassembly.js\"></script></body></html>";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static Dictionary<string, byte[]> StandardFiles() => new(StringComparer.Ordinal)
    {
        ["index.html"] = Utf8(Shell),
        ["_framework/blazor.webassembly.js"] = Utf8("// loader"),
        ["_framework/dotnet.js"] = Utf8("// dotnet loader"),
        ["_framework/dotnet.runtime.a1b2c3d4e5.js"] = Utf8("export {};"),
        ["app.css"] = Utf8("body{margin:0}"),
    };

    /// <summary>A temporary publish directory. The recorded map is what the static web assets manifest names.</summary>
    private sealed class Publish : IDisposable
    {
        public Publish(IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, byte[]>? recorded = null, bool writeManifest = true, bool writeWwwroot = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "arcforges-bundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            if (!writeWwwroot)
                return;
            var wwwroot = Path.Combine(Root, "wwwroot");
            Directory.CreateDirectory(wwwroot);
            foreach (var (path, bytes) in files)
            {
                var target = Path.Combine(wwwroot, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
            if (writeManifest)
                WriteManifest(recorded ?? files);
        }

        public string Root { get; }

        public string Location => Root;

        private void WriteManifest(IReadOnlyDictionary<string, byte[]> recorded)
        {
            var endpoints = new System.Text.Json.Nodes.JsonArray();
            foreach (var (path, bytes) in recorded.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var integrity = "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes));
                endpoints.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["Route"] = path,
                    ["AssetFile"] = path,
                    ["EndpointProperties"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                    {
                        ["Name"] = "integrity",
                        ["Value"] = integrity,
                    }),
                });
            }
            var document = new System.Text.Json.Nodes.JsonObject
            {
                ["Version"] = 1,
                ["Endpoints"] = endpoints,
            };
            File.WriteAllText(System.IO.Path.Combine(Root, "app.staticwebassets.endpoints.json"), document.ToJsonString());
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static ProfileBundleResult BuildStandard()
    {
        using var publish = new Publish(StandardFiles());
        return ProfileBundle.Build(publish.Location);
    }

    [Fact]
    public void AStandardPublishBuildsADigestNamedBundleThatVerifies()
    {
        using var publish = new Publish(StandardFiles());

        var bundle = ProfileBundle.Build(publish.Location);
        var summary = ProfileBundle.Verify(bundle.Archive, bundle.Digest);
        var paths = TarArchive.Read(bundle.Archive).Select(entry => entry.Path).ToArray();

        Assert.Equal(ProfileBundle.BundleName(bundle.Digest), bundle.Name);
        Assert.Matches("^web-profiles-[a-f0-9]{64}\\.tar$", bundle.Name);
        Assert.Equal(bundle.Members, summary.Members);
        Assert.Equal(ProfileBundle.ManifestPath, paths[0]);
        Assert.Contains("account/index.html", paths);
        Assert.Contains("chat/index.html", paths);
        Assert.Contains(ProfileBundle.HeadersPath, paths);
        Assert.Contains("app.css", paths);
        Assert.DoesNotContain(ProfileBundle.ShellPath, paths);
    }

    [Fact]
    public void TheSameBytesGiveTheSameBundleAndAChangedFileGivesADifferentOne()
    {
        using var first = new Publish(StandardFiles());
        using var second = new Publish(StandardFiles());
        var changedFiles = StandardFiles();
        changedFiles["app.css"] = Utf8("body{margin:1px}");
        using var changed = new Publish(changedFiles);

        var original = ProfileBundle.Build(first.Location);

        Assert.Equal(original.Digest, ProfileBundle.Build(second.Location).Digest);
        Assert.NotEqual(original.Digest, ProfileBundle.Build(changed.Location).Digest);
    }

    [Theory]
    [InlineData("account/index.html")]
    [InlineData("chat/index.html")]
    [InlineData("chat/extra.html")]
    [InlineData("_headers")]
    [InlineData("manifest.json")]
    public void APublishThatCarriesAGeneratedOrReservedBundlePathIsRefused(string path)
    {
        var files = StandardFiles();
        files[path] = Utf8("<html>reserved</html>");
        using var publish = new Publish(files);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("reserved bundle path: " + path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APublishWithoutItsApplicationShellIsRefused()
    {
        var files = StandardFiles();
        files.Remove("index.html");
        using var publish = new Publish(files);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("no application shell", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APublishWithoutItsWwwrootDirectoryIsRefused()
    {
        using var publish = new Publish(new Dictionary<string, byte[]>(), writeWwwroot: false);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("no wwwroot directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWhoseBytesDifferFromItsRecordedIntegrityIsRefused()
    {
        // Two builds that fill one path with different bytes: the manifest records the original bytes of app.css.
        var recorded = StandardFiles();
        var files = StandardFiles();
        files["app.css"] = Utf8("body{margin:2px}");
        using var publish = new Publish(files, recorded);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("Served bytes differ from the build's integrity: app.css", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatTheStaticWebAssetsManifestDoesNotNameIsRefused()
    {
        var files = StandardFiles();
        files["extra.txt"] = Utf8("unlisted");
        var recorded = StandardFiles();
        using var publish = new Publish(files, recorded);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("not named by the build's static web assets manifest", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestThatNamesAMissingFileIsRefused()
    {
        var recorded = StandardFiles();
        recorded["ghost.css"] = Utf8("body{}");
        using var publish = new Publish(StandardFiles(), recorded);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("The manifest names a missing file: ghost.css", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APublishWithoutItsStaticWebAssetsManifestIsRefused()
    {
        using var publish = new Publish(StandardFiles(), writeManifest: false);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("exactly one static web assets manifest", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnfingerprintedFrameworkFileFailsTheBuildBeforeAnyBundleIsWritten()
    {
        var files = StandardFiles();
        files["_framework/app.js"] = Utf8("export {};");
        using var publish = new Publish(files);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Build(publish.Location));

        Assert.Contains("Unfingerprinted framework file: _framework/app.js", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingABundleLeavesOneDigestNamedArchiveAndKeepsOtherFiles()
    {
        using var publish = new Publish(StandardFiles());
        var output = Path.Combine(Path.GetTempPath(), "arcforges-bundle-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            File.WriteAllText(Path.Combine(output, "web-profiles-" + new string('0', 64) + ".tar"), "stale");
            File.WriteAllText(Path.Combine(output, "unrelated.txt"), "kept");
            var bundle = ProfileBundle.Build(publish.Location);

            ProfileBundle.Write(output, bundle);

            var names = Directory.EnumerateFiles(output).Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "unrelated.txt", bundle.Name }.Order(StringComparer.Ordinal).ToArray(), names);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void AnArchiveWhoseMemberChangedIsRefusedByItsManifest()
    {
        var bundle = BuildStandard();
        var entries = TarArchive.Read(bundle.Archive)
            .Select(entry => new TarArchive.Entry(entry.Path, (byte[])entry.Bytes.Clone()))
            .ToList();
        var target = entries.Single(entry => entry.Path == "app.css");
        target.Bytes[0] ^= 1;
        var tampered = TarArchive.Write(entries);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Verify(tampered));

        Assert.Contains("Content of app.css", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArchiveWithAnExtraMemberIsRefusedByItsManifest()
    {
        var bundle = BuildStandard();
        var entries = TarArchive.Read(bundle.Archive).ToList();
        entries.Add(new TarArchive.Entry("zz-extra.txt", Utf8("x")));
        var extra = TarArchive.Write(entries);

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Verify(extra));

        Assert.Contains("does not hold exactly the manifest's files", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArchiveWhoseDigestDoesNotMatchIsRefused()
    {
        var bundle = BuildStandard();

        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.Verify(bundle.Archive, new string('a', 64)));

        Assert.Contains("Bundle digest mismatch.", error.Message, StringComparison.Ordinal);
    }
}
