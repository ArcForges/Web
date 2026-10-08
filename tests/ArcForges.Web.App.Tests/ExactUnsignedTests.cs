// SPDX-License-Identifier: AGPL-3.0-only
// Port of the 64-bit case of tests/unit/app-session.test.ts: canonical decimal text in, the same text out, and no value passes
// through a floating-point number.
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
}
