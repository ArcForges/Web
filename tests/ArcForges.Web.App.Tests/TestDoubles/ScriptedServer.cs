// SPDX-License-Identifier: AGPL-3.0-only
// TEST-ONLY DOUBLE. The same-origin server is an external input that these unit tests do not run; this double answers
// with the scripted responses the tests set up, and records each request as it was sent. It is never referenced by the
// application project and never part of a release graph.
using System.Net.Http.Headers;

namespace ArcForges.Web.App.Tests.TestDoubles;

/// <summary>What the test double observed of one request, captured before the transport disposed it.</summary>
public sealed record CapturedRequest(
    Uri Url,
    HttpMethod Method,
    string? CsrfHeader,
    bool HasCookieHeader,
    bool HasAuthorizationHeader,
    string? AcceptHeader,
    byte[] Body);

/// <summary>A scripted same-origin server: each request is answered by the given function, and cancellation reaches it.</summary>
public sealed class ScriptedServer : HttpMessageHandler
{
    private readonly Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> _answer;

    /// <summary>Creates the double with the answer for each request.</summary>
    public ScriptedServer(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        _answer = answer;
    }

    /// <summary>Every request that reached the double, in order.</summary>
    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>The number of requests that reached the double.</summary>
    public int Count => Requests.Count;

    /// <summary>Answers every request with the same response factory.</summary>
    public static ScriptedServer Always(Func<HttpResponseMessage> answer) =>
        new((_, _) => Task.FromResult(answer()));

    /// <summary>Records the request as sent, then answers it.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var captured = new CapturedRequest(
            request.RequestUri ?? throw new InvalidOperationException("The request has no URL."),
            request.Method,
            request.Headers.TryGetValues("X-AF-CSRF", out var csrf) ? csrf.SingleOrDefault() : null,
            request.Headers.Contains("Cookie"),
            request.Headers.Contains("Authorization"),
            request.Headers.Accept.Count == 0 ? null : request.Headers.Accept.ToString(),
            body);
        Requests.Add(captured);
        return await _answer(captured, cancellationToken);
    }
}

/// <summary>
/// A test-only response stream that yields the given chunks, then either ends or fails. It stands for a connection that
/// delivers the body in pieces and may break.
/// </summary>
public sealed class ChunkedStream(IReadOnlyList<byte[]> chunks, Exception? failWith = null) : Stream
{
    private int _index;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_index < chunks.Count)
        {
            var chunk = chunks[_index++];
            var count = Math.Min(chunk.Length, buffer.Length);
            chunk.AsSpan(0, count).CopyTo(buffer.Span);
            return ValueTask.FromResult(count);
        }
        if (failWith is not null)
            throw failWith;
        return ValueTask.FromResult(0);
    }
}

/// <summary>
/// A test-only endless response stream: each read yields a fixed number of bytes and counts the reads, and disposing it
/// records that the transfer was cut off.
/// </summary>
public sealed class CountingStream(byte fill, int bytesPerRead) : Stream
{
    /// <summary>The number of reads made so far.</summary>
    public long Reads { get; private set; }

    /// <summary>The total bytes yielded so far.</summary>
    public long BytesYielded { get; private set; }

    /// <summary>True once the stream was disposed, which is how a cut-off transfer is observed.</summary>
    public bool Disposed { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reads++;
        var count = Math.Min(bytesPerRead, buffer.Length);
        buffer.Span[..count].Fill(fill);
        BytesYielded += count;
        return ValueTask.FromResult(count);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Helpers that build the test-only response shapes.</summary>
public static class Responses
{
    /// <summary>A response with a JSON body and the given status.</summary>
    public static HttpResponseMessage Json(byte[] body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK, string contentType = "application/json")
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    /// <summary>A response with a text body and the given status and content type.</summary>
    public static HttpResponseMessage Text(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK, string? contentType = null)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body)) };
        if (contentType is not null)
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    /// <summary>A response with the given bytes and content type, for shapes the test controls exactly.</summary>
    public static HttpResponseMessage Raw(byte[] body, string? contentType, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        if (contentType is not null)
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }
}
