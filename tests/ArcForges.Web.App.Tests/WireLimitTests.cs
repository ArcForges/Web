// SPDX-License-Identifier: AGPL-3.0-only
// The CON.92 decode and encode limits (PRF.11 U3, offline). The bounds are the registry values of the Contracts wire codec, and
// the boundary is checked on the real generated parser: the exact bound decodes, one byte over is refused as too large, and a
// truncated frame is malformed. Live cases (the deployed ingress) are local opt-in and stay in U7.
using ArcForges.Contracts.Foundation.Serialization;
using Google.Protobuf;
using Xunit;
using WireDecimal = ArcForges.Contracts.Foundation.V1.Decimal;

namespace ArcForges.Web.App.Tests;

public sealed class WireLimitTests
{
    /// <summary>A decimal message whose binary encoding is exactly <paramref name="size"/> bytes.</summary>
    private static WireDecimal MessageOfEncodedSize(int size)
    {
        // The encoding is one tag byte, a length varint and the text, so it grows with the text. The text length that gives
        // the size is found by counting down from the size, which is at most a few steps because the varint adds at most five.
        for (var length = size; length >= 0; length--)
        {
            var candidate = new WireDecimal { Value = new string('7', length) };
            if (candidate.CalculateSize() == size)
                return candidate;
        }
        throw new InvalidOperationException("No text length gives the requested encoded size.");
    }

    [Fact]
    public void TheRegistryByteBoundsAreTheReviewedCon92Values()
    {
        Assert.Equal(4 * 1024 * 1024, WireLimits.Bytes(WireLimit.UnaryMessage));
        Assert.Equal(4 * 1024 * 1024, WireLimits.Bytes(WireLimit.HelperMessage));
        Assert.Equal(256 * 1024, WireLimits.Bytes(WireLimit.InlinePage));
        Assert.Equal(32 * 1024, WireLimits.Bytes(WireLimit.StreamFrame));
        Assert.Equal(64 * 1024 * 1024, WireLimits.Bytes(WireLimit.LargeProjection));
        Assert.Equal(100, WireLimits.NestedMessageLevels);
    }

    [Fact]
    public void ADecodeAtTheStreamFrameBoundIsAcceptedAndOneByteOverIsTooLarge()
    {
        var bound = WireLimits.Bytes(WireLimit.StreamFrame);
        var atBound = MessageOfEncodedSize(bound).ToByteArray();
        Assert.Equal(bound, atBound.Length);
        Assert.True(ContractWire.TryDecode(WireDecimal.Parser, atBound, WireLimit.StreamFrame, out var decoded, out _));
        Assert.NotNull(decoded);
        Assert.Equal(MessageOfEncodedSize(bound).Value, decoded.Value);

        var overBound = MessageOfEncodedSize(bound + 1).ToByteArray();
        Assert.Equal(bound + 1, overBound.Length);
        Assert.False(ContractWire.TryDecode(WireDecimal.Parser, overBound, WireLimit.StreamFrame, out _, out var refused));
        Assert.Equal(ContractSerializationFailure.TooLarge, refused);
    }

    [Fact]
    public void TheSameBytesAreAdmittedAsAUnaryMessageAndRefusedAsAStreamFrame()
    {
        var bytes = MessageOfEncodedSize(WireLimits.Bytes(WireLimit.StreamFrame) + 1).ToByteArray();

        Assert.True(ContractWire.TryDecode(WireDecimal.Parser, bytes, WireLimit.UnaryMessage, out _, out _));
        Assert.False(ContractWire.TryDecode(WireDecimal.Parser, bytes, WireLimit.StreamFrame, out _, out var failure));
        Assert.Equal(ContractSerializationFailure.TooLarge, failure);
    }

    [Fact]
    public void AUnaryBoundMessageDecodesAndOneByteOverIsRefused()
    {
        var bound = WireLimits.Bytes(WireLimit.UnaryMessage);
        Assert.True(ContractWire.TryDecode(WireDecimal.Parser, MessageOfEncodedSize(bound).ToByteArray(), WireLimit.UnaryMessage, out _, out _));
        Assert.False(ContractWire.TryDecode(WireDecimal.Parser, MessageOfEncodedSize(bound + 1).ToByteArray(), WireLimit.UnaryMessage, out _, out var failure));
        Assert.Equal(ContractSerializationFailure.TooLarge, failure);
    }

    [Fact]
    public void AMalformedOrTruncatedFrameIsMalformedNeverAMessage()
    {
        // Field 1, length-delimited, declares five bytes and carries one.
        byte[] truncated = [0x0A, 0x05, 0x61];
        Assert.False(ContractWire.TryDecode(WireDecimal.Parser, truncated, WireLimit.StreamFrame, out _, out var truncatedFailure));
        Assert.Equal(ContractSerializationFailure.Malformed, truncatedFailure);

        // A tag with no length, and a reserved wire type (7), are both malformed.
        byte[] bareTag = [0x0A];
        Assert.False(ContractWire.TryDecode(WireDecimal.Parser, bareTag, WireLimit.StreamFrame, out _, out var bareFailure));
        Assert.Equal(ContractSerializationFailure.Malformed, bareFailure);

        byte[] reservedWireType = [0x0F, 0x00];
        Assert.False(ContractWire.TryDecode(WireDecimal.Parser, reservedWireType, WireLimit.StreamFrame, out _, out var reservedFailure));
        Assert.Equal(ContractSerializationFailure.Malformed, reservedFailure);
    }

    [Fact]
    public void EncodingAcceptsTheStreamFrameBoundAndRefusesOneByteOver()
    {
        var bound = WireLimits.Bytes(WireLimit.StreamFrame);
        var encoded = ContractWire.Encode(MessageOfEncodedSize(bound), WireLimit.StreamFrame);
        Assert.Equal(bound, encoded.Length);

        var refusal = Assert.Throws<ContractSerializationException>(() =>
            ContractWire.Encode(MessageOfEncodedSize(bound + 1), WireLimit.StreamFrame));
        Assert.Equal(ContractSerializationFailure.TooLarge, refusal.Failure);
    }
}
