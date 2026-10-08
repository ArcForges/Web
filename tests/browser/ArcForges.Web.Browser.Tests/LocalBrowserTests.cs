// SPDX-License-Identifier: AGPL-3.0-only
// Local opt-in browser checks (P2-021 item 8; WEB.40 section 6 decision 3). They skip unless LocalOptIn is enabled. The CI
// accessibility gate is the bUnit/xUnit semantic set; axe-core is injected here only into the page under test.
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalBrowserTests
{
    [Fact]
    public async Task AccountAndChatPagesRenderTheirHeadingsInAChromiumPage()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();

        await page.GotoAsync(new Uri(baseUrl, "/account").ToString());
        var heading = (await page.Locator("h1").TextContentAsync() ?? string.Empty).Trim();
        Assert.StartsWith("Your session,", heading, StringComparison.Ordinal);

        await page.GotoAsync(new Uri(baseUrl, "/chat").ToString());
        await page.Locator("button[type=submit]").ClickAsync();
        await page.WaitForSelectorAsync("ol.transcript li:nth-child(2)");
        Assert.Contains("Hello, ArcForges!", await page.Locator("ol.transcript").TextContentAsync() ?? string.Empty, StringComparison.Ordinal);

        var axeScript = environment.GetValueOrDefault(LocalOptIn.AxeScriptVariable);
        if (!string.IsNullOrEmpty(axeScript))
        {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = axeScript });
            var result = await page.EvaluateAsync<JsonElement>("async () => await axe.run()");
            Assert.Equal(0, result.GetProperty("violations").GetArrayLength());
        }
    }
}
