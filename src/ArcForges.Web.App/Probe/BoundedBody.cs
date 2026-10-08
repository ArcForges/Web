// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Net.Http;

namespace ArcForges.Web.App.Probe;

/// <summary>
/// Reads a response body without ever holding more than <c>limit</c> bytes (the TypeScript readBounded rule). A declared
/// length above the limit is refused without reading; a stream above it is refused on the first byte beyond the limit, and
/// the stream is disposed so the transfer is cut off.
/// </summary>
public static class BoundedBody
{
    private const int ChunkSize = 4096;

    /// <summary>Reads at most <paramref name="limit"/> bytes, or fails as malformed (above the limit) or unavailable/cancelled (transport).</summary>
    public static async Task<byte[]> ReadAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.TryGetValues("Content-Length", out var declaredValues))
        {
            var declared = declaredValues.ToArray();
            if (declared.Length != 1
                || !long.TryParse(declared[0], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length > limit)
                throw new ProbeFailureException(FailureKind.Malformed);
        }

        Stream stream;
        try
        {
            stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not ProbeFailureException)
        {
            throw FromTransport(exception, cancellationToken);
        }

        await using (stream)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[ChunkSize];
            long total = 0;
            while (true)
            {
                // Never request more than one byte beyond the limit, so the first refused byte is the limit plus one.
                var wanted = (int)Math.Min(chunk.Length, limit + 1L - total);
                int read;
                try
                {
                    read = await stream.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not ProbeFailureException)
                {
                    throw FromTransport(exception, cancellationToken);
                }

                if (read == 0)
                    break;
                total += read;
                if (total > limit)
                    throw new ProbeFailureException(FailureKind.Malformed);
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    /// <summary>The transport failure kind: the caller's own cancellation is cancelled, anything else is unavailable.</summary>
    internal static ProbeFailureException FromTransport(Exception cause, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? new ProbeFailureException(FailureKind.Cancelled, cause)
            : new ProbeFailureException(FailureKind.Unavailable, cause);
}
