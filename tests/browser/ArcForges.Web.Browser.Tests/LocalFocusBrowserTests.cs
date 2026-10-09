// SPDX-License-Identifier: AGPL-3.0-only
// Local opt-in AX-02 focus check (P2-021 item 8; docs/web-40-ax-02-focus.md). bUnit does not move browser focus and axe does
// not detect focus loss, so this check drives a real page with the keyboard and asserts that the same element keeps focus
// through each operation. The same-origin answers are fixed by Playwright routes, so no live service is needed. It skips
// unless LocalOptIn is enabled; CI never builds this project.
using System.Text;
using Microsoft.Playwright;
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalFocusBrowserTests
{
    internal const string HelloPath = "**/api/arcforges.hello.v1.HelloService/SayHello";
    internal const string BootstrapPath = "**/session/v1/bootstrap";
    internal const string LogoutPath = "**/session/v1/logout";
    private const string Ended = "You are signed out. Reload to check again.";

    // The same wire fixtures as the bUnit suite (tests/ArcForges.Web.App.Tests/Fixtures/ProbeFixtures.cs), in browser form.
    internal const string AuthenticatedJson =
        "{\"csrfToken\":\"cccccccccccccccccccccccc\",\"authenticated\":true,"
        + "\"session\":{\"sessionId\":\"6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11\",\"expiresAt\":\"2026-10-03T08:00:00.000000Z\","
        + "\"idleExpiresAt\":\"2026-10-02T20:30:00.000000Z\",\"userId\":\"0f0e6a30-5d1c-4c7e-8b53-5b6b7f9a4a10\","
        + "\"deviceId\":\"8f2a1d77-1c1b-4b0a-9a55-2f1d0e5c7b21\",\"workspaceIds\":[\"3f1d3b1e-2a8c-4c44-a1c8-77a1e8f0b9a1\","
        + "\"a2b84f55-0c2d-4b0d-8e6a-5d6e44a6c7d3\"],\"recoveryGeneration\":\"18446744073709551615\",\"purpose\":\"authenticate\"},"
        + "\"profile\":{\"displayName\":\"Ada Lovelace\",\"locale\":\"en-GB\",\"timezone\":\"Europe/London\","
        + "\"revision\":\"9007199254740993\"}}";

    internal const string ReceiptJson =
        "{\"commandId\":\"7c9e6679-7425-40de-944b-e07fc1f90ae7\",\"effect\":\"happened\"}";

    /// <summary>One gRPC-Web frame: a flag byte, a big-endian 32-bit length and the payload.</summary>
    private static byte[] Frame(byte flag, byte[] payload)
    {
        var frame = new byte[payload.Length + 5];
        frame[0] = flag;
        frame[1] = (byte)(payload.Length >> 24);
        frame[2] = (byte)(payload.Length >> 16);
        frame[3] = (byte)(payload.Length >> 8);
        frame[4] = (byte)payload.Length;
        payload.CopyTo(frame, 5);
        return frame;
    }

    /// <summary>A binary gRPC-Web hello answer: the SayHelloResponse message (field 1) and an OK trailer.</summary>
    internal static byte[] HelloReply(string message)
    {
        var text = Encoding.UTF8.GetBytes(message);
        var payload = new byte[text.Length + 2];
        payload[0] = 0x0A;
        payload[1] = (byte)text.Length;
        text.CopyTo(payload, 2);
        var trailer = Encoding.UTF8.GetBytes("grpc-status: 0\r\n");
        var data = Frame(0x00, payload);
        var end = Frame(0x80, trailer);
        var reply = new byte[data.Length + end.Length];
        data.CopyTo(reply, 0);
        end.CopyTo(reply, data.Length);
        return reply;
    }

    /// <summary>True when the element is the one holding keyboard focus right now (identity, not a look-alike).</summary>
    private static Task<bool> HasFocusAsync(IPage page, IElementHandle element) =>
        page.EvaluateAsync<bool>("el => el === document.activeElement", element);

    /// <summary>
    /// Launches the browser the check drives. ARCFORGES_CHROMIUM_PATH, when set, names an installed Chromium for a machine whose
    /// Playwright browser build differs from the package's; otherwise the Playwright-managed build is used.
    /// </summary>
    private static Task<IBrowser> LaunchAsync(IPlaywright playwright, IReadOnlyDictionary<string, string?> environment)
    {
        var executable = environment.GetValueOrDefault(LocalOptIn.ChromiumPathVariable);
        return playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
        });
    }

    [Fact]
    public async Task ChatKeepsFocusOnSendWhileAMessageIsPendingAndAfterTheReply()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright, environment);
        var page = await browser.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(HelloPath, async route =>
        {
            // Hold the answer so that the pending state is observable, then answer with a real gRPC-Web reply.
            await release.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/grpc-web+proto",
                BodyBytes = HelloReply("Hello, ArcForges!"),
            });
        });

        await page.GotoAsync(new Uri(baseUrl, "/chat").ToString());
        var sendLocator = page.Locator("form button[type=submit]");
        await sendLocator.FocusAsync();
        var send = await sendLocator.ElementHandleAsync()
            ?? throw new InvalidOperationException("The Chat page has no Send button.");
        Assert.True(await HasFocusAsync(page, send), "Send did not take keyboard focus.");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync("form button[type=submit][aria-disabled=true]");
        // Pending: Send is aria-disabled, not disabled, so it keeps keyboard focus (AX-02).
        Assert.True(await HasFocusAsync(page, send), "Focus left Send while the message was pending.");
        Assert.False(await page.EvaluateAsync<bool>("el => el.disabled", send));

        release.SetResult();
        await page.WaitForSelectorAsync("ol.transcript li:nth-child(2)");
        Assert.True(await HasFocusAsync(page, send), "Focus left Send after the reply.");
        Assert.Equal("false", await page.EvaluateAsync<string?>("el => el.getAttribute('aria-disabled')", send));
    }

    [Fact]
    public async Task AccountKeepsFocusOnSignOutWhileSigningOutAndAfterTheSessionEnds()
    {
        var environment = LocalOptIn.Current();
        if (!LocalOptIn.IsEnabled(environment))
            Assert.Skip("Local opt-in only: set ARCFORGES_LOCAL_BROWSER=1 and ARCFORGES_BROWSER_BASE_URL on a developer machine; never on CI.");
        var baseUrl = new Uri(environment[LocalOptIn.BaseUrlVariable]!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright, environment);
        var page = await browser.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(BootstrapPath, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = AuthenticatedJson,
        }));
        await page.RouteAsync(LogoutPath, async route =>
        {
            await release.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = ReceiptJson,
            });
        });

        await page.GotoAsync(new Uri(baseUrl, "/account").ToString());
        var signOutLocator = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" });
        await signOutLocator.WaitForAsync();
        await signOutLocator.FocusAsync();
        var element = await signOutLocator.ElementHandleAsync()
            ?? throw new InvalidOperationException("The Account page has no Sign out button.");
        Assert.True(await HasFocusAsync(page, element), "Sign out did not take keyboard focus.");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync("button[aria-disabled=true]");
        // Working: Sign out is aria-disabled and keeps focus instead of being disabled (AX-02).
        Assert.True(await HasFocusAsync(page, element), "Focus left Sign out while the sign-out was in flight.");

        release.SetResult();
        await page.WaitForSelectorAsync($"text={Ended}");
        // Ended: the same element stays and offers the one remaining action, so focus is not dropped to the body.
        Assert.True(await HasFocusAsync(page, element), "Focus was dropped when the session ended.");
        Assert.Equal("Try again", (await element.TextContentAsync())?.Trim());
    }
}
