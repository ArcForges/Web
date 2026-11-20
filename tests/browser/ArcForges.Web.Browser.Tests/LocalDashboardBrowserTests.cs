// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

/// <summary>The real local WEB-01 journey. Chromium supplies only a virtual passkey authenticator.</summary>
public sealed class LocalDashboardBrowserTests
{
    private const string DeviceApi = "/api/arcforges.management.v2.DeviceService/";
    private const string RunList = "/api/arcforges.sandbox.v2.RunService/List";
    private const string BeginStepUp = "/api/arcforges.publicapi.v1.IdentityService/BeginStepUp";
    private const string CompleteStepUp = "/api/arcforges.publicapi.v1.IdentityService/CompleteStepUp";

    [Fact]
    public async Task OwnerSignsInReadsRealEmptyAndOfflineStatesAndRevokesTheInstalledNode()
    {
        var environment = LocalOptIn.Current();
        var directory = environment.GetValueOrDefault("ARCFORGES_WEB01_EXCHANGE");
        if (!LocalOptIn.IsEnabled(environment) || string.IsNullOrEmpty(directory))
            Assert.Skip("This local-only browser journey needs the live WEB-01 peer and explicit local opt-in.");
        Assert.True(Path.IsPathFullyQualified(directory), "The scoped browser exchange must be absolute.");
        var token = TestContext.Current.CancellationToken;
        var launch = await ReadAsync(directory, "browser.json", token);
        var origin = new Uri(Text(launch, "origin"));
        Assert.True(origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttps && origin.AbsolutePath == "/" &&
            origin.UserInfo.Length == 0 && origin.Query.Length == 0 && origin.Fragment.Length == 0 &&
            origin == new Uri(environment[LocalOptIn.BaseUrlVariable]!));
        var spki = Text(launch, "spkiSha256");
        Assert.Equal(32, Convert.FromBase64String(spki).Length);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            ExecutablePath = environment.GetValueOrDefault(LocalOptIn.ChromiumPathVariable),
            Args = ["--ignore-certificate-errors-spki-list=" + spki, "--disable-background-networking"],
        });
        await using var context = await browser.NewContextAsync(new()
        {
            ServiceWorkers = ServiceWorkerPolicy.Block,
            ViewportSize = new() { Width = 1280, Height = 900 },
        });
        var outside = new ConcurrentQueue<bool>();
        var controlReplies = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var revokeStages = new ConcurrentQueue<string>();
        var attempts = new ConcurrentQueue<Task<RevokeWire>>();
        var cspViolations = new ConcurrentQueue<bool>();
        await context.RouteAsync("**/*", async route =>
        {
            if (Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var request) &&
                (SameAuthority(origin, request) || IsOwnConsentCallback(route.Request, origin, request)))
            {
                await route.ContinueAsync();
            }
            else
            {
                outside.Enqueue(true);
                await route.AbortAsync();
            }
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
                cspViolations.Enqueue(true);
        };
        page.Response += (_, response) =>
        {
            if (Uri.TryCreate(response.Url, UriKind.Absolute, out var url) && SameAuthority(origin, url) &&
                response.Ok && response.Request.Method == "POST")
            {
                var path = url.AbsolutePath;
                if (path.StartsWith(DeviceApi, StringComparison.Ordinal) || path == RunList)
                    controlReplies.AddOrUpdate(path, 1, static (_, count) => count + 1);
                if (path == DeviceApi + "Revoke" || path is BeginStepUp or CompleteStepUp)
                    revokeStages.Enqueue(path);
                if (path == DeviceApi + "Revoke")
                    attempts.Enqueue(CaptureRevokeWireAsync(response));
            }
        };
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable");
        await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2", ["transport"] = "internal", ["hasResidentKey"] = true,
                ["hasUserVerification"] = true, ["isUserVerified"] = true, ["automaticPresenceSimulation"] = true,
            },
        });

        await BootstrapThenSignOutAsync(page, origin, Text(launch, "bootstrap"));
        var shell = await page.GotoAsync(new Uri(origin, "/overview").AbsoluteUri)
            ?? throw new InvalidOperationException("The exact Dashboard shell did not respond.");
        var policy = await shell.HeaderValueAsync("content-security-policy");
        Assert.NotNull(policy);
        Assert.Contains("connect-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);

        await Link(page, "Sign in").ClickAsync();
        await Heading(page, "Sign in to ArcForges").WaitForAsync();
        Assert.True(SameAuthority(origin, new Uri(page.Url)));
        await RequirePostAsync(page, "/api/session/v1/web/authentication/complete", () => Button(page, "Sign in with a passkey").ClickAsync());
        await page.WaitForURLAsync(new Uri(origin, "/overview").AbsoluteUri);
        await Link(page, "Devices").ClickAsync();
        await Heading(page, "No enrolled devices").WaitForAsync();

        Signal(directory, "enroll-node");
        var offer = await ReadAsync(directory, "consent.json", token);
        var consent = new Uri(Text(offer, "verificationUri"));
        Assert.True(SameAuthority(origin, consent) && consent.AbsolutePath == "/api/connect" && consent.Fragment.StartsWith("#flow=", StringComparison.Ordinal));
        await page.GotoAsync(consent.AbsoluteUri);
        await Heading(page, "Connect this node?").WaitForAsync();
        await page.Locator("#node-fingerprint").WaitForAsync();
        await page.Locator("#approve button").ClickAsync();
        var observed = await ReadAsync(directory, "device.json", token);
        var deviceId = Text(observed, "deviceId");
        Assert.True(Guid.TryParseExact(deviceId, "D", out var device) && device.ToString("D") == deviceId);
        var detail = "/devices/" + deviceId;
        await page.GotoAsync(new Uri(origin, "/devices").AbsoluteUri);
        await Heading(page, "Enrolled devices").WaitForAsync();
        var row = page.Locator("a[href='" + detail + "']");
        await row.WaitForAsync();
        Assert.Single(await row.AllAsync());
        await row.ClickAsync();
        await page.WaitForURLAsync(new Uri(origin, detail).AbsoluteUri);
        await Heading(page, "Run activity").WaitForAsync();
        await page.GetByText("Run data unavailable", new() { Exact = true }).WaitForAsync();

        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
        await Link(page, "Skip to content").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.True(await page.Locator("#dashboard-content").EvaluateAsync<bool>("el => document.activeElement === el"));

        await CheckScopedRunsAsync(page, observed);
        var empty = page.GetByText("No runs in this Sandbox", new() { Exact = true });
        await empty.WaitForAsync();
        Assert.Equal(1, controlReplies.GetValueOrDefault(RunList));

        Signal(directory, "disconnect-node");
        await ReadAsync(directory, "offline.json", token);
        await Button(page, "Refresh").ClickAsync();
        await page.GetByText("Device offline", new() { Exact = true }).WaitForAsync();
        Assert.False(await empty.IsVisibleAsync(), "An offline device must not present a fresh empty result.");
        Assert.Equal(1, controlReplies.GetValueOrDefault(RunList));

        Signal(directory, "reconnect-node");
        await ReadAsync(directory, "rejoined.json", token);
        await Button(page, "Refresh").ClickAsync();
        await page.GetByText("Online", new() { Exact = true }).WaitForAsync();
        await page.GetByText("Run data unavailable", new() { Exact = true }).WaitForAsync();
        Assert.Equal(1, controlReplies.GetValueOrDefault(RunList));
        // A restored connection requires a fresh owner selection before each
        // new scoped observation. The app clears the former form and count.
        await CheckScopedRunsAsync(page, observed);
        await empty.WaitForAsync();
        Assert.Equal(2, controlReplies.GetValueOrDefault(RunList));
        Assert.Empty(await page.GetByRole(AriaRole.Link, new() { NameRegex = new("subscription|checkout", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).AllAsync());

        var revoke = Button(page, "Revoke access");
        await revoke.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var confirmation = page.GetByRole(AriaRole.Dialog, new() { Name = "Revoke device access?", Exact = true });
        await confirmation.WaitForAsync();
        var cancel = confirmation.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });
        await page.WaitForFunctionAsync("el => document.activeElement === el", await cancel.ElementHandleAsync());
        await page.Keyboard.PressAsync("Escape");
        await confirmation.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await page.WaitForFunctionAsync("el => document.activeElement === el", await revoke.ElementHandleAsync());

        await page.Keyboard.PressAsync("Enter");
        await confirmation.WaitForAsync();
        await RequirePostAsync(page, DeviceApi + "Revoke",
            () => confirmation.GetByRole(AriaRole.Button, new() { Name = "Revoke access", Exact = true }).ClickAsync());
        await Button(page, "Access revoked").WaitForAsync();
        Assert.Equal([DeviceApi + "Revoke", BeginStepUp, CompleteStepUp, DeviceApi + "Revoke"], revokeStages.ToArray());
        var revokeWire = await Task.WhenAll(attempts.ToArray());
        Assert.Equal(2, revokeWire.Length);
        // These two bounded typed bodies contain no cookie or credential
        // headers. The exact-Contracts Go peer interprets them in private.
        await WritePrivateAsync(directory, "revoke-wire.json", revokeWire, token);
        Signal(directory, "browser-revoked");

        Assert.True(controlReplies.GetValueOrDefault(DeviceApi + "List") >= 2, "The browser did not observe empty and populated actual typed device lists.");
        Assert.True(controlReplies.GetValueOrDefault(DeviceApi + "Inspect") >= 3, "The actual details, offline and online observations were not fetched.");
        Assert.Equal(2, controlReplies.GetValueOrDefault(DeviceApi + "Revoke"));
        Assert.Empty(outside);
        Assert.Empty(cspViolations);
        await WritePrivateAsync(directory, "browser-observed.json", new
        {
            browserVersion = browser.Version,
            deviceId,
            deviceLists = controlReplies.GetValueOrDefault(DeviceApi + "List"),
            deviceDetails = controlReplies.GetValueOrDefault(DeviceApi + "Inspect"),
            scopedRunLists = controlReplies.GetValueOrDefault(RunList),
            deviceRevokeAttempts = controlReplies.GetValueOrDefault(DeviceApi + "Revoke"),
            passkeyStepUp = true,
            locallyOptedIn = true,
        }, token);
    }

    private sealed record RevokeWire(byte[] Request, byte[] Response);

    private static async Task CheckScopedRunsAsync(IPage page, JsonElement scope)
    {
        await page.GetByText("Check runs in an authorized Sandbox", new() { Exact = true }).ClickAsync();
        foreach (var (label, field) in new[] { ("Node ID", "nodeId"), ("Sandbox ID", "sandboxId"),
            ("Grant ID", "grantId"), ("Grant revision", "grantRevision") })
        {
            var input = page.GetByLabel(label, new() { Exact = true });
            Assert.Equal(string.Empty, await input.InputValueAsync());
            await input.FillAsync(Text(scope, field));
        }
        await RequirePostAsync(page, RunList, () => Button(page, "Check runs").ClickAsync());
    }

    private static async Task<RevokeWire> CaptureRevokeWireAsync(IResponse response)
    {
        var request = response.Request.PostDataBuffer;
        var reply = await response.BodyAsync();
        if (request is null || request.Length is < 5 or > 4096 || reply.Length is < 5 or > 65536)
            throw new InvalidOperationException("The real device revoke did not carry one bounded request and response.");
        return new RevokeWire(request, reply);
    }

    private static async Task WritePrivateAsync<T>(string directory, string name, T value, CancellationToken token)
    {
        await using var file = File.Open(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(file, value, cancellationToken: token);
    }

    private static async Task BootstrapThenSignOutAsync(IPage page, Uri origin, string bootstrap)
    {
        await page.GotoAsync(new Uri(origin, "/api/account/bootstrap").AbsoluteUri);
        await page.Locator("#token").FillAsync(bootstrap);
        await page.Locator("#display-name").FillAsync("Local verification owner");
        await RequirePostAsync(page, "/api/session/v1/web/enrollment/complete", () => page.Locator("#create").ClickAsync());
        await page.GotoAsync(new Uri(origin, "/api/account/sign-out").AbsoluteUri);
        await RequirePostAsync(page, "/api/session/v1/web/logout", () => page.Locator("#sign-out").ClickAsync());
    }

    private static async Task RequirePostAsync(IPage page, string path, Func<Task> action)
    {
        var response = await page.RunAndWaitForResponseAsync(action,
            reply => reply.Request.Method == "POST" && new Uri(reply.Url).AbsolutePath == path);
        Assert.True(response.Ok, "Expected a successful request to " + path);
    }

    private static ILocator Link(IPage page, string name) => page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private static ILocator Heading(IPage page, string name) => page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true });

    private static bool SameAuthority(Uri first, Uri second) =>
        first.Scheme == second.Scheme && first.IdnHost == second.IdnHost && first.Port == second.Port && second.UserInfo.Length == 0;

    private static bool IsOwnConsentCallback(IRequest request, Uri dashboard, Uri candidate) =>
        request.IsNavigationRequest && request.Method == "GET" && candidate.Scheme == Uri.UriSchemeHttp &&
        candidate.Host is "127.0.0.1" or "[::1]" && !candidate.IsDefaultPort && candidate.Port != dashboard.Port &&
        candidate.UserInfo.Length == 0 && candidate.AbsolutePath == "/callback" &&
        candidate.Query.Contains("code=", StringComparison.Ordinal) && candidate.Query.Contains("state=", StringComparison.Ordinal);

    private static string Text(JsonElement value, string property) =>
        value.GetProperty(property).GetString() ?? throw new InvalidOperationException("A scoped fixture string was missing.");

    private static async Task<JsonElement> ReadAsync(string directory, string name, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromSeconds(70));
        var path = Path.Combine(directory, name);
        while (!limit.IsCancellationRequested)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, limit.Token));
                    return document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // The sole fixture writer may still be completing the new file.
                }
            }
            await Task.Delay(150, limit.Token);
        }
        throw new TimeoutException("The real peer did not reach the expected browser phase.");
    }

    private static void Signal(string directory, string name)
    {
        using var marker = File.Open(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }
}
