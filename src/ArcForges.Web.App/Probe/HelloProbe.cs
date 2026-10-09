// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Net;
using System.Text;
using ArcForges.Contracts.Hello.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace ArcForges.Web.App.Probe;

/// <summary>
/// One anonymous greeting over binary gRPC-Web to same-origin <c>/api</c> (the TypeScript hello.ts port). The call uses the
/// generated Contracts <see cref="HelloService.HelloServiceClient"/> over <see cref="GrpcWebHandler"/>. It sends no cookie,
/// follows no redirect and ends at a ten second deadline. The failure kind is decided from what the transport saw before the
/// gRPC library mapped it, so a lossy library code never decides the kind alone.
/// </summary>
public sealed class HelloProbe
{
    /// <summary>The API path of the SayHello method, relative to the origin.</summary>
    public const string HelloApiPath = "/api/arcforges.hello.v1.HelloService/SayHello";

    /// <summary>The base of the gRPC path (the API prefix of the same origin).</summary>
    public const string ApiBasePath = "/api";

    /// <summary>The overall deadline of one greeting.</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(10);

    private readonly HttpMessageHandler _transport;
    private readonly Uri _apiBase;
    private readonly TimeSpan _deadline;

    /// <summary>Creates the probe with the ten second deadline.</summary>
    public HelloProbe(HttpMessageHandler transport, ProbeOrigin origin)
        : this(transport, origin, DefaultDeadline)
    {
    }

    /// <summary>Creates the probe with an explicit deadline (tests shorten it; the page uses the default).</summary>
    public HelloProbe(HttpMessageHandler transport, ProbeOrigin origin, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(origin);
        if (deadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deadline));
        _transport = transport;
        _apiBase = new Uri(origin.Value, ApiBasePath);
        _deadline = deadline;
    }

    /// <summary>Sends one anonymous greeting and returns the reply, which must be the greeting for this name.</summary>
    /// <exception cref="ProbeFailureException">Any typed failure of the call.</exception>
    public async Task<string> SayHelloAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        var seen = new Observation();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_deadline);
        using var channel = GrpcChannel.ForAddress(_apiBase, new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new ObservingHandler(_transport, seen)),
            DisposeHttpClient = false,
            ThrowOperationCanceledOnCancellation = true,
        });
        try
        {
            var client = new HelloService.HelloServiceClient(channel);
            var options = new CallOptions(
                deadline: DateTime.UtcNow.Add(_deadline),
                cancellationToken: deadline.Token);
            var reply = await client.SayHelloAsync(new SayHelloRequest { Name = name }, options).ResponseAsync.ConfigureAwait(false);
            if (reply.Message != $"Hello, {name}!")
                throw new ProbeFailureException(FailureKind.Malformed);
            return reply.Message;
        }
        catch (Exception exception) when (exception is not ProbeFailureException)
        {
            throw Classify(exception, seen, cancellationToken, deadline.IsCancellationRequested);
        }
    }

    /// <summary>
    /// The failure of a call that did not return a greeting. The caller's token and the transport decide first; the status the
    /// server sent in its trailers, as the transport read it, decides the rest. Library texts never decide a kind.
    /// </summary>
    internal ProbeFailureException Classify(Exception exception, Observation seen, CancellationToken userToken, bool deadlineFired)
    {
        if (userToken.IsCancellationRequested)
            return new ProbeFailureException(FailureKind.Cancelled, exception);
        if (seen.Foreign)
            return new ProbeFailureException(FailureKind.Malformed, exception);
        if (seen.Status is int status && status != (int)HttpStatusCode.OK)
            return new ProbeFailureException(FailureMapping.FromStatus(status), exception);
        if (seen.Network)
            return new ProbeFailureException(deadlineFired ? FailureKind.Timeout : FailureKind.Unavailable, exception);
        if (deadlineFired)
            return new ProbeFailureException(FailureKind.Timeout, exception);
        // A non-OK server status is the server's answer, whatever exception the library raised for it. A successful status
        // with no reply, or no readable status, is a malformed answer.
        if (seen.TrailerStatus is int code && code != (int)StatusCode.OK)
            return new ProbeFailureException(FailureMapping.FromGrpc((StatusCode)code), exception);
        // An answer that the page received and could not read is malformed; no answer at all is unexpected.
        return new ProbeFailureException(seen.Status is not null ? FailureKind.Malformed : FailureKind.Unexpected, exception);
    }

    /// <summary>Parses a gRPC status number (0 to 16, canonical digits), or returns null for any other text.</summary>
    internal static int? ParseStatus(string? text)
    {
        if (text is null || !int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return null;
        return number is >= 0 and <= 16 ? number : null;
    }

    /// <summary>What the transport saw of one call, before the gRPC library mapped it.</summary>
    internal sealed class Observation
    {
        /// <summary>The HTTP status of the answer, when one arrived.</summary>
        public int? Status { get; set; }

        /// <summary>True when the transport failed before an answer, including a cancelled transport.</summary>
        public bool Network { get; set; }

        /// <summary>True when a 200 answer was not gRPC-Web.</summary>
        public bool Foreign { get; set; }

        /// <summary>The grpc-status the server sent (in the HTTP headers or a trailers frame), or null when none was readable.</summary>
        public int? TrailerStatus { get; set; }
    }

    /// <summary>The anonymous transport: no credentials, and the observation of the answer.</summary>
    private sealed class ObservingHandler(HttpMessageHandler inner, Observation seen) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The gRPC library resolves the method path against the authority and drops the base path, so the same-origin
            // API prefix is restored here, once, and never doubled.
            var requested = request.RequestUri ?? throw new InvalidOperationException("The request has no URL.");
            if (!requested.AbsolutePath.StartsWith(ApiBasePath + "/", StringComparison.Ordinal))
                request.RequestUri = new UriBuilder(requested) { Path = ApiBasePath + requested.AbsolutePath }.Uri;
            // The greeting is anonymous: no cookie is sent with it.
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                seen.Network = true;
                throw;
            }

            if ((int)response.StatusCode == 0)
            {
                // An opaque redirect the page refuses to follow is a refused transport, not an answer.
                seen.Network = true;
                response.Dispose();
                throw new HttpRequestException("A redirect is refused.");
            }
            seen.Status = (int)response.StatusCode;
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (response.StatusCode == HttpStatusCode.OK
                && !mediaType.StartsWith("application/grpc-web", StringComparison.OrdinalIgnoreCase))
            {
                seen.Foreign = true;
                response.Dispose();
                throw new InvalidOperationException("The answer is not gRPC-Web.");
            }
            if (response.Headers.TryGetValues("grpc-status", out var headerStatus))
                seen.TrailerStatus = ParseStatus(headerStatus.FirstOrDefault());
            // The body is read by the library through the watcher, so the status of its trailers frame is seen here first.
            if (response.StatusCode == HttpStatusCode.OK && response.Content is not null)
                response.Content = new WatchedContent(response.Content, seen);
            return response;
        }
    }

    /// <summary>
    /// The answer body as the library reads it: the same bytes, passed through a frame watcher that records the grpc-status of
    /// a trailers frame (flag 0x80). The watcher reads no more than the library reads and never changes a byte.
    /// </summary>
    private sealed class WatchedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly FrameWatcher _watcher;

        public WatchedContent(HttpContent inner, Observation seen)
        {
            _inner = inner;
            _watcher = new FrameWatcher(seen);
            foreach (var header in inner.Headers)
                Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        /// <summary>
        /// The library may buffer the whole answer through this path (for example for a trailers-only answer), so every byte
        /// copied here is watched as well. The bytes written are exactly the bytes read.
        /// </summary>
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
            var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                _watcher.Feed(buffer.AsSpan(0, read));
                await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task<Stream> CreateContentReadStreamAsync() =>
            new WatchedStream(await _inner.ReadAsStreamAsync().ConfigureAwait(false), _watcher);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>A read-only stream that hands every byte it returns to the frame watcher.</summary>
    private sealed class WatchedStream(Stream inner, FrameWatcher watcher) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            watcher.Feed(buffer.AsSpan(offset, read));
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            watcher.Feed(buffer[..read]);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            watcher.Feed(buffer.Span[..read]);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Follows gRPC-Web frames (one flag byte, a big-endian 32-bit length and the payload) and records the grpc-status of a
    /// trailers frame. A trailers frame above <see cref="MaxTrailerBytes"/> is not read; a frame that never completes records
    /// nothing, so the answer is then malformed.
    /// </summary>
    private sealed class FrameWatcher(Observation seen)
    {
        /// <summary>The largest trailers frame read for its status (a cost control; real trailers are far smaller).</summary>
        public const int MaxTrailerBytes = 4096;

        private readonly byte[] _header = new byte[5];
        private int _headerLength;
        private bool _inPayload;
        private bool _isTrailer;
        private long _remaining;
        private byte[]? _trailer;
        private int _trailerLength;

        public void Feed(ReadOnlySpan<byte> bytes)
        {
            var index = 0;
            while (index < bytes.Length)
            {
                if (!_inPayload)
                {
                    var take = Math.Min(_header.Length - _headerLength, bytes.Length - index);
                    bytes.Slice(index, take).CopyTo(_header.AsSpan(_headerLength));
                    _headerLength += take;
                    index += take;
                    if (_headerLength < _header.Length)
                        return;
                    _headerLength = 0;
                    _isTrailer = (_header[0] & 0x80) != 0;
                    _remaining = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(1, 4));
                    _inPayload = true;
                    _trailer = _isTrailer && _remaining <= MaxTrailerBytes ? new byte[_remaining] : null;
                    _trailerLength = 0;
                    if (_remaining == 0)
                        FinishFrame();
                    continue;
                }

                var chunk = (int)Math.Min(_remaining, bytes.Length - index);
                if (_trailer is not null)
                {
                    bytes.Slice(index, chunk).CopyTo(_trailer.AsSpan(_trailerLength));
                    _trailerLength += chunk;
                }
                index += chunk;
                _remaining -= chunk;
                if (_remaining == 0)
                    FinishFrame();
            }
        }

        private void FinishFrame()
        {
            if (_trailer is not null)
                RecordStatus(Encoding.UTF8.GetString(_trailer, 0, _trailerLength));
            _inPayload = false;
            _trailer = null;
        }

        private void RecordStatus(string trailers)
        {
            foreach (var line in trailers.Split('\n'))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0 || !string.Equals(line[..colon].Trim(), "grpc-status", StringComparison.OrdinalIgnoreCase))
                    continue;
                seen.TrailerStatus = ParseStatus(line[(colon + 1)..]);
            }
        }
    }
}
