// SPDX-License-Identifier: AGPL-3.0-only
// Port of the chat cases of tests/unit/app-routes.test.tsx: the Chat page rendered with bUnit over the same-origin gRPC-Web
// probe. The server is a test-only double; every reply is a real gRPC-Web frame sequence from the generated protobuf type.
using ArcForges.Contracts.Hello.V1;
using ArcForges.Web.App.Pages;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using AngleSharp.Dom;
using Bunit;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class ChatPageTests
{
    private const string Unavailable = "The server is unavailable. Try again later.";

    private static IElement SendButton(IRenderedComponent<Chat> cut) =>
        cut.FindAll("button").Single(button => button.TextContent.Trim() is "Send" or "Sending…");

    private static IElement? CancelButton(IRenderedComponent<Chat> cut) =>
        cut.FindAll("button").SingleOrDefault(button => button.TextContent.Trim() == "Cancel");

    private static HttpResponseMessage Greeting(string name) =>
        Responses.Raw(ProbeFixtures.HelloReply($"Hello, {name}!"), "application/grpc-web+proto");

    private static string NameOf(CapturedRequest request) =>
        SayHelloRequest.Parser.ParseFrom(request.Body.AsSpan(5).ToArray()).Name;

    private static void WaitIdle(IRenderedComponent<Chat> cut) =>
        cut.WaitForAssertion(() => Assert.DoesNotContain("Sending…", cut.Markup, StringComparison.Ordinal));

    [Fact]
    public void ChatSendsNothingUntilAskedThenShowsBothSidesOfOneAnonymousExchange()
    {
        var server = ScriptedServer.Always(() => Responses.Raw(ProbeFixtures.HelloReply("Hello, ArcForges!"), "application/grpc-web+proto"));
        using var context = new BunitContext();
        AccountPageTests.RegisterProbes(context, server);

        var cut = context.Render<Chat>();
        Assert.Equal(0, server.Count);
        SendButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains("Hello, ArcForges!", cut.Markup, StringComparison.Ordinal));

        Assert.Equal(1, server.Count);
        var request = Assert.Single(server.Requests);
        Assert.Equal($"{ProbeFixtures.Origin.Scheme}://{ProbeFixtures.Origin.Authority}{HelloProbe.HelloApiPath}", request.Url.ToString());
        Assert.False(request.HasCookieHeader);
        var items = cut.FindAll("li").Select(item => item.TextContent).ToList();
        Assert.Equal(new[] { "youArcForges", "serverHello, ArcForges!" }, items);
    }

    [Fact]
    public void ChatShowsTheServersTypedRefusalAsAFixedNoticeAndKeepsWorking()
    {
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => Responses.Raw(
                ProbeFixtures.Trailers("grpc-status: 3\r\ngrpc-message: Name%20must%20not%20be%20empty.\r\n"),
                "application/grpc-web+proto"),
            () => Responses.Raw(ProbeFixtures.HelloReply("Hello, again!"), "application/grpc-web+proto"),
        ]);
        using var context = new BunitContext();
        AccountPageTests.RegisterProbes(context, ScriptedServer.Always(() => answers.Dequeue()()));

        var cut = context.Render<Chat>();
        cut.Find("input").Input(string.Empty);
        SendButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains("The server rejected the request.", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("must not be empty", cut.Markup, StringComparison.Ordinal);

        cut.Find("input").Input("again");
        WaitIdle(cut);
        SendButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains("Hello, again!", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void ChatCanCancelAPendingMessageReportsItAsCancelledAndDoesNotDoubleSend()
    {
        var server = new ScriptedServer(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Greeting("unused");
        });
        using var context = new BunitContext();
        AccountPageTests.RegisterProbes(context, server);

        var cut = context.Render<Chat>();
        SendButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains("Sending…", cut.Markup, StringComparison.Ordinal));
        Assert.True(SendButton(cut).HasAttribute("disabled"));
        // A second submit while one is pending does not send again.
        cut.Find("form").Submit();
        Assert.Equal(1, server.Count);

        CancelButton(cut)!.Click();
        cut.WaitForAssertion(() => Assert.Contains("The request was cancelled.", cut.Markup, StringComparison.Ordinal));
        Assert.Null(CancelButton(cut));
        Assert.False(SendButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void ChatRendersUntrustedNamesAsTextAndKeepsOnlyTheLastTwentyEntries()
    {
        var echo = new ScriptedServer((request, _) => Task.FromResult(Greeting(NameOf(request))));
        using var context = new BunitContext();
        AccountPageTests.RegisterProbes(context, echo);

        var cut = context.Render<Chat>();
        cut.Find("input").Input("n");
        for (var index = 0; index < 11; index++)
        {
            SendButton(cut).Click();
            WaitIdle(cut);
        }
        Assert.Equal(20, cut.FindAll("li").Count);
        Assert.Equal(11, echo.Count);

        cut.Find("input").Input("<img src=x onerror=alert(1)>");
        SendButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public async Task LeavingTheChatPageCancelsTheMessageThatIsStillPending()
    {
        // The double hands over the token of the request it is answering; the request is pending until it is cancelled.
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new ScriptedServer(async (_, token) =>
        {
            started.TrySetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return Greeting("unused");
        });
        using var context = new BunitContext();
        AccountPageTests.RegisterProbes(context, server);

        var cut = context.Render<Chat>();
        SendButton(cut).Click();
        var observed = await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        Assert.False(observed.IsCancellationRequested);
        // Leaving the page disposes the rendered component (bUnit disposes components through DisposeComponentsAsync; the
        // context dispose only releases services), and the component cancels the message that is still pending.
        await context.DisposeComponentsAsync();
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public void TheTransportFailureIsUnavailableAndItIsAFixedNotice()
    {
        var server = new ScriptedServer((_, _) => throw new HttpRequestException("Failed to fetch"));
        using var context = new BunitContext();
        AccountPageTests.RegisterProbes(context, server);

        var cut = context.Render<Chat>();
        SendButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains(Unavailable, cut.Markup, StringComparison.Ordinal));
    }
}
