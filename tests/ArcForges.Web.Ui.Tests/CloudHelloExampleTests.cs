// SPDX-License-Identifier: AGPL-3.0-only
// The cloud-hello control states: the fixed greeting on success and the exact failure text on any error.
using Bunit;
using Xunit;

namespace ArcForges.Web.Ui.Tests;

public sealed class CloudHelloExampleTests
{
    private const string FailureText = "The server is unavailable or returned an unexpected response. Try again later.";

    [Fact]
    public void TheCheckShowsTheServerGreetingOnSuccess()
    {
        using var context = new BunitContext();
        var cut = context.Render<CloudHelloExample>(parameters => parameters.Add(component => component.CheckConnection, _ => Task.FromResult("Hello, ArcForges!")));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("Hello, ArcForges!", cut.Find("[role=status] p").TextContent));
    }

    [Fact]
    public void AnyFailureShowsTheExactFailureTextAsAnAlert()
    {
        using var context = new BunitContext();
        var cut = context.Render<CloudHelloExample>(parameters => parameters.Add(component => component.CheckConnection, _ => Task.FromException<string>(new ServerConnectionException("refused"))));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal(FailureText, cut.Find("[role=alert] p").TextContent));
    }

    [Fact]
    public void TheControlDoesNothingWhenNoCheckIsSupplied()
    {
        using var context = new BunitContext();

        var cut = context.Render<CloudHelloExample>();

        Assert.Equal("No request sent yet.", cut.Find("[role=status] p").TextContent);
        Assert.Equal("Check connection", cut.Find("button").TextContent.Trim());
        cut.Find("button").Click();
        Assert.Equal("No request sent yet.", cut.Find("[role=status] p").TextContent);
    }
}
