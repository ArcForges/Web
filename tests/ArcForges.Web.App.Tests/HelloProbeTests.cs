// SPDX-License-Identifier: AGPL-3.0-only
// Port of tests/unit/app-hello.test.ts: the anonymous binary gRPC-Web greeting through the generated Contracts client over
// Grpc.Net.Client.Web. The server is a test-only double; every answer is a real gRPC-Web frame sequence.
using System.Net;
using System.Text;
using ArcForges.Contracts.Foundation.Serialization;
using ArcForges.Contracts.Hello.V1;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using Google.Protobuf;
using Grpc.Core;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class HelloProbeTests
{
    private const string GrpcWeb = "application/grpc-web+proto";

    private static HelloProbe ProbeFor(HttpMessageHandler handler, TimeSpan? deadline = null) =>
        new(handler, new ProbeOrigin(ProbeFixtures.Origin), deadline ?? HelloProbe.DefaultDeadline);

    private static async Task<FailureKind> FailureOf(Func<Task> call)
    {
        var failure = await Assert.ThrowsAsync<ProbeFailureException>(call);
        return failure.Kind;
    }

    private static HttpResponseMessage Answer(byte[] body, string contentType = GrpcWeb, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    [Fact]
    public void TheDeadlineIsTenSecondsAndTheHelloPathIsTheSameOriginApiPath()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), HelloProbe.DefaultDeadline);
        Assert.Equal("/api/arcforges.hello.v1.HelloService/SayHello", HelloProbe.HelloApiPath);
    }

    [Fact]
    public async Task TheGreetingIsOneAnonymousBinaryGrpcWebCallToSameOriginApi()
    {
        var server = ScriptedServer.Always(() => Answer(ProbeFixtures.HelloReply("Hello, ArcForges 世界!")));
        var reply = await ProbeFor(server).SayHelloAsync("ArcForges 世界", CancellationToken.None);

        Assert.Equal("Hello, ArcForges 世界!", reply);
        var seen = Assert.Single(server.Requests);
        Assert.Equal($"{ProbeFixtures.Origin.Scheme}://{ProbeFixtures.Origin.Authority}{HelloProbe.HelloApiPath}", seen.Url.ToString());
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.False(seen.HasCookieHeader);
        Assert.False(seen.HasAuthorizationHeader);
        // The request frame: one uncompressed protobuf data frame carrying the name.
        Assert.Equal((byte)0, seen.Body[0]);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(seen.Body.AsSpan(1, 4));
        Assert.Equal(seen.Body.Length - 5, (int)length);
        Assert.Equal("ArcForges 世界", SayHelloRequest.Parser.ParseFrom(seen.Body.AsSpan(5).ToArray()).Name);
    }

    [Fact]
    public async Task EveryGrpcStatusMapsToAClosedFailureKindAndNoneIsASuccess()
    {
        var expected = new (StatusCode Code, FailureKind Kind)[]
        {
            (StatusCode.Cancelled, FailureKind.Unexpected),
            (StatusCode.Unknown, FailureKind.Unexpected),
            (StatusCode.InvalidArgument, FailureKind.Rejected),
            (StatusCode.DeadlineExceeded, FailureKind.Timeout),
            (StatusCode.NotFound, FailureKind.Rejected),
            (StatusCode.AlreadyExists, FailureKind.Rejected),
            (StatusCode.PermissionDenied, FailureKind.Forbidden),
            (StatusCode.ResourceExhausted, FailureKind.Limit),
            (StatusCode.FailedPrecondition, FailureKind.Rejected),
            (StatusCode.Aborted, FailureKind.Unavailable),
            (StatusCode.OutOfRange, FailureKind.Rejected),
            (StatusCode.Unimplemented, FailureKind.Rejected),
            (StatusCode.Internal, FailureKind.Unexpected),
            (StatusCode.Unavailable, FailureKind.Unavailable),
            (StatusCode.DataLoss, FailureKind.Unexpected),
            (StatusCode.Unauthenticated, FailureKind.Unauthenticated),
        };
        foreach (var (code, kind) in expected)
        {
            Assert.True(FailureMapping.FromGrpc(code) == kind, code.ToString());
            // The same status arriving as a real trailers-only gRPC-Web answer through the generated client.
            var server = ScriptedServer.Always(() => Answer(
                ProbeFixtures.Trailers($"grpc-status: {(int)code}\r\ngrpc-message: Server%20text\r\n")));
            Assert.True(await FailureOf(() => ProbeFor(server).SayHelloAsync("x", CancellationToken.None)) == kind, code.ToString());
        }
    }

    [Fact]
    public async Task AServerMessageIsNeverEchoedIntoTheFailure()
    {
        var server = ScriptedServer.Always(() => Answer(
            ProbeFixtures.Trailers("grpc-status: 3\r\ngrpc-message: secret-token-123\r\n")));
        var failure = await Assert.ThrowsAsync<ProbeFailureException>(() => ProbeFor(server).SayHelloAsync("x", CancellationToken.None));
        Assert.Equal(FailureKind.Rejected, failure.Kind);
        Assert.DoesNotContain("secret-token-123", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpStatusesAndNonGrpcAnswersAreTypedFailuresNeverALocalGreeting()
    {
        var answers = new (string Name, Func<HttpResponseMessage> Answer, FailureKind Kind)[]
        {
            ("404", () => Answer("Not found"u8.ToArray(), "text/plain", HttpStatusCode.NotFound), FailureKind.Rejected),
            ("405", () => Answer("Method not allowed"u8.ToArray(), "text/plain", (HttpStatusCode)405), FailureKind.Rejected),
            ("429", () => Answer("Slow down"u8.ToArray(), "text/plain", (HttpStatusCode)429), FailureKind.Limit),
            ("500", () => Answer("Broken"u8.ToArray(), "text/plain", HttpStatusCode.InternalServerError), FailureKind.Unexpected),
            ("503", () => Answer([], "text/plain", HttpStatusCode.ServiceUnavailable), FailureKind.Unavailable),
            ("504", () => Answer([], "text/plain", HttpStatusCode.GatewayTimeout), FailureKind.Timeout),
            ("html", () => Answer("<html>Not an API</html>"u8.ToArray(), "text/html"), FailureKind.Malformed),
            ("valid frames under another content type", () => Answer(ProbeFixtures.HelloReply(), "application/json"), FailureKind.Malformed),
            ("no content type", () => Raw(ProbeFixtures.HelloReply()), FailureKind.Malformed),
        };
        foreach (var (name, answer, kind) in answers)
            Assert.True(
                await FailureOf(() => ProbeFor(ScriptedServer.Always(answer)).SayHelloAsync("ArcForges", CancellationToken.None)) == kind,
                name);

        var network = new FailingTransport(new HttpRequestException("Failed to fetch"));
        Assert.True(await FailureOf(() => ProbeFor(network).SayHelloAsync("ArcForges", CancellationToken.None)) == FailureKind.Unavailable);
    }

    [Fact]
    public async Task MalformedFramesAndStatusNeverResolve()
    {
        var data = ProbeFixtures.HelloReply("Hello, ArcForges!");
        var messageLength = 5 + (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1, 4));
        var messageFrame = data[..messageLength];
        var bodies = new (string Name, byte[] Body)[]
        {
            ("empty body", []),
            ("truncated header", messageFrame[..3]),
            ("truncated message", messageFrame[..(messageLength - 2)]),
            ("no trailers", messageFrame),
            ("trailers without a status", ProbeFixtures.Concat(messageFrame, ProbeFixtures.Trailers("x-other: 1\r\n"))),
            ("unparsable status", ProbeFixtures.Concat(messageFrame, ProbeFixtures.Trailers("grpc-status: ok\r\n"))),
            (
                "compressed frame without negotiated compression",
                ProbeFixtures.Concat(new byte[] { 1 }.Concat(messageFrame.Skip(1)).ToArray(), ProbeFixtures.Trailers("grpc-status: 0\r\n"))),
            (
                "frame length beyond the body",
                ProbeFixtures.Concat([0, 0, 0, 1, 0, 1], ProbeFixtures.Trailers("grpc-status: 0\r\n"))),
            ("trailers only, status ok, no message", ProbeFixtures.Trailers("grpc-status: 0\r\n")),
            ("random bytes", Enumerable.Range(0, 64).Select(index => (byte)((index * 37 + 11) & 0xff)).ToArray()),
        };
        foreach (var (name, body) in bodies)
            Assert.True(
                await FailureOf(() => ProbeFor(ScriptedServer.Always(() => Answer(body))).SayHelloAsync("ArcForges", CancellationToken.None)) == FailureKind.Malformed,
                name);
    }

    [Fact]
    public async Task AReplyThatIsNotTheGreetingForThisNameIsMalformed()
    {
        var server = ScriptedServer.Always(() => Answer(ProbeFixtures.HelloReply("Hello, someone else!")));
        Assert.True(await FailureOf(() => ProbeFor(server).SayHelloAsync("ArcForges", CancellationToken.None)) == FailureKind.Malformed);
    }

    [Fact]
    public async Task AGreetingReplyAboveTheUnaryMessageBoundIsRefusedNeverAccepted()
    {
        // CON.92: a unary reply is bounded by the 4 MiB unary message class. A data frame whose message is over the class is
        // refused by the client's receive bound, and the probe never presents it as a greeting.
        var bound = (int)WireLimits.Bytes(WireLimit.UnaryMessage);
        var oversized = new SayHelloResponse { Message = new string('h', bound) }.ToByteArray();
        var body = ProbeFixtures.Concat(ProbeFixtures.Frame(oversized, 0), ProbeFixtures.Trailers("grpc-status: 0\r\n"));
        var server = ScriptedServer.Always(() => Answer(body));

        // The client refuses the frame before any greeting is decoded, and a successful status with no readable reply is malformed.
        Assert.Equal(FailureKind.Malformed, await FailureOf(() => ProbeFor(server).SayHelloAsync("ArcForges", CancellationToken.None)));
    }

    [Fact]
    public async Task TheUsersCancellationAbortsTheRequestAndIsReportedAsCancelled()
    {
        using var controller = new CancellationTokenSource();
        var transport = new NeverAnswers();
        var pending = ProbeFor(transport).SayHelloAsync("ArcForges", controller.Token);
        await transport.Started.Task;
        Assert.False(transport.Observed.IsCancellationRequested);
        await controller.CancelAsync();
        Assert.True(await FailureOf(() => pending) == FailureKind.Cancelled);
        Assert.True(transport.Observed.IsCancellationRequested);
    }

    [Fact]
    public async Task ACallThatHasAlreadyBeenCancelledNeverReachesASuccess()
    {
        using var controller = new CancellationTokenSource();
        await controller.CancelAsync();
        var server = ScriptedServer.Always(() => Answer(ProbeFixtures.HelloReply()));
        Assert.True(await FailureOf(() => ProbeFor(server).SayHelloAsync("ArcForges", controller.Token)) == FailureKind.Cancelled);
    }

    [Fact]
    public async Task AServerThatNeverAnswersEndsAtTheDeadlineAsATimeout()
    {
        // The ten second deadline is the page's; the test shortens it to keep the suite quick and asserts the value above.
        var transport = new NeverAnswers();
        var failure = await FailureOf(() => ProbeFor(transport, TimeSpan.FromMilliseconds(200)).SayHelloAsync("ArcForges", CancellationToken.None));
        Assert.True(failure == FailureKind.Timeout);
    }

    private static HttpResponseMessage Raw(byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        return response;
    }

    /// <summary>A test-only transport that fails before any answer.</summary>
    private sealed class FailingTransport(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(failure);
    }

    /// <summary>A test-only transport that never answers and records the token it was given, as a stalled fetch does.</summary>
    private sealed class NeverAnswers : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Observed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Observed = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
