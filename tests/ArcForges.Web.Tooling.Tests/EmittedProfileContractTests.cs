// SPDX-License-Identifier: AGPL-3.0-only
// The emitted Blazor profile contract (PRF.11 U2). The bundle that the candidate job publishes is built from a publish
// whose application shell is the real host page of ArcForges.Web.App. The test reads the emitted archive, so it checks
// what is served: the Account and Chat shells with base href "/" (CLOUD.85 D1), and the exact CSP token set on both
// profile paths of the headers file. Nothing here is a fixture of the policy: the expected string is pinned.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArcForges.Web.Tooling.Profiles;
using Xunit;

namespace ArcForges.Web.Tooling.Tests;

public sealed class EmittedProfileContractTests
{
    /// <summary>
    /// The exact emitted policy, pinned in both test projects (ArcForges.Web.App.Tests pins the same string). base-uri is 'self'
    /// because the shells carry the base element (S36, WEB.40 follow-up Web #38); the Site policy keeps 'none'.
    /// </summary>
    private const string ExactPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'none'";

    private static readonly string HostPage = File.ReadAllText(
        Path.Combine(RepositoryRoot.Find(), "src", "ArcForges.Web.App", "wwwroot", "index.html"), Encoding.UTF8);

    /// <summary>The bundle built from a synthetic publish whose shell and framework names come from the real host page.</summary>
    private static ProfileBundleResult BuildFromHostPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "arcforges-emitted-" + Guid.NewGuid().ToString("N"));
        try
        {
            var wwwroot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(Path.Combine(wwwroot, "_framework"));
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["index.html"] = Encoding.UTF8.GetBytes(HostPage),
                ["app.css"] = Encoding.UTF8.GetBytes("body{margin:0}"),
                ["favicon.svg"] = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
                ["robots.txt"] = Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /\n"),
                ["_framework/blazor.webassembly.js"] = Encoding.UTF8.GetBytes("// loader"),
                ["_framework/dotnet.js"] = Encoding.UTF8.GetBytes("// dotnet"),
                ["_framework/ArcForges.Web.App.w1s8cjv5ju.wasm"] = Encoding.UTF8.GetBytes("\0asm"),
            };
            var endpoints = new JsonArray();
            foreach (var (path, bytes) in files)
            {
                var target = Path.Combine(wwwroot, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                endpoints.Add(new JsonObject
                {
                    ["Route"] = path,
                    ["AssetFile"] = path,
                    ["EndpointProperties"] = new JsonArray(new JsonObject
                    {
                        ["Name"] = "integrity",
                        ["Value"] = "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes)),
                    }),
                });
            }
            File.WriteAllText(Path.Combine(root, "app.staticwebassets.endpoints.json"),
                new JsonObject { ["Version"] = 1, ["Endpoints"] = endpoints }.ToJsonString());
            return ProfileBundle.Build(root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, string> Members(ProfileBundleResult bundle) =>
        TarArchive.Read(bundle.Archive).ToDictionary(entry => entry.Path, entry => Encoding.UTF8.GetString(entry.Bytes), StringComparer.Ordinal);

    [Fact]
    public void TheBundleServesExactlyTheAccountAndChatProfilesAndVerifies()
    {
        var bundle = BuildFromHostPage();

        Assert.Equal(new[] { "account", "chat" }, ProfileBundle.Profiles);
        var summary = ProfileBundle.Verify(bundle.Archive, bundle.Digest);
        Assert.Equal(bundle.Members, summary.Members);
    }

    [Fact]
    public void EachEmittedProfileShellHasExactlyOneRootBaseAndTheSameBytes()
    {
        var members = Members(BuildFromHostPage());

        foreach (var profile in ProfileBundle.Profiles)
        {
            var shell = members[profile + "/index.html"];
            var bases = Regex.Matches(shell, @"<base\s[^>]*>", RegexOptions.IgnoreCase);
            var only = Assert.Single(bases);
            Assert.Matches(@"^<base\s+href=""/""\s*/?>$", only.Value);
            Assert.Contains("_framework/blazor.webassembly.js", shell, StringComparison.Ordinal);
            Assert.Contains("<div id=\"app\"", shell, StringComparison.Ordinal);
        }
        Assert.Equal(members["account/index.html"], members["chat/index.html"]);
    }

    [Fact]
    public void TheHeadersCarryTheExactPolicyOnEachProfilePathAndNothingUnsafe()
    {
        var headers = Members(BuildFromHostPage())[ProfileBundle.HeadersPath];
        var lines = headers.Split('\n');

        // Each profile path block is followed by its Content-Security-Policy line, which is the exact pinned policy.
        foreach (var profile in ProfileBundle.Profiles)
        {
            var index = Array.IndexOf(lines, "/" + profile + "/*");
            Assert.True(index >= 0, "The headers file has no block for /" + profile + "/*.");
            Assert.Equal("  Content-Security-Policy: " + ExactPolicy, lines[index + 1]);
        }
        Assert.Equal(ProfileBundle.Profiles.Length, lines.Count(line => line.StartsWith("  Content-Security-Policy: ", StringComparison.Ordinal)));
        Assert.Contains("script-src 'self' 'wasm-unsafe-eval';", ExactPolicy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self';", ExactPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", headers, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", headers, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-hashes'", headers, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePolicyLineStaysWithinTheHeaderLineBudget()
    {
        Assert.True(ExactPolicy.Length < ProfileBundle.HeaderLineBudget);
    }
}
