// SPDX-License-Identifier: AGPL-3.0-only
// Offline server-stream framing fixtures (PRF.11 U4). Both candidate framings are tested as fixtures only: the binary
// application/grpc-web+proto stream (the first choice, requirement 12 line 418) and the grpc-web-text variant
// (application/grpc-web-text, the base64 encoding of the same frames). The decoders here are test-only and check the
// framing, the CON.92 stream frame bound and the malformed cases. They make no claim about the deployed ingress, which is
// observed only in the local opt-in run (U7 and LS2).
using System.Buffers.Binary;
using System.Text;
using ArcForges.Contracts.Foundation.Serialization;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using Google.Protobuf;
using Xunit;
using WireDecimal = ArcForges.Contracts.Foundation.V1.Decimal;

namespace ArcForges.Web.App.Tests;

public sealed class StreamFramingTests
{
    /// <summary>One decoded frame: its flag byte and its payload.</summary>
    private sealed record Frame(byte Flag, byte[] Payload);

    /// <summary>
    /// The test-only binary decoder: a flag byte, a big-endian 32-bit length and the payload, repeated. A frame that declares
    /// more bytes than remain is malformed, and a data frame above the stream frame bound is too large.
    /// </summary>
    private static List<Frame> DecodeBinary(ReadOnlySpan<byte> body)
    {
        var frames = new List<Frame>();
        var offset = 0;
        while (offset < body.Length)
        {
            if (body.Length - offset < 5)
                throw new ProbeFailureException(FailureKind.Malformed);
            var flag = body[offset];
            var length = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(offset + 1, 4));
            offset += 5;
            if (length > body.Length - offset)
                throw new ProbeFailureException(FailureKind.Malformed);
            var isTrailer = (flag & 0x80) != 0;
            if (!isTrailer && length > WireLimits.Bytes(WireLimit.StreamFrame))
                throw new ProbeFailureException(FailureKind.Malformed);
            frames.Add(new Frame(flag, body.Slice(offset, (int)length).ToArray()));
            offset += (int)length;
        }
        return frames;
    }

    /// <summary>The test-only grpc-web-text decoder: the whole body is base64, decoded before the frames are read.</summary>
    private static byte[] DecodeText(string body)
    {
        // A complete grpc-web-text body is a whole number of four-character base64 groups.
        if (body.Length % 4 != 0)
            throw new ProbeFailureException(FailureKind.Malformed);
        try
        {
            return Convert.FromBase64String(body);
        }
        catch (FormatException exception)
        {
            throw new ProbeFailureException(FailureKind.Malformed, exception);
        }
    }

    /// <summary>A server stream of three data messages and an OK trailers frame, as binary frames.</summary>
    private static byte[] ThreeMessageStream()
    {
        var parts = new List<byte[]>();
        foreach (var text in new[] { "9007199254740993", "18446744073709551615", "-9223372036854775808" })
            parts.Add(ProbeFixtures.Frame(new WireDecimal { Value = text }.ToByteArray(), 0));
        parts.Add(ProbeFixtures.Trailers("grpc-status: 0\r\n"));
        return ProbeFixtures.Concat([.. parts]);
    }

    [Fact]
    public void TheBinaryStreamDecodesToItsMessagesInOrderAndEndsWithItsStatus()
    {
        var frames = DecodeBinary(ThreeMessageStream());

        Assert.Equal(4, frames.Count);
        Assert.Equal(new[] { "9007199254740993", "18446744073709551615", "-9223372036854775808" },
            frames.Take(3).Select(frame => WireDecimal.Parser.ParseFrom(frame.Payload).Value).ToArray());
        Assert.True((frames[3].Flag & 0x80) != 0);
        Assert.Equal("grpc-status: 0\r\n", Encoding.UTF8.GetString(frames[3].Payload));
    }

    [Fact]
    public void TheGrpcWebTextStreamCarriesTheSameFramesAsTheBinaryStream()
    {
        var binary = ThreeMessageStream();
        var text = Convert.ToBase64String(binary);

        var decoded = DecodeText(text);

        Assert.Equal(binary, decoded);
        Assert.Equal(
            DecodeBinary(binary).Select(frame => Convert.ToBase64String(frame.Payload)),
            DecodeBinary(decoded).Select(frame => Convert.ToBase64String(frame.Payload)));
    }

    [Fact]
    public void AStreamFrameAtTheBoundIsAcceptedAndOneByteOverIsMalformedNeverAPartialMessage()
    {
        var bound = (int)WireLimits.Bytes(WireLimit.StreamFrame);
        // A data frame of exactly the stream bound is accepted; the payload is an opaque byte run for the framing check.
        var atBound = ProbeFixtures.Frame(new byte[bound], 0);
        Assert.Single(DecodeBinary(atBound));

        var overBound = ProbeFixtures.Frame(new byte[bound + 1], 0);
        Assert.Throws<ProbeFailureException>(() => DecodeBinary(overBound));
    }

    [Fact]
    public void ATruncatedBinaryFrameIsMalformedAndNeverAMessage()
    {
        // The header declares five payload bytes; only one remains.
        byte[] truncated = [0x00, 0x00, 0x00, 0x00, 0x05, 0x61];
        Assert.Throws<ProbeFailureException>(() => DecodeBinary(truncated));

        // A header cut short is malformed too.
        byte[] shortHeader = [0x00, 0x00, 0x00];
        Assert.Throws<ProbeFailureException>(() => DecodeBinary(shortHeader));
    }

    [Fact]
    public void AMalformedGrpcWebTextBodyIsMalformedAndNeverAMessage()
    {
        // Not a whole number of four-character groups.
        Assert.Throws<ProbeFailureException>(() => DecodeText("AAAAA"));
        // A character outside the base64 alphabet.
        Assert.Throws<ProbeFailureException>(() => DecodeText("AA!A"));
        // A body that is valid base64 but whose frames are truncated is malformed at the framing layer.
        var truncated = Convert.ToBase64String(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x09, 0x61 });
        Assert.Throws<ProbeFailureException>(() => DecodeBinary(DecodeText(truncated)));
    }

    [Fact]
    public void EachStreamedMessageDecodesAsAStreamFrameMessageWithinTheBound()
    {
        // The payloads are the generated messages, checked against the stream frame class of CON.92.
        foreach (var frame in DecodeBinary(ThreeMessageStream()).Where(frame => (frame.Flag & 0x80) == 0))
        {
            Assert.True(ContractWire.TryDecode(WireDecimal.Parser, frame.Payload, WireLimit.StreamFrame, out var message, out _));
            Assert.NotNull(message);
        }
    }
}
