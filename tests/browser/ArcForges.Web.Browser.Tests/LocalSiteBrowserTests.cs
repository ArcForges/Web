// SPDX-License-Identifier: AGPL-3.0-only
// Local opt-in checks of the static public Site (the successor of tests/browser/site.spec.ts). They skip unless LocalOptIn
// is enabled and never run on CI (P2-017). axe-core is injected only into the page under test, from a local script.
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalSiteBrowserTests
{
    private static readonly string[] PublicPages = ["/", "/hello/", "/cloud-hello/"];

    [Fact]
    public async Task PublicPagesReadWithJavaScriptDisabled()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();

        foreach (var path in PublicPages)
        {
            await page.GotoAsync(new Uri(baseUrl, path).ToString());
            var heading = (await page.Locator("h1").First.TextContentAsync() ?? string.Empty).Trim();
            Assert.False(string.IsNullOrEmpty(heading), path);
            Assert.Equal(0, await page.Locator("script").CountAsync());
        }
    }

    [Fact]
    public async Task ServerConnectionPageMakesNoAutomaticApiCall()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        var apiRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/", StringComparison.Ordinal))
                apiRequests.Add(request.Url);
        };

        await page.GotoAsync(new Uri(baseUrl, "/cloud-hello/").ToString());
        await page.WaitForTimeoutAsync(500);

        Assert.Empty(apiRequests);
    }

    [Fact]
    public async Task PublicPagesFitANarrowScreenWithoutHorizontalScrolling()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 375, Height = 812 } });

        foreach (var path in PublicPages)
        {
            await page.GotoAsync(new Uri(baseUrl, path).ToString());
            var overflow = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
            Assert.False(overflow, path);
        }
    }

    [Fact]
    public async Task PublicPagesPassTheAxeRulesWhenTheLocalScriptIsNamed()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var axeScript = environment.GetValueOrDefault(LocalOptIn.AxeScriptVariable);
        if (string.IsNullOrEmpty(axeScript))
            Assert.Skip("Set ARCFORGES_AXE_CORE_PATH to a local axe-core script to run the axe rules.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();

        foreach (var path in PublicPages)
        {
            await page.GotoAsync(new Uri(baseUrl, path).ToString());
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = axeScript });
            var result = await page.EvaluateAsync<JsonElement>("async () => await axe.run()");
            Assert.Equal(0, result.GetProperty("violations").GetArrayLength());
        }
    }
}
