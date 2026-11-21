// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

/// <summary>Local keyboard and viewport checks of published Dashboard assets with a fixed session answer.</summary>
public sealed class LocalDashboardMockLayoutTests
{
    private const string CaptureDirectoryVariable = "ARCFORGES_WEB04_SCREENSHOTS";
    private static readonly Uri Origin = new("https://dashboard-fixture.example.test/");
    private const string Settings = """{"Dashboard":{"CenterOrigin":"https://dashboard-fixture.example.test","Mode":"self-hosted","Name":"Layout fixture"}}""";
    private const string Bootstrap = """{"csrfToken":"aaaaaaaaaaaaaaaaaaaaaaaa","authenticated":false}""";

    [Theory]
    [InlineData(320, 760, false)]
    [InlineData(390, 844, true)]
    [InlineData(1280, 900, false)]
    [InlineData(1440, 900, true)]
    public async Task PublishedShellSupportsKeyboardNavigationAndViewportWithoutAService(int width, int height, bool dark)
    {
        var environment = LocalOptIn.Current();
        var archive = environment.GetValueOrDefault(LocalAccessibilityBrowserTests.ProfileDirVariable);
        if (!LocalOptIn.IsEnabled(environment) || environment.GetValueOrDefault(LocalOptIn.BaseUrlVariable) != Origin.AbsoluteUri ||
            string.IsNullOrWhiteSpace(archive))
        {
            Assert.Skip("Opt into the synthetic Dashboard origin and supply the local published wwwroot.");
        }

        Assert.True(Path.IsPathFullyQualified(archive), "The archived wwwroot must be an absolute path.");
        var assetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(archive)) + Path.DirectorySeparatorChar;
        Assert.True(File.Exists(Path.Combine(assetRoot, "index.html")), "Publish the Dashboard before running the local layout fixture.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = environment.GetValueOrDefault(LocalOptIn.ChromiumPathVariable),
            Args = ["--disable-background-networking", "--host-resolver-rules=MAP * ~NOTFOUND"],
        });
        await using var context = await browser.NewContextAsync(new()
        {
            ServiceWorkers = ServiceWorkerPolicy.Block,
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var blocked = new ConcurrentQueue<string>();
        var token = TestContext.Current.CancellationToken;
        await context.RouteAsync("**/*", route => ServeAsync(route, assetRoot, blocked, token));
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(12_000);

        await page.GotoAsync(new Uri(Origin, "/overview").AbsoluteUri);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in to ArcForges", Exact = true })).ToBeVisibleAsync();
        var navigation = page.GetByRole(AriaRole.Navigation, new() { Name = "Dashboard" });
        var menu = page.Locator("button[aria-controls='dashboard-navigation']");
        if (width < 820)
        {
            await Assertions.Expect(menu).ToBeVisibleAsync();
            await Assertions.Expect(navigation).ToBeHiddenAsync();
            await menu.PressAsync("Enter");
            await Assertions.Expect(menu).ToHaveAttributeAsync("aria-expanded", "true");
            await Assertions.Expect(navigation).ToBeVisibleAsync();
            var overview = navigation.GetByRole(AriaRole.Link, new() { Name = "Overview", Exact = true });
            await overview.FocusAsync();
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(menu).ToHaveAttributeAsync("aria-expanded", "false");
            await Assertions.Expect(navigation).ToBeHiddenAsync();
            await Assertions.Expect(menu).ToBeFocusedAsync();
        }
        else
        {
            await Assertions.Expect(navigation).ToBeVisibleAsync();
            await Assertions.Expect(menu).ToBeHiddenAsync();
        }

        await page.GetByRole(AriaRole.Link, new() { Name = "Skip to content", Exact = true }).PressAsync("Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Main)).ToBeFocusedAsync();
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"));
        Assert.True(await page.EvaluateAsync<bool>("matchMedia('(prefers-reduced-motion: reduce)').matches"));
        Assert.Equal("auto", await page.EvaluateAsync<string>("getComputedStyle(document.documentElement).scrollBehavior"));
        Assert.Equal(dark, await page.EvaluateAsync<bool>("matchMedia('(prefers-color-scheme: dark)').matches"));

        var scan = await page.RunAxe();
        Assert.Empty(scan.Violations ?? []);
        Assert.Empty(blocked);

        if (environment.GetValueOrDefault(CaptureDirectoryVariable) is { Length: > 0 } captures)
        {
            Assert.True(Path.IsPathFullyQualified(captures) && Directory.Exists(captures), "Supply an existing private capture directory.");
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(captures, $"dashboard-mock-{width}x{height}-{(dark ? "dark" : "light")}.png"),
            });
        }
    }

    private static async Task ServeAsync(IRoute route, string assetRoot, ConcurrentQueue<string> blocked, CancellationToken token)
    {
        var request = new Uri(route.Request.Url);
        var path = request.AbsolutePath;
        if (request.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority) || route.Request.Method != "GET")
        {
            blocked.Enqueue(path);
            await route.AbortAsync();
            return;
        }

        if (path is "/appsettings.json" or "/api/session/v1/web/bootstrap")
        {
            await route.FulfillAsync(new()
            {
                Status = 200, ContentType = "application/json", Body = path == "/appsettings.json" ? Settings : Bootstrap,
                Headers = new Dictionary<string, string> { ["Cache-Control"] = "no-store" },
            });
            return;
        }

        var relative = path is "/" or "/overview" ? "index.html" : Uri.UnescapeDataString(path).TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(assetRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(assetRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !File.Exists(file) || path.StartsWith("/api/", StringComparison.Ordinal))
        {
            blocked.Enqueue(path);
            await route.AbortAsync();
            return;
        }

        await route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = Path.GetExtension(file) switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css",
                ".svg" => "image/svg+xml",
                ".js" or ".mjs" => "text/javascript",
                ".json" => "application/json",
                ".wasm" => "application/wasm",
                _ => "application/octet-stream",
            },
            BodyBytes = await File.ReadAllBytesAsync(file, token),
        });
    }
}
