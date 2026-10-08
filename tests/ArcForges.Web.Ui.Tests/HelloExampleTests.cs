// SPDX-License-Identifier: AGPL-3.0-only
// Port of tests/unit/hello.test.tsx (the form part): validation, recovery and untrusted names rendered as text.
using Bunit;
using Xunit;

namespace ArcForges.Web.Ui.Tests;

public sealed class HelloExampleTests
{
    [Fact]
    public void FormValidatesRecoversAndRendersUntrustedNamesAsText()
    {
        using var context = new BunitContext();
        var cut = context.Render<HelloExample>();
        var input = cut.Find("#hello-name");

        input.Input(string.Empty);
        cut.Find("form").Submit();
        Assert.Equal("Enter a name between 1 and 80 characters, without control characters.", cut.Find("[role=alert]").TextContent);

        input.Input("<img src=x onerror=alert(1)>");
        cut.Find("form").Submit();
        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.Equal("Hello, <img src=x onerror=alert(1)>!", cut.Find("[role=status] p").TextContent);
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void TheControlsAreEnabledAfterTheFirstInteractiveRenderAndTheDefaultGreetingIsShown()
    {
        using var context = new BunitContext();

        var cut = context.Render<HelloExample>();

        Assert.Null(cut.Find("#hello-name").GetAttribute("disabled"));
        Assert.Equal("Hello, World!", cut.Find("[role=status] p").TextContent);
        Assert.Equal("false", cut.Find("#hello-name").GetAttribute("aria-invalid"));
        Assert.Equal("name-hint", cut.Find("#hello-name").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void AnInvalidNameMarksTheFieldAndDescribesTheError()
    {
        using var context = new BunitContext();
        var cut = context.Render<HelloExample>();

        cut.Find("#hello-name").Input("   ");
        cut.Find("form").Submit();

        Assert.Equal("true", cut.Find("#hello-name").GetAttribute("aria-invalid"));
        Assert.Equal("name-error name-hint", cut.Find("#hello-name").GetAttribute("aria-describedby"));
    }
}
