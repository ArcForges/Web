// SPDX-License-Identifier: AGPL-3.0-only
// Port of the 64-bit case of tests/unit/app-session.test.ts: canonical decimal text in, the same text out, and no value passes
// through a floating-point number.
using ArcForges.Contracts.Foundation.Values;
using ArcForges.Web.App.Probe;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class ExactUnsignedTests
{
    [Fact]
    public void SixtyFourBitValuesStayExactCanonicalTextInTheSameTextOut()
    {
        foreach (var value in new[]
                 {
                     "0",
                     "9007199254740991",
                     "9007199254740993",
                     "9223372036854775807",
                     "18446744073709551615",
                 })
        {
            Assert.True(Exact.TryUnsigned(value, out var exact), value);
            Assert.Equal(value, exact.Text);
            Assert.Equal(ulong.Parse(value, System.Globalization.CultureInfo.InvariantCulture), exact.Value);
        }
    }

    [Fact]
    public void AFloatingPointNumberCannotHoldTheseValuesApartButTheExactPathCan()
    {
        // A double cannot tell 9007199254740993 from 9007199254740992; the exact path can.
        Assert.Equal((double)9007199254740993UL, (double)9007199254740992UL);
        Assert.True(Exact.TryUnsigned("9007199254740993", out var odd));
        Assert.True(Exact.TryUnsigned("9007199254740992", out var even));
        Assert.NotEqual(odd.Value, even.Value);
    }

    [Fact]
    public void AnythingThatIsNotCanonicalUint64TextIsRefused()
    {
        foreach (var value in new[]
                 {
                     string.Empty,
                     "01",
                     "-1",
                     "+1",
                     "1.0",
                     "1e3",
                     " 1",
                     "1 ",
                     "0x10",
                     "18446744073709551616",
                 })
            Assert.False(Exact.TryUnsigned(value, out _), value);
        Assert.False(Exact.TryUnsigned(null, out _));
    }

    [Fact]
    public void SignedSixtyFourBitValuesAtTheirEdgesAndAboveTheDoubleRangeStayExact()
    {
        // The generated Contracts parser (ExactInteger.ParseInt64) holds int64 values exactly, at both edges and above 2^53.
        Assert.Equal(long.MinValue, ExactInteger.ParseInt64("-9223372036854775808"));
        Assert.Equal(long.MaxValue, ExactInteger.ParseInt64("9223372036854775807"));
        Assert.Equal(9007199254740993L, ExactInteger.ParseInt64("9007199254740993"));
        Assert.NotEqual(ExactInteger.ParseInt64("9007199254740993"), ExactInteger.ParseInt64("9007199254740992"));
    }

    [Fact]
    public void NonCanonicalOrOutOfRangeSignedTextIsRefusedNotRounded()
    {
        // The parser refuses with its typed FormatException, and no other exception type passes.
        foreach (var value in new[] { "-0", "01", "+1", "1e3", " 1", "1 ", "0x10", "9223372036854775808", "-9223372036854775809" })
            Assert.Throws<FormatException>(() => ExactInteger.ParseInt64(value));
    }

    [Fact]
    public void DecimalTextKeepsItsCoefficientAndDeclaredScaleExactly()
    {
        // Shared exact decimal (Foundation): canonical wire text, declared scale preserved, no binary floating point.
        // The shared bound admits at most nine fractional digits and twenty-eight significant digits.
        foreach (var value in new[] { "0", "-12.5", "12345678901234567890.1200", "0.000000001" })
        {
            var exact = new ExactDecimal(value);
            Assert.Equal(value, exact.Value);
            Assert.Equal(value, new ExactDecimal(value).ToWire().Value);
        }
        var trailing = new ExactDecimal("12345678901234567890.1200");
        Assert.Equal(4, trailing.Scale);
        Assert.Equal(System.Numerics.BigInteger.Parse("123456789012345678901200", System.Globalization.CultureInfo.InvariantCulture), trailing.Coefficient);
    }

    [Fact]
    public void NonCanonicalDecimalTextIsRefused()
    {
        // The constructor refuses syntax with its typed FormatException, and no other exception type passes.
        foreach (var value in new[] { "1e3", "+1", " 1", "NaN", "Infinity" })
            Assert.Throws<FormatException>(() => new ExactDecimal(value));
    }

    [Fact]
    public void DecimalsAtTheSharedBoundAreAcceptedAndRoundTrip()
    {
        // The shared bound admits at most nine fractional digits and twenty-eight significant digits.
        foreach (var value in new[]
                 {
                     "1234567890123456789.123456789",
                     "-1234567890123456789.123456789",
                     "0.123456789",
                     "1234567890123456789012345678",
                 })
        {
            var exact = new ExactDecimal(value);
            Assert.Equal(value, exact.Value);
            Assert.Equal(value, exact.ToWire().Value);
        }
        Assert.Equal(9, new ExactDecimal("1234567890123456789.123456789").Scale);
    }

    [Fact]
    public void DecimalsOverTheSharedBoundAreRefusedNotRounded()
    {
        // Ten fractional digits, and 29 significant digits (an integer, and a fraction after 28 integer digits), are over the bound.
        foreach (var value in new[]
                 {
                     "0.1234567890",
                     "1.0000000000",
                     "12345678901234567890123456789",
                     "1234567890123456789012345678.9",
                 })
            Assert.Throws<FormatException>(() => new ExactDecimal(value));
    }
}
