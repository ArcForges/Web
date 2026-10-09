// SPDX-License-Identifier: AGPL-3.0-only
// Failure cases of the unary gRPC-Web reply, ported from the malformed-frame and status cases of tests/unit/app-hello.test.ts.
using ArcForges.Contracts.Hello.V1;
using ArcForges.Web.Ui;
using Google.Protobuf;
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class GrpcWebFramesTests
{
    [Fact]
    public void AWellFormedUnaryReplyYieldsTheMessageBytes()
    {
        var message = new SayHelloResponse { Message = "Hello, ArcForges!" }.ToByteArray();

        var decoded = GrpcWebFrames.DecodeUnaryReply(GrpcWebFixture.HelloResponse());

        Assert.Equal(message, decoded);
    }

    [Fact]
    public void FramesEncodedByTheProductAreReadBackByTheFixture()
    {
        var payload = new SayHelloRequest { Name = "ArcForges" }.ToByteArray();

        var frame = GrpcWebFrames.Encode(payload, GrpcWebFrames.DataFlag);

        Assert.Equal("ArcForges", GrpcWebFixture.DecodeHelloRequestName(frame));
    }

    public static TheoryData<string, byte[]> MalformedReplies
    {
        get
        {
            var data = GrpcWebFixture.Frame(new SayHelloResponse { Message = "Hello, ArcForges!" }.ToByteArray(), GrpcWebFixture.DataFlag);
            var trailersOk = GrpcWebFixture.Frame("grpc-status: 0\r\n"u8.ToArray(), GrpcWebFixture.TrailersFlag);
            return new TheoryData<string, byte[]>
            {
                { "empty body", [] },
                { "truncated header", data[..3] },
                { "truncated message", data[..^2] },
                { "no trailers", data },
                { "trailers only", trailersOk },
                { "trailers without a status", GrpcWebFixture.Concat(data, GrpcWebFixture.Frame("x-other: 1\r\n"u8.ToArray(), GrpcWebFixture.TrailersFlag)) },
                { "unparsable status", GrpcWebFixture.Concat(data, GrpcWebFixture.Frame("grpc-status: ok\r\n"u8.ToArray(), GrpcWebFixture.TrailersFlag)) },
                { "non-OK status", GrpcWebFixture.Concat(data, GrpcWebFixture.Frame("grpc-status: 3\r\n"u8.ToArray(), GrpcWebFixture.TrailersFlag)) },
                { "compressed frame", GrpcWebFixture.Concat([1, .. data[1..]], trailersOk) },
                { "frame length beyond the body", GrpcWebFixture.Concat([0, 0, 0, 0, 200, .. "x"u8.ToArray()], trailersOk) },
                { "bytes after the trailers", GrpcWebFixture.Concat(data, trailersOk, [0]) },
                { "data frame after the trailers", GrpcWebFixture.Concat(data, trailersOk, data) },
            };
        }
    }

    [Theory]
    [MemberData(nameof(MalformedReplies))]
    public void MalformedOrNonOkRepliesAreRefusedAndNeverResolve(string name, byte[] body)
    {
        var error = Assert.Throws<ServerConnectionException>(() => GrpcWebFrames.DecodeUnaryReply(body));
        Assert.False(string.IsNullOrEmpty(error.Message), name);
    }

    [Fact]
    public Task AMessageThatIsNotAProtobufSayHelloResponseIsRefusedByTheClient()
    {
        var garbage = GrpcWebFixture.Concat(
            GrpcWebFixture.Frame([0xFF, 0xFF, 0xFF], GrpcWebFixture.DataFlag),
            GrpcWebFixture.Frame("grpc-status: 0\r\n"u8.ToArray(), GrpcWebFixture.TrailersFlag));

        var client = new ServerConnectionClient(new HttpMessageHandlerStub(garbage));

        return Assert.ThrowsAsync<ServerConnectionException>(() => client.CheckAsync(new Uri("https://arcforges.com"), TestContext.Current.CancellationToken));
    }

    /// <summary>Answers one fixed binary body with the gRPC-Web media type.</summary>
    private sealed class HttpMessageHandlerStub(byte[] body) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent(body),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GrpcWebFrames.MediaType);
            return Task.FromResult(response);
        }
    }
}
