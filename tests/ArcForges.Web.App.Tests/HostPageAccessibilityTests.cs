// SPDX-License-Identifier: AGPL-3.0-only
// WCAG 2.2 AA checks of the App host page and stylesheet (WEB.40 accessibility re-proof, 2026-10-09): the unhandled-error
// banner's Dismiss is a named keyboard button (2.1.1, 4.1.2), no anchor without an href acts as a control, and the
// user-interface boundaries, the disabled-state fill and the focus indicators reach 3:1 against their adjacent colours
// (1.4.11). The host page and stylesheet are read from the source tree.
using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class HostPageAccessibilityTests
{
    private const string Paper = "#f4f3ed";
    private const string White = "#ffffff";
    private const string FieldFill = "#fffdf7";
    private const string BannerFill = "#fff3cd";
    private static readonly string Root = RepositoryRoot.Find();

    private static string AppSource(string relative) => File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.App", relative));

    [Fact]
    public void TheErrorBannerDismissIsANamedKeyboardButton()
    {
        var document = new HtmlParser().ParseDocument(AppSource("wwwroot/index.html"));
        var dismiss = Assert.Single(document.QuerySelectorAll("#blazor-error-ui .dismiss"));
        Assert.Equal("button", dismiss.LocalName);
        Assert.Equal("button", dismiss.GetAttribute("type"));
        Assert.Equal("Dismiss", dismiss.GetAttribute("aria-label"));
        Assert.False(dismiss.HasAttribute("tabindex"));
    }

    [Fact]
    public void TheErrorBannerMessageIsAnAlertAndNoTextSitsDirectlyInTheBanner()
    {
        // axe-core 4.13.0's region rule (WCAG 1.3.1 best practice) flags text and links outside every landmark, region, live region or
        // alert (an anchor is exempt only for a same-page fragment target). role="alert" makes its content a region, so the message
        // and the Reload link live inside that alert and the banner itself carries no text or link of its own.
        var document = new HtmlParser().ParseDocument(AppSource("wwwroot/index.html"));
        var banner = Assert.Single(document.QuerySelectorAll("#blazor-error-ui"));
        var alert = Assert.Single(banner.QuerySelectorAll("[role=\"alert\"]"));
        Assert.Equal("An unhandled error has occurred. Reload", Regex.Replace(alert.TextContent, @"\s+", " ").Trim());
        Assert.Single(alert.QuerySelectorAll("a.reload"));
        foreach (var text in banner.ChildNodes.OfType<IText>())
        {
            Assert.True(
                string.IsNullOrWhiteSpace(text.Data),
                $"Text \"{text.Data.Trim()}\" sits directly in #blazor-error-ui, outside the alert region.");
        }
        foreach (var link in banner.QuerySelectorAll("a"))
        {
            Assert.NotNull(link.Closest("[role=\"alert\"]"));
        }
    }

    [Fact]
    public void NoAnchorWithoutAnHrefIsLeftInTheHostPage()
    {
        var document = new HtmlParser().ParseDocument(AppSource("wwwroot/index.html"));
        Assert.Empty(document.QuerySelectorAll("a:not([href])"));
    }

    [Theory]
    [InlineData(".field input", "border", White, "text input boundary on the white field")]
    [InlineData(".field input", "border", Paper, "text input boundary on the page")]
    [InlineData(".input-row input", "border", FieldFill, "text input boundary on the field fill")]
    [InlineData(".input-row input", "border", Paper, "text input boundary on the page")]
    [InlineData(".button[aria-disabled=\"true\"]", "background", Paper, "disabled button fill on the page")]
    [InlineData(".button", "background", Paper, "button fill on the page")]
    public void ComponentBoundariesAndStateFillsReachThreeToOne(string selector, string property, string adjacent, string what)
    {
        var colour = DeclaredColour(AppSource("wwwroot/app.css"), selector, property);
        Assert.True(
            Contrast.Ratio(colour, adjacent) >= 3.0,
            $"{what}: {selector} {property} {colour} against {adjacent} is {Contrast.Ratio(colour, adjacent):0.00}:1");
    }

    [Theory]
    [InlineData(Paper, "focus indicator on the page")]
    [InlineData(BannerFill, "focus indicator on the error banner")]
    public void TheFocusIndicatorReachesThreeToOne(string adjacent, string what)
    {
        var css = AppSource("wwwroot/app.css");
        var match = Regex.Match(css, @":focus-visible\s*\{[^}]*outline:\s*\d+px solid (#[0-9a-fA-F]{6})", RegexOptions.Singleline);
        Assert.True(match.Success, "The :focus-visible outline is not declared.");
        var colour = match.Groups[1].Value;
        Assert.True(Contrast.Ratio(colour, adjacent) >= 3.0, $"{what}: {colour} against {adjacent} is {Contrast.Ratio(colour, adjacent):0.00}:1");
    }

    /// <summary>The first declaration of a property in the block of an exact selector, as a six-digit hex colour.</summary>
    private static string DeclaredColour(string css, string selector, string property)
    {
        // The selector may head a selector list ("a, a:hover { ... }"); the first block whose list starts with it is the one read.
        var block = Regex.Match(css, @"(?<![\w.#-])" + Regex.Escape(selector) + @"\s*(?:,[^{]*)?\{(?<body>[^}]*)\}");
        Assert.True(block.Success, $"The selector {selector} is not declared.");
        var declaration = Regex.Match(block.Groups["body"].Value, @"(?<![\w-])" + property + @":\s*(?:\d+px solid\s+)?(#[0-9a-fA-F]{6})\b");
        Assert.True(declaration.Success, $"{selector} does not declare a {property} colour.");
        return declaration.Groups[1].Value;
    }

    /// <summary>The WCAG 2.x contrast ratio of two six-digit hex colours.</summary>
    private static class Contrast
    {
        internal static double Ratio(string first, string second)
        {
            var a = Luminance(first);
            var b = Luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        private static double Luminance(string hex)
        {
            double Channel(int offset)
            {
                var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }
    }
}
