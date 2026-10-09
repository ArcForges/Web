// SPDX-License-Identifier: AGPL-3.0-only
using System.Net.Http.Headers;
using ArcForges.Contracts.Hello.V1;
using Google.Protobuf;

namespace ArcForges.Web.Ui;

/// <summary>A refused or failed server connection. The message is for logs, never for the visitor.</summary>
public sealed class ServerConnectionException : Exception
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public ServerConnectionException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The server connection check of the cloud-hello example (the TypeScript checkServerConnection port): one anonymous
/// binary gRPC-Web SayHello call to the same-origin <c>/api</c> path, with a ten second deadline. It sends no
/// credentials, follows no redirect and accepts only the fixed greeting.
/// </summary>
public sealed class ServerConnectionClient
{
    /// <summary>The fixed name sent to the server.</summary>
    public const string RequestName = "ArcForges";

    /// <summary>The only reply accepted for <see cref="RequestName"/>.</summary>
    public const string ExpectedReply = "Hello, ArcForges!";

    /// <summary>The API path of the SayHello method, relative to the origin.</summary>
    public const string HelloApiPath = "/api/arcforges.hello.v1.HelloService/SayHello";

    /// <summary>The overall deadline of one check (the TypeScript defaultTimeoutMs of 10000).</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    /// <summary>The largest reply body read, a cost control for an unexpected response.</summary>
    public const int MaximumReplyBytes = 64 * 1024;

    private readonly HttpMessageInvoker _invoker;

    /// <summary>Creates a client over a message handler. The caller supplies a handler that keeps no cookies.</summary>
    public ServerConnectionClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
    }

    /// <summary>
    /// Sends the check to <paramref name="origin"/> and returns the greeting. Throws <see cref="ServerConnectionException"/>
    /// for any reply that is not the fixed greeting over the binary gRPC-Web form; the caller's cancellation propagates
    /// as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<string> CheckAsync(Uri origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        var payload = new SayHelloRequest { Name = RequestName }.ToByteArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, HelloApiPath))
        {
            Content = new ByteArrayContent(GrpcWebFrames.Encode(payload, GrpcWebFrames.DataFlag)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(GrpcWebFrames.MediaType);
        request.Headers.TryAddWithoutValidation("x-grpc-web", "1");
        using var response = await SendAsync(request, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ServerConnectionException($"The server answered HTTP {(int)response.StatusCode}.");
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, GrpcWebFrames.MediaType, StringComparison.OrdinalIgnoreCase))
            throw new ServerConnectionException("The reply is not the binary gRPC-Web media type.");
        var body = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
        SayHelloResponse message;
        try
        {
            message = SayHelloResponse.Parser.ParseFrom(GrpcWebFrames.DecodeUnaryReply(body));
        }
        catch (InvalidProtocolBufferException)
        {
            throw new ServerConnectionException("The reply is not a valid SayHello message.");
        }
        if (message.Message != ExpectedReply)
            throw new ServerConnectionException("Unexpected Hello response");
        return message.Message;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            return await _invoker.SendAsync(request, token).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ServerConnectionException("The server is unreachable: " + exception.GetType().Name);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new ServerConnectionException("The server did not answer before the deadline.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength is long declared && declared > MaximumReplyBytes)
            throw new ServerConnectionException("The reply is larger than the limit.");
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > MaximumReplyBytes)
                throw new ServerConnectionException("The reply is larger than the limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
