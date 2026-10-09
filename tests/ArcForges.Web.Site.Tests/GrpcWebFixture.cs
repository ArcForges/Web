// SPDX-License-Identifier: AGPL-3.0-only
// Test-only gRPC-Web fixture (the port of tests/fixtures/grpc-web.ts). It encodes and decodes the frames
// independently of the product framing code, so the tests do not check the framing with itself.
using System.Buffers.Binary;
using System.Text;
using ArcForges.Contracts.Hello.V1;
using Google.Protobuf;

namespace ArcForges.Web.Site.Tests;

internal static class GrpcWebFixture
{
    public const byte DataFlag = 0;
    public const byte TrailersFlag = 0x80;

    public static byte[] Frame(byte[] payload, byte flag)
    {
        var bytes = new byte[payload.Length + 5];
        bytes[0] = flag;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(bytes, 5);
        return bytes;
    }

    /// <summary>Returns the name of one uncompressed SayHello request frame, and fails for any other shape.</summary>
    public static string DecodeHelloRequestName(byte[] bytes)
    {
        if (bytes.Length < 5 || bytes[0] != DataFlag || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4)) != bytes.Length - 5)
            throw new InvalidOperationException("Expected one uncompressed protobuf request frame");
        return SayHelloRequest.Parser.ParseFrom(bytes.AsSpan(5).ToArray()).Name;
    }

    /// <summary>A unary OK reply: one data frame with the message, then the trailers frame with grpc-status 0.</summary>
    public static byte[] HelloResponse(string message = "Hello, ArcForges!")
    {
        var data = Frame(new SayHelloResponse { Message = message }.ToByteArray(), DataFlag);
        var trailers = Frame(Encoding.UTF8.GetBytes("grpc-status: 0\r\n"), TrailersFlag);
        return [.. data, .. trailers];
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var bytes = new List<byte>();
        foreach (var part in parts)
            bytes.AddRange(part);
        return bytes.ToArray();
    }
}
