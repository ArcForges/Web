// SPDX-License-Identifier: AGPL-3.0-only
// Local opt-in no-script reading of the static Site (PRF.11 U5). The test runs only with ARCFORGES_LOCAL_BROWSER=1 and a base
// URL on a developer machine, never on CI, and it skips cleanly otherwise. The browser is the installed Chrome or Edge named by
// ARCFORGES_CHROMIUM_PATH, so no browser is downloaded. The offline no-script scan of the Site output is in
// tests/ArcForges.Web.Site.Tests (SiteOutputTests) and runs in CI.
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalNoScriptBrowserTests
{
    private static readonly string[] PublicPages = ["/", "/hello/", "/cloud-hello/"];

    [Fact]
    public async Task EveryPublicPageIsReadWithScriptingDisabledAndNoScriptIsRequested()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);
        var executable = environment.GetValueOrDefault(LocalOptIn.ChromiumPathVariable);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        var scriptRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.ResourceType == "script")
                scriptRequests.Add(request.Url);
        };

        foreach (var path in PublicPages)
        {
            var response = await page.GotoAsync(new Uri(baseUrl, path).ToString());
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            var text = (await page.Locator("main").First.InnerTextAsync()).Trim();
            Assert.False(string.IsNullOrEmpty(text), path);
            Assert.Equal(0, await page.Locator("script").CountAsync());
        }

        Assert.Empty(scriptRequests);
    }
}
