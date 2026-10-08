// SPDX-License-Identifier: AGPL-3.0-only
// Port of tests/unit/hello.test.tsx (the greeting part: the published protobuf round trip).
using ArcForges.Web.Ui;
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class GreetingTests
{
    public static TheoryData<string> InvalidNames =>
    [
        " ",
        "",
        new string('x', 81),
        "hi" + (char)0,
        "tab" + '\t',
        "del" + (char)127,
        string.Concat(Enumerable.Repeat("🌍", 81)),
    ];

    [Fact]
    public void PublishedProtobufRoundTripPreservesUnicodeNames()
    {
        Assert.Equal("Hello, 世界 🌍!", HelloGreeting.Greet("  世界 🌍  "));
        var longest = string.Concat(Enumerable.Repeat("🌍", 80));
        Assert.Equal("Hello, " + longest + "!", HelloGreeting.Greet(longest));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void InvalidNamesAreRefusedWithTheExactFailureText(string invalid)
    {
        var error = Assert.Throws<InvalidNameException>(() => HelloGreeting.Greet(invalid));
        Assert.Equal("Enter a name between 1 and 80 characters, without control characters.", error.Message);
    }

    [Fact]
    public void TheInitialGreetingIsTheReactGreetingForTheDefaultName()
    {
        Assert.Equal("World", HelloGreeting.DefaultName);
        Assert.Equal("Hello, World!", HelloGreeting.Greet(HelloGreeting.DefaultName));
    }

    [Fact]
    public void UnpairedSurrogateIsEncodedAsTheReplacementCharacterLikeTheBrowserEncoder()
    {
        var lone = "a" + (char)0xD800 + "b";
        Assert.Equal("Hello, a�b!", HelloGreeting.Greet(lone));
    }
}
