// SPDX-License-Identifier: AGPL-3.0-only
// Local opt-in accessibility re-proof of every replacement screen and state (P2-021 item 8; WEB.40 2026-10-09 adjudication;
// docs/web-40-accessibility.md). axe-core (Deque.AxeCore.Playwright, a test-only package) is injected only into the page
// under test, and each screen is run through the installed Edge or Chrome (ARCFORGES_CHROMIUM_PATH); no browser is
// downloaded. The pages are served from the local build outputs through Playwright routes, so no server and no live
// service is needed. The same-origin answers are fixed by routes. The suite skips unless ARCFORGES_LOCAL_BROWSER=1 and
// the build directories are named, and it is never built or run in CI.
using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalAccessibilityBrowserTests
{
    /// <summary>The wwwroot of the published Account and Chat profiles (artifacts/publish/app/wwwroot).</summary>
    internal const string ProfileDirVariable = "ARCFORGES_PROFILE_DIR";

    /// <summary>The static public Site output (artifacts/site).</summary>
    internal const string SiteDirVariable = "ARCFORGES_SITE_DIR";

    /// <summary>An optional file that receives one line per screen and state with the axe result.</summary>
    internal const string ReportVariable = "ARCFORGES_A11Y_REPORT";

    private const string Origin = "http://arcforges.test";
    private const string AnonymousJson = "{\"csrfToken\":\"aaaaaaaaaaaaaaaaaaaaaaaa\",\"authenticated\":false}";
    private static readonly object ReportLock = new();

    [Fact]
    public async Task AccountAnonymousPassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        session.Route = async route => await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = AnonymousJson });
        await session.GotoAsync("/account");
        await session.Page.WaitForSelectorAsync("text=You are not signed in.");
        await session.AssertAxeAsync("Account", "anonymous first read");
    }

    [Fact]
    public async Task AccountSignedInPassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        session.Route = async route => await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = LocalFocusBrowserTests.AuthenticatedJson });
        await session.GotoAsync("/account");
        await session.Page.WaitForSelectorAsync("button:has-text('Sign out')");
        await session.AssertAxeAsync("Account", "signed in");
    }

    [Fact]
    public async Task AccountSignOutInFlightPassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await session.Page.RouteAsync("**/session/v1/bootstrap", route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = LocalFocusBrowserTests.AuthenticatedJson }));
        await session.Page.RouteAsync("**/session/v1/logout", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = LocalFocusBrowserTests.ReceiptJson });
        });
        await session.GotoAsync("/account");
        await session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
        await session.Page.WaitForSelectorAsync("button[aria-disabled=true]:has-text('Signing out')");
        await session.AssertAxeAsync("Account", "sign-out in flight");
        release.SetResult();
        await session.Page.WaitForSelectorAsync("text=You are signed out. Reload to check again.");
        await session.AssertAxeAsync("Account", "session ended by sign-out (after the sign-out)");
    }

    [Fact]
    public async Task AccountFailedReadPassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        await session.Page.RouteAsync("**/session/v1/bootstrap", route => route.FulfillAsync(new RouteFulfillOptions { Status = 503, ContentType = "application/json", Body = "{}" }));
        await session.GotoAsync("/account");
        await session.Page.WaitForSelectorAsync("p[role=alert]");
        await session.AssertAxeAsync("Account", "failed read");
    }

    [Fact]
    public async Task ChatIdlePassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        await session.GotoAsync("/chat");
        await session.Page.WaitForSelectorAsync("form button[aria-disabled=false]");
        await session.AssertAxeAsync("Chat", "idle");
    }

    [Fact]
    public async Task ChatPendingPassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await session.Page.RouteAsync("**/api/arcforges.hello.v1.HelloService/SayHello", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/grpc-web+proto", BodyBytes = LocalFocusBrowserTests.HelloReply("Hello, ArcForges!") });
        });
        await session.GotoAsync("/chat");
        await session.Page.WaitForSelectorAsync("form button[aria-disabled=false]");
        await session.Page.Locator("form button[type=submit]").ClickAsync();
        await session.Page.WaitForSelectorAsync("form button[type=submit][aria-disabled=true]");
        await session.AssertAxeAsync("Chat", "pending");
        release.SetResult();
        await session.Page.WaitForSelectorAsync("ol.transcript li:nth-child(2)");
    }

    [Fact]
    public async Task ChatAfterReplyPassesAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: true);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        await session.Page.RouteAsync("**/api/arcforges.hello.v1.HelloService/SayHello", route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/grpc-web+proto", BodyBytes = LocalFocusBrowserTests.HelloReply("Hello, ArcForges!") }));
        await session.GotoAsync("/chat");
        await session.Page.WaitForSelectorAsync("form button[aria-disabled=false]");
        await session.Page.Locator("form button[type=submit]").ClickAsync();
        await session.Page.WaitForSelectorAsync("ol.transcript li:nth-child(2)");
        await session.AssertAxeAsync("Chat", "after reply");
    }

    [Fact]
    public async Task SitePagesPassAxe()
    {
        var harness = Harness.FromEnvironment(LocalOptIn.Current(), profile: false);
        if (harness is null)
            Assert.Skip(SkipMessage);
        await using var session = await harness.OpenAsync();
        foreach (var path in new[] { "/", "/hello/", "/cloud-hello/", "/404.html" })
        {
            await session.GotoAsync(path);
            await session.AssertAxeAsync("Site", $"page {path}");
        }
    }

    private const string SkipMessage =
        "Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1, ARCFORGES_PROFILE_DIR and ARCFORGES_SITE_DIR on a developer machine; never on CI.";

    /// <summary>Serves a build directory from routes, with the single-page fallback for the profiles.</summary>
    private sealed class Harness
    {
        private readonly IReadOnlyDictionary<string, string?> _environment;
        private readonly string _root;
        private readonly bool _spa;

        private Harness(IReadOnlyDictionary<string, string?> environment, string root, bool spa)
        {
            _environment = environment;
            _root = root;
            _spa = spa;
        }

        /// <summary>The harness for a screen group, or null when the local gate or the named directory is missing.</summary>
        internal static Harness? FromEnvironment(IReadOnlyDictionary<string, string?> environment, bool profile)
        {
            if (LocalOptIn.IsCi(environment) || !environment.TryGetValue(LocalOptIn.OptInVariable, out var optIn) || optIn != "1")
                return null;
            var variable = profile ? ProfileDirVariable : SiteDirVariable;
            if (!environment.TryGetValue(variable, out var root) || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return null;
            return new Harness(environment, Path.GetFullPath(root), profile);
        }

        internal async Task<Session> OpenAsync()
        {
            var playwright = await Playwright.CreateAsync();
            var executable = _environment.GetValueOrDefault(LocalOptIn.ChromiumPathVariable);
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
            });
            var page = await browser.NewPageAsync();
            var session = new Session(playwright, browser, page, _root, _spa);
            await page.RouteAsync(Origin + "/**", session.ServeAsync);
            return session;
        }
    }

    /// <summary>One browser page served from a build directory. Routes not set by the screen answer from the directory.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly string _root;
        private readonly bool _spa;

        internal Session(IPlaywright playwright, IBrowser browser, IPage page, string root, bool spa)
        {
            _playwright = playwright;
            _browser = browser;
            Page = page;
            _root = root;
            _spa = spa;
        }

        internal IPage Page { get; }

        /// <summary>A route the screen installs for the same-origin answer of its own request; null serves the directory.</summary>
        internal Func<IRoute, Task>? Route { get; set; }

        internal Task GotoAsync(string path) => Page.GotoAsync(Origin + path);

        internal async Task ServeAsync(IRoute route)
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (Route is not null && (path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/session/", StringComparison.Ordinal)))
            {
                await Route(route);
                return;
            }
            var file = Resolve(path);
            if (file is null)
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 404, ContentType = "text/plain", Body = "not found" });
                return;
            }
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = ContentType(file),
                BodyBytes = await File.ReadAllBytesAsync(file),
            });
        }

        /// <summary>Maps a request path to a file of the build directory; the profiles fall back to their shell.</summary>
        private string? Resolve(string path)
        {
            var relative = Uri.UnescapeDataString(path).TrimStart('/');
            var candidate = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                return null;
            if (Directory.Exists(candidate))
                candidate = Path.Combine(candidate, "index.html");
            if (File.Exists(candidate))
                return candidate;
            if (!_spa)
            {
                return null;
            }
            if (Path.HasExtension(candidate))
                return null;
            var shell = Path.Combine(_root, "index.html");
            return File.Exists(shell) ? shell : null;
        }

        private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript",
            ".css" => "text/css",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain; charset=utf-8",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };

        /// <summary>Runs axe-core on the page under test and records the result. Every violation fails the screen.</summary>
        internal async Task AssertAxeAsync(string screen, string state)
        {
            var result = await Page.RunAxe();
            var violations = result.Violations ?? [];
            var line = new StringBuilder()
                .Append(screen).Append(" | ").Append(state)
                .Append(" | url ").Append(new Uri(result.Url).AbsolutePath)
                .Append(" | violations ").Append(violations.Count)
                .Append(" | passes ").Append(result.Passes?.Count ?? 0)
                .Append(" | incomplete ").Append(result.Incomplete?.Count ?? 0)
                .Append(" | inapplicable ").Append(result.Inapplicable?.Count ?? 0);
            foreach (var violation in violations)
                line.Append(" | ").Append(violation.Id).Append(" (").Append(violation.Impact).Append(", ").Append(violation.Nodes.Count).Append(" nodes)");
            Record(line.ToString());
            Assert.True(violations.Count == 0, $"axe found violations on {screen} ({state}): {line}");
        }

        private static void Record(string line)
        {
            var report = Environment.GetEnvironmentVariable(ReportVariable);
            if (string.IsNullOrWhiteSpace(report))
                return;
            lock (ReportLock)
            {
                File.AppendAllText(report, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Page.CloseAsync();
            await _browser.CloseAsync();
            _playwright.Dispose();
        }
    }
}
