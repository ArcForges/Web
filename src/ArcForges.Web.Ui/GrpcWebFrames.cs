// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Ui;

/// <summary>
/// Length-prefixed gRPC-Web framing for one unary call: a data frame (flag 0) and a trailers frame (flag 0x80),
/// the binary <c>application/grpc-web+proto</c> form the hello example uses. Compressed frames are refused.
/// </summary>
public static class GrpcWebFrames
{
    /// <summary>The media type of the binary gRPC-Web form.</summary>
    public const string MediaType = "application/grpc-web+proto";

    /// <summary>The flag of an uncompressed protobuf data frame.</summary>
    public const byte DataFlag = 0x00;

    /// <summary>The flag of the trailers frame.</summary>
    public const byte TrailersFlag = 0x80;

    /// <summary>Encodes one frame: a flag byte, a big-endian 32-bit payload length and the payload.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> payload, byte flag)
    {
        var frame = new byte[payload.Length + 5];
        frame[0] = flag;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), checked((uint)payload.Length));
        payload.CopyTo(frame.AsSpan(5));
        return frame;
    }

    /// <summary>
    /// Returns the single protobuf message of a unary reply and checks the trailers. The reply must be exactly one
    /// uncompressed data frame followed by a trailers frame whose <c>grpc-status</c> is 0.
    /// </summary>
    /// <exception cref="ServerConnectionException">The body is truncated, compressed, out of shape or not an OK status.</exception>
    public static byte[] DecodeUnaryReply(ReadOnlySpan<byte> body)
    {
        var offset = 0;
        var message = ReadFrame(body, ref offset);
        if (message.Flag != DataFlag)
            throw new ServerConnectionException("The reply frame is not an uncompressed protobuf message.");
        var trailers = ReadFrame(body, ref offset);
        if (trailers.Flag != TrailersFlag)
            throw new ServerConnectionException("The reply has no trailers frame.");
        if (offset != body.Length)
            throw new ServerConnectionException("The reply has bytes after the trailers.");
        if (GrpcStatus(body.Slice(trailers.Start, trailers.Length)) != "0")
            throw new ServerConnectionException("The server reported a non-OK status.");
        return body.Slice(message.Start, message.Length).ToArray();
    }

    private readonly record struct Frame(byte Flag, int Start, int Length);

    private static Frame ReadFrame(ReadOnlySpan<byte> body, ref int offset)
    {
        if (body.Length - offset < 5)
            throw new ServerConnectionException("The reply frame header is truncated.");
        var flag = body[offset];
        var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body.Slice(offset + 1, 4));
        offset += 5;
        if (length > body.Length - offset)
            throw new ServerConnectionException("The reply frame length is beyond the body.");
        var frame = new Frame(flag, offset, (int)length);
        offset += (int)length;
        return frame;
    }

    private static string? GrpcStatus(ReadOnlySpan<byte> trailers)
    {
        var text = System.Text.Encoding.UTF8.GetString(trailers);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
                continue;
            if (string.Equals(line[..separator].Trim(), "grpc-status", StringComparison.OrdinalIgnoreCase))
                return line[(separator + 1)..].Trim();
        }
        return null;
    }
}
