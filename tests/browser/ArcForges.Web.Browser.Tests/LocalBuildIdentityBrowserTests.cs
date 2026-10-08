// SPDX-License-Identifier: AGPL-3.0-only
// The local opt-in port of tests/browser/build-identity.spec.ts: a real browser reads the served build identity of the
// sealed candidate and compares it with the identity the candidate sealed. It skips unless LocalOptIn is enabled and
// never runs on CI (P2-017).
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalBuildIdentityBrowserTests
{
    [Fact]
    public async Task TheBrowserReadsTheSealedSourceRunIdentityWithNoStore()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);
        var sealedIdentity = Path.Combine(RepositoryRoot(), "artifacts", "candidate", "assets", "__build-info.json");
        if (!File.Exists(sealedIdentity))
            Assert.Skip("Build and verify the sealed candidate first (artifacts/candidate); see docs/validation.md.");
        var expected = JsonNode.Parse(File.ReadAllText(sealedIdentity));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(new Uri(baseUrl, "/").ToString());
        var observed = await page.EvaluateAsync<JsonElement>(
            """
            async () => {
              const response = await fetch('/__build-info.json', { cache: 'no-store' });
              if (!response.ok) throw new Error('Build identity returned ' + response.status);
              return { identity: await response.json(), cache: response.headers.get('cache-control') };
            }
            """);

        var identity = JsonNode.Parse(observed.GetProperty("identity").GetRawText());
        var cache = observed.GetProperty("cache").GetString() ?? string.Empty;
        Assert.True(JsonNode.DeepEquals(expected, identity), "The served identity differs from the sealed identity.");
        Assert.Contains("no-store", cache, StringComparison.Ordinal);
        Assert.Contains("no-transform", cache, StringComparison.Ordinal);
    }

    /// <summary>Walks up from the test output directory to the repository root (the directory with win.slnx).</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "win.slnx")) && File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The repository root with win.slnx and global.json was not found.");
    }
}
