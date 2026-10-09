// SPDX-License-Identifier: AGPL-3.0-only
// Test-only fixtures for the Account and Chat probes. They are wire documents and frames, byte for byte what the same-origin
// server sends; they are produced with the generated Contracts types and never part of a release graph.
using System.Text;
using System.Text.Json.Nodes;
using ArcForges.Contracts.Hello.V1;
using Google.Protobuf;

namespace ArcForges.Web.App.Tests.Fixtures;

/// <summary>The fixture documents and frames of the Account and Chat tests.</summary>
public static class ProbeFixtures
{
    /// <summary>The origin the fixtures are served from (the TypeScript fixture origin).</summary>
    public static readonly Uri Origin = new("https://account.example.test/");

    /// <summary>The CSRF token of the authenticated bootstrap.</summary>
    public const string AuthenticatedCsrf = "cccccccccccccccccccccccc";

    /// <summary>The CSRF token of the anonymous bootstrap.</summary>
    public const string AnonymousCsrf = "aaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>The authenticated bootstrap document (the fixture of the TypeScript suite, in wire form).</summary>
    public const string AuthenticatedJson =
        "{\"csrfToken\":\"" + AuthenticatedCsrf + "\",\"authenticated\":true,"
        + "\"session\":{\"sessionId\":\"6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11\",\"expiresAt\":\"2026-10-03T08:00:00.000000Z\","
        + "\"idleExpiresAt\":\"2026-10-02T20:30:00.000000Z\",\"userId\":\"0f0e6a30-5d1c-4c7e-8b53-5b6b7f9a4a10\","
        + "\"deviceId\":\"8f2a1d77-1c1b-4b0a-9a55-2f1d0e5c7b21\",\"workspaceIds\":[\"3f1d3b1e-2a8c-4c44-a1c8-77a1e8f0b9a1\","
        + "\"a2b84f55-0c2d-4b0d-8e6a-5d6e44a6c7d3\"],\"recoveryGeneration\":\"18446744073709551615\",\"purpose\":\"authenticate\"},"
        + "\"profile\":{\"displayName\":\"Ada Lovelace\",\"locale\":\"en-GB\",\"timezone\":\"Europe/London\","
        + "\"revision\":\"9007199254740993\"}}";

    /// <summary>The anonymous bootstrap document.</summary>
    public const string AnonymousJson = "{\"csrfToken\":\"" + AnonymousCsrf + "\",\"authenticated\":false}";

    /// <summary>A logout receipt with the given effect.</summary>
    public static byte[] ReceiptBody(string effect = "happened") =>
        Encoding.UTF8.GetBytes(
            "{\"commandId\":\"7c9e6679-7425-40de-944b-e07fc1f90ae7\",\"effect\":\"" + effect + "\"}");

    /// <summary>The authenticated bootstrap as a JSON object that a test can change field by field.</summary>
    public static JsonObject AuthenticatedNode() => JsonNode.Parse(AuthenticatedJson)!.AsObject();

    /// <summary>The anonymous bootstrap as a JSON object that a test can change field by field.</summary>
    public static JsonObject AnonymousNode() => JsonNode.Parse(AnonymousJson)!.AsObject();

    /// <summary>The UTF-8 bytes of a JSON object.</summary>
    public static byte[] Bytes(JsonObject node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    /// <summary>One gRPC-Web frame: a flag byte, a big-endian 32-bit length and the payload.</summary>
    public static byte[] Frame(ReadOnlySpan<byte> payload, byte flag)
    {
        var frame = new byte[payload.Length + 5];
        frame[0] = flag;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), checked((uint)payload.Length));
        payload.CopyTo(frame.AsSpan(5));
        return frame;
    }

    /// <summary>A trailers frame (flag 0x80) carrying the given text.</summary>
    public static byte[] Trailers(string text) => Frame(Encoding.UTF8.GetBytes(text), 0x80);

    /// <summary>A binary gRPC-Web hello answer: the SayHelloResponse data frame and an OK trailer.</summary>
    public static byte[] HelloReply(string message = "Hello, ArcForges!")
    {
        var payload = new SayHelloResponse { Message = message }.ToByteArray();
        return Concat(Frame(payload, 0), Trailers("grpc-status: 0\r\n"));
    }

    /// <summary>The bytes of the given parts, in order.</summary>
    public static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
            total += part.Length;
        var bytes = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(bytes, offset);
            offset += part.Length;
        }
        return bytes;
    }
}
