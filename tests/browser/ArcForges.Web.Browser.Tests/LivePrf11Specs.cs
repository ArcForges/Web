// SPDX-License-Identifier: AGPL-3.0-only
// PRF.11 live proof specs (U7). Test-only: they are compiled and skipped without the opt-in, and they are not executed by the
// offline gate or by CI (P2-017). A live run records its evidence locally under ARCFORGES_PRF11_RESULTS and is claimant-reported.
//
// Scope of this file:
//   - Shells and framework on the proof origin (HTTP only, no browser): the Account and Chat shells at /account/ and /chat/,
//     base href "/", the framework at the root, and the exact CSP on the served response.
//   - In-browser CSP: the Account and Chat shells load in the installed Chrome or Edge with JavaScript enabled, and no
//     Content-Security-Policy violation is reported. This is the check that the base-uri 'none' directive does not block the
//     base href (see docs/prf-11-profile-proof.md, the base-uri open point).
//   - Cloud parts: the anonymous greeting round trip, and the interaction-responsiveness (INP) capture. These are skipped as
//     "blocked on CLOUD.21/CLOUD.22, not proven" until the cloud switch is set. The exact int64, uint64 and decimal calls,
//     typed failures, session expiry and CSRF need an authenticated session or the CLOUD.21 methods, so they are a manual step
//     in docs/prf-11-runbook.md and are not automated here.
using System.Net;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LivePrf11Specs
{
    /// <summary>The exact policy of the profile paths, the same string the Tooling and App tests pin.</summary>
    private const string ExactPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    private static readonly string[] Profiles = ["/account/", "/chat/"];

    [Fact]
    public async Task TheShellsAreServedOnTheProofOriginWithRootBaseAndTheExactPolicy()
    {
        var environment = LocalOptIn.Current();
        if (!LivePrf11OptIn.IsEnabled(environment))
            Assert.Skip(LivePrf11OptIn.LiveOptInReason);
        var origin = LivePrf11OptIn.ProofOrigin(environment)!;

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        foreach (var path in Profiles)
        {
            var response = await http.GetAsync(new Uri(origin, path), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<base\\s[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            Assert.Contains("<base href=\"/\"", html, StringComparison.Ordinal);
            Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var policies), path);
            Assert.Equal(ExactPolicy, Assert.Single(policies));

            var framework = await http.GetAsync(new Uri(origin, "/_framework/blazor.webassembly.js"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, framework.StatusCode);
        }
    }

    [Fact]
    public async Task TheShellsLoadInTheInstalledBrowserWithNoContentSecurityPolicyViolation()
    {
        var environment = LocalOptIn.Current();
        if (!LivePrf11OptIn.IsEnabled(environment))
            Assert.Skip(LivePrf11OptIn.LiveOptInReason);
        var origin = LivePrf11OptIn.ProofOrigin(environment)!;
        var executable = environment.GetValueOrDefault(LivePrf11OptIn.ChromiumPathVariable);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var violations = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
                violations.Add(message.Text);
        };
        page.PageError += (_, error) =>
        {
            if (error.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
                violations.Add(error);
        };

        foreach (var path in Profiles)
        {
            var response = await page.GotoAsync(new Uri(origin, path).ToString());
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            // The profile renders its heading once the framework has started (the shell shows "Loading…" until then).
            await page.Locator("#app h1").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        }

        Assert.Empty(violations);
    }

    [Fact]
    public async Task TheAnonymousGreetingRoundTripsOnTheDeployedIngress()
    {
        var environment = LocalOptIn.Current();
        if (!LivePrf11OptIn.IsEnabled(environment))
            Assert.Skip(LivePrf11OptIn.LiveOptInReason);
        if (!LivePrf11OptIn.IsCloudReady(environment))
            Assert.Skip(LivePrf11OptIn.BlockedCloudReason);
        var origin = LivePrf11OptIn.ProofOrigin(environment)!;
        var executable = environment.GetValueOrDefault(LivePrf11OptIn.ChromiumPathVariable);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
        });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(new Uri(origin, "/chat/").ToString());
        await page.Locator("#app h1").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        await page.GetByLabel("Name").FillAsync("ArcForges");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
        await page.Locator(".transcript li").Filter(new LocatorFilterOptions { HasTextString = "Hello, ArcForges!" }).WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
    }

    [Fact]
    public async Task TheGreetingInteractionResponsivenessIsCapturedForTheRebaseline()
    {
        var environment = LocalOptIn.Current();
        if (!LivePrf11OptIn.IsEnabled(environment))
            Assert.Skip(LivePrf11OptIn.LiveOptInReason);
        if (!LivePrf11OptIn.IsCloudReady(environment))
            Assert.Skip(LivePrf11OptIn.BlockedCloudReason);
        var origin = LivePrf11OptIn.ProofOrigin(environment)!;
        var executable = environment.GetValueOrDefault(LivePrf11OptIn.ChromiumPathVariable);
        var results = environment.GetValueOrDefault(LivePrf11OptIn.ResultsVariable);
        Assert.False(string.IsNullOrWhiteSpace(results), "Set ARCFORGES_PRF11_RESULTS to a local folder for the measurements.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
        });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(new Uri(origin, "/chat/").ToString());
        await page.Locator("#app h1").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await page.EvaluateAsync(@"() => {
            window.__inp = [];
            new PerformanceObserver(list => {
                for (const entry of list.getEntries()) window.__inp.push(entry.duration);
            }).observe({ type: 'event', durationThreshold: 16, buffered: true });
        }");

        var samples = new List<double>();
        for (var index = 0; index < 5; index++)
        {
            await page.GetByLabel("Name").FillAsync("ArcForges");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" }).ClickAsync();
            await page.WaitForTimeoutAsync(1500);
        }
        var durations = await page.EvaluateAsync<double[]>("() => window.__inp");
        samples.AddRange(durations);

        // No budget is asserted here. The values are the measurement for the AL-06 interaction re-baseline (owner PRF.11).
        Directory.CreateDirectory(results);
        var path = Path.Combine(results, "inp-chat-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture) + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { origin = origin.ToString(), inpDurationsMs = samples }), TestContext.Current.CancellationToken);
    }
}
