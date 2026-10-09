// SPDX-License-Identifier: AGPL-3.0-only
// Port of tests/unit/cloud-hello.test.ts (the server connection check) plus its failure and cancellation cases.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArcForges.Web.Ui;
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class ServerConnectionTests
{
    private static readonly Uri Origin = new("https://arcforges.com");

    [Fact]
    public async Task ConnectionUsesThePublishedBinaryClientSameOriginApiAndNoCredentials()
    {
        var handler = new ScriptedHandler(_ => Reply(HttpStatusCode.OK, GrpcWebFixture.HelloResponse(), GrpcWebFrames.MediaType));
        var client = new ServerConnectionClient(handler);

        var message = await client.CheckAsync(Origin, TestContext.Current.CancellationToken);

        Assert.Equal("Hello, ArcForges!", message);
        var seen = handler.Last ?? throw new InvalidOperationException("No request was sent.");
        Assert.Equal("https://arcforges.com" + ServerConnectionClient.HelloApiPath, seen.Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Contains("application/grpc-web+proto", seen.ContentType ?? string.Empty, StringComparison.Ordinal);
        Assert.False(seen.HasAuthorization);
        Assert.False(seen.HasCookie);
        Assert.Equal("ArcForges", GrpcWebFixture.DecodeHelloRequestName(seen.Body));
    }

    [Fact]
    public async Task TheSameCheckFromAnotherOriginPathUsesTheRootApiPath()
    {
        var handler = new ScriptedHandler(_ => Reply(HttpStatusCode.OK, GrpcWebFixture.HelloResponse(), GrpcWebFrames.MediaType));
        var client = new ServerConnectionClient(handler);

        await client.CheckAsync(new Uri("https://arcforges.com/hello/?name=ignored"), TestContext.Current.CancellationToken);

        Assert.Equal("https://arcforges.com" + ServerConnectionClient.HelloApiPath, handler.Last?.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task MissingApiAndUnexpectedServerOutputNeverBecomeASuccessfulGreeting()
    {
        var answers = new (string Name, Func<HttpResponseMessage> Answer)[]
        {
            ("404", () => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not found") }),
            ("405", () => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) { Content = new StringContent("Method not allowed") }),
            ("html", () => Reply(HttpStatusCode.OK, Encoding.UTF8.GetBytes("<html>Not an API</html>"), "text/html")),
            ("other service", () => Reply(HttpStatusCode.OK, GrpcWebFixture.HelloResponse("Hello, different service!"), GrpcWebFrames.MediaType)),
            ("no content type", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(GrpcWebFixture.HelloResponse()) }),
            ("redirect refused", () => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://other.example/") } }),
            ("server error", () => new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        };
        foreach (var (name, answer) in answers)
        {
            var client = new ServerConnectionClient(new ScriptedHandler(_ => answer()));
            var error = await Assert.ThrowsAsync<ServerConnectionException>(() => client.CheckAsync(Origin, TestContext.Current.CancellationToken));
            Assert.False(string.IsNullOrEmpty(error.Message), name);
        }
    }

    [Fact]
    public async Task AnUnreachableServerIsATypedFailureNotAGreeting()
    {
        var client = new ServerConnectionClient(new ScriptedHandler(_ => throw new HttpRequestException("network down")));

        await Assert.ThrowsAsync<ServerConnectionException>(() => client.CheckAsync(Origin, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheUsersCancellationAbortsTheRequestAndIsNotReportedAsAGreeting()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("the request must not complete"), waitForCancellation: true);
        var client = new ServerConnectionClient(handler);
        var pending = client.CheckAsync(Origin, cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(handler.WasCancelled);
    }

    [Fact]
    public async Task AlreadyCancelledCheckNeverReachesASuccess()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new ServerConnectionClient(new ScriptedHandler(_ => Reply(HttpStatusCode.OK, GrpcWebFixture.HelloResponse(), GrpcWebFrames.MediaType)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CheckAsync(Origin, cancellation.Token));
    }

    [Fact]
    public void TheDeadlineIsTenSecondsAsInTheReactExample()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), ServerConnectionClient.Deadline);
    }

    [Fact]
    public async Task AReplyBeyondTheCostLimitIsRefusedBeforeItIsParsed()
    {
        var oversized = new byte[ServerConnectionClient.MaximumReplyBytes + 1];
        var client = new ServerConnectionClient(new ScriptedHandler(_ => Reply(HttpStatusCode.OK, oversized, GrpcWebFrames.MediaType)));

        var error = await Assert.ThrowsAsync<ServerConnectionException>(() => client.CheckAsync(Origin, TestContext.Current.CancellationToken));
        Assert.Contains("larger than the limit", error.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, byte[] body, string mediaType)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed record Captured(Uri Uri, HttpMethod Method, string? ContentType, bool HasAuthorization, bool HasCookie, byte[] Body);

    /// <summary>Answers each request with a scripted reply and records what was sent.</summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, bool waitForCancellation = false) : HttpMessageHandler
    {
        public Captured? Last { get; private set; }

        public bool WasCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Last = new Captured(
                request.RequestUri ?? throw new InvalidOperationException("No request URI."),
                request.Method,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization is not null,
                request.Headers.Contains("Cookie"),
                body);
            if (waitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    throw;
                }
            }
            return respond(request);
        }
    }
}
