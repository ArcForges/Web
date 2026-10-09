// SPDX-License-Identifier: AGPL-3.0-only
// Port of the account cases of tests/unit/app-routes.test.tsx: the Account page rendered with bUnit over the same-origin
// probe. The server is a test-only double; the wire documents are fixtures.
using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using ArcForges.Web.App.Pages;
using Microsoft.Extensions.DependencyInjection;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class AccountPageTests
{
    internal const string Ended = "You are signed out. Reload to check again.";

    /// <summary>Registers the probes over one scripted server. The handler is the only external input.</summary>
    internal static void RegisterProbes(BunitContext context, HttpMessageHandler server)
    {
        context.Services.AddSingleton(new ProbeOrigin(ProbeFixtures.Origin));
        context.Services.AddSingleton(server);
        context.Services.AddSingleton<SessionProbe>();
        context.Services.AddSingleton<HelloProbe>();
    }

    internal static IElement Button(IRenderedComponent<Account> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

    internal static bool HasButton(IRenderedComponent<Account> cut, string text) =>
        cut.FindAll("button").Any(button => button.TextContent.Trim() == text);

    [Fact]
    public void AccountReadsTheSessionOnceOnOpenAndPresentsItAsTextWithTheExactGeneration()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson)));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Contains("Ada Lovelace", cut.Markup, StringComparison.Ordinal));

        Assert.Equal(1, server.Count);
        Assert.Equal($"{ProbeFixtures.Origin.Scheme}://{ProbeFixtures.Origin.Authority}/session/v1/bootstrap", Assert.Single(server.Requests).Url.ToString());
        Assert.Equal("18446744073709551615", cut.Find("dd.exact").TextContent);
        Assert.Contains("en-GB", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Europe/London", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2026-10-03T08:00:00.000000Z", cut.Markup, StringComparison.Ordinal);
        var text = cut.Markup;
        foreach (var secret in new[]
                 {
                     ProbeFixtures.AuthenticatedCsrf,
                     "6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11",
                     "0f0e6a30-5d1c-4c7e-8b53-5b6b7f9a4a10",
                     "8f2a1d77-1c1b-4b0a-9a55-2f1d0e5c7b21",
                 })
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhileTheReadIsInFlightTheCheckingStateIsShownAndThenTheAnswerReplacesIt()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new ScriptedServer((_, _) => gate.Task);
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        await WaitUntilAsync(() => server.Count == 1);
        Assert.Contains("Checking your session…", cut.Markup, StringComparison.Ordinal);

        gate.SetResult(Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson)));
        cut.WaitForAssertion(() => Assert.Contains("You are not signed in.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Checking your session…", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountRendersAnAnonymousVisitorWithNoSignOutControl()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson)));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Contains("You are not signed in.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void AccountRendersDisplayTextFromTheServerAsTextNeverMarkup()
    {
        var node = ProbeFixtures.AuthenticatedNode();
        node["profile"]!["displayName"] = "<img src=x onerror=alert(1)>";
        var server = ScriptedServer.Always(() => Responses.Json(ProbeFixtures.Bytes(node)));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void SignOutPostsTheCsrfTokenFromThisPagesBootstrapAndThenShowsTheEndedState()
    {
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson)),
            () => Responses.Json(ProbeFixtures.ReceiptBody("happened")),
        ]);
        var server = ScriptedServer.Always(() => answers.Dequeue()());
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.True(HasButton(cut, "Sign out")));
        Button(cut, "Sign out").Click();
        cut.WaitForAssertion(() => Assert.Contains(Ended, cut.Markup, StringComparison.Ordinal));

        Assert.Equal(2, server.Count);
        var logout = server.Requests[1];
        Assert.Equal(HttpMethod.Post, logout.Method);
        Assert.Equal($"{ProbeFixtures.Origin.Scheme}://{ProbeFixtures.Origin.Authority}/session/v1/logout", logout.Url.ToString());
        Assert.Equal(ProbeFixtures.AuthenticatedCsrf, logout.CsrfHeader);
        Assert.DoesNotContain("Ada Lovelace", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionThatEndedOnTheServerIsShownAsEndedNotAsATransientFault()
    {
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson)),
            () => Responses.Text("{}", HttpStatusCode.Unauthorized),
        ]);
        using var context = new BunitContext();
        RegisterProbes(context, ScriptedServer.Always(() => answers.Dequeue()()));

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.True(HasButton(cut, "Sign out")));
        Button(cut, "Sign out").Click();
        cut.WaitForAssertion(() => Assert.Contains(Ended, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("[role=alert]"));
    }

    [Fact]
    public void ARefusedOrUnconfirmedSignOutIsReportedAsAFailureAndOffersAFreshRead()
    {
        var cases = new (Func<HttpResponseMessage> Answer, string Text)[]
        {
            (() => Responses.Text("{}", HttpStatusCode.Forbidden), "The server refused this request."),
            (() => Responses.Json(ProbeFixtures.ReceiptBody("didNotHappen")), "The server rejected the request."),
            (() => Responses.Json(ProbeFixtures.ReceiptBody("unknown")), "The server failed unexpectedly. Try again later."),
        };
        foreach (var (answer, text) in cases)
        {
            var answers = new Queue<Func<HttpResponseMessage>>(
            [
                () => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson)),
                answer,
            ]);
            using var context = new BunitContext();
            RegisterProbes(context, ScriptedServer.Always(() => answers.Dequeue()()));

            var cut = context.Render<Account>();
            cut.WaitForAssertion(() => Assert.True(HasButton(cut, "Sign out")));
            Button(cut, "Sign out").Click();
            cut.WaitForAssertion(() => Assert.Equal(text, cut.Find("[role=alert]").TextContent));
            Assert.DoesNotContain(Ended, cut.Markup, StringComparison.Ordinal);
            // The page does not claim a sign-out; it offers a fresh read (a new session and token).
            Assert.True(HasButton(cut, "Try again"));
            Assert.False(HasButton(cut, "Sign out"));
        }
    }

    [Fact]
    public void AFailedReadIsAnnouncedAndCanBeRetried()
    {
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => Responses.Text("down", HttpStatusCode.ServiceUnavailable),
            () => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson)),
        ]);
        var server = ScriptedServer.Always(() => answers.Dequeue()());
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Equal("The server is unavailable. Try again later.", cut.Find("[role=alert]").TextContent));
        Button(cut, "Try again").Click();
        cut.WaitForAssertion(() => Assert.Contains("You are not signed in.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(2, server.Count);
    }

    [Fact]
    public void AnUnreadableAnswerIsShownAsMalformedAndLeavesNoSession()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes("{\"csrfToken\":\"x\"}")));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Equal("The server answered with something this page cannot read.", cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public async Task LeavingThePageCancelsTheReadThatIsStillInFlight()
    {
        CancellationToken observed = default;
        var server = new ScriptedServer(async (_, token) =>
        {
            observed = token;
            await Task.Delay(Timeout.Infinite, token);
            return Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson));
        });
        using var context = new BunitContext();
        RegisterProbes(context, server);

        context.Render<Account>();
        await WaitUntilAsync(() => server.Count == 1);
        Assert.False(observed.IsCancellationRequested);
        // Disposing the rendered tree unmounts the page, which must cancel the read in flight.
        context.Dispose();
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public void AnAnonymousVisitorHasNoControlToTabTo()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson)));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Contains("You are not signed in.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(FocusOrder.TabSequence(cut));
        FocusOrder.AssertNoPositiveTabIndex(cut);
    }

    [Fact]
    public void ASignedInVisitorTabsToSignOutAfterTheSessionStatusAndSignOutIsANativeButton()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson)));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.True(HasButton(cut, "Sign out")));
        // Reading order: the live status region holds the session facts, then the one control.
        Assert.Equal(new[] { "status", "button" }, FocusOrder.ReadingOrder(cut));
        Assert.Equal(new[] { "button:Sign out" }, FocusOrder.TabSequence(cut));
        // A native button gives Enter and Space activation; type="button" keeps it out of any form submission.
        var signOut = Button(cut, "Sign out");
        Assert.Equal("button", signOut.LocalName);
        Assert.Equal("button", signOut.GetAttribute("type"));
        FocusOrder.AssertNoPositiveTabIndex(cut);
    }

    [Fact]
    public void AFailedReadAnnouncesItsAlertBeforeTheOnlyControlTryAgain()
    {
        var server = ScriptedServer.Always(() => Responses.Text("down", HttpStatusCode.ServiceUnavailable));
        using var context = new BunitContext();
        RegisterProbes(context, server);

        var cut = context.Render<Account>();
        cut.WaitForAssertion(() => Assert.Equal("The server is unavailable. Try again later.", cut.Find("[role=alert]").TextContent));
        Assert.Equal(new[] { "status", "alert", "button" }, FocusOrder.ReadingOrder(cut));
        Assert.Equal(new[] { "button:Try again" }, FocusOrder.TabSequence(cut));
        FocusOrder.AssertNoPositiveTabIndex(cut);
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition(), "The condition was not reached in time.");
    }
}
