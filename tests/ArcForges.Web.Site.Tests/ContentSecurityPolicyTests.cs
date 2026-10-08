// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using ArcForges.Web.Site;
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void PagesWithoutScriptsGetTheSelfOnlyScriptSource()
    {
        var policy = SiteContentSecurityPolicy.FromPages(["<!DOCTYPE html><html><body>plain</body></html>"]);

        Assert.Equal(SiteOutputTests.ExpectedPolicy, policy);
    }

    [Fact]
    public void AnInlineScriptBodyIsAddedAsItsSha256Hash()
    {
        const string body = "console.log(1);";
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        var policy = SiteContentSecurityPolicy.FromPages([$"<html><script>{body}</script></html>"]);

        Assert.Contains($"script-src 'self' 'sha256-{hash}';", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void HashesAreSortedAndDeduplicatedAcrossPages()
    {
        var one = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("a()")));
        var two = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("b()")));

        var policy = SiteContentSecurityPolicy.FromPages(["<script>b()</script>", "<script>a()</script><SCRIPT>a()</SCRIPT>"]);

        var sorted = new[] { $"'sha256-{one}'", $"'sha256-{two}'" }.OrderBy(token => token, StringComparer.Ordinal);
        Assert.Contains("script-src 'self' " + string.Join(' ', sorted) + ";", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyInlineScriptIsNotHashed()
    {
        var policy = SiteContentSecurityPolicy.FromPages(["<script></script>"]);

        Assert.Equal(SiteOutputTests.ExpectedPolicy, policy);
    }

    [Theory]
    [InlineData("<script src=\"/app.js\"></script>")]
    [InlineData("<script type=\"module\" src=\"/app.js\"></script>")]
    [InlineData("<SCRIPT SRC=\"https://cdn.example/app.js\"></SCRIPT>")]
    [InlineData("<script data-src=\"/app.js\">x()</script>")]
    public void AnExternalScriptIsRefused(string html)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SiteContentSecurityPolicy.FromPages([html]));
        Assert.Contains("external script", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<script>x()")]
    [InlineData("<script")]
    public void AnUnterminatedScriptElementIsRefused(string html)
    {
        Assert.Throws<InvalidOperationException>(() => SiteContentSecurityPolicy.FromPages([html]));
    }

    [Fact]
    public void ThePolicyNeverCarriesUnsafeOrWebAssemblySourcesAndFitsTheHeaderBudget()
    {
        var policy = SiteContentSecurityPolicy.FromPages(["<script>x()</script>"]);

        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("wasm-unsafe-eval", policy, StringComparison.Ordinal);
        Assert.True(policy.Length < SiteContentSecurityPolicy.HeaderLineBudget);
    }

    [Fact]
    public void APolicyThatExceedsTheHeaderBudgetIsRefusedByTheHeaderRenderer()
    {
        var oversized = "default-src 'self'; " + new string('x', SiteContentSecurityPolicy.HeaderLineBudget);

        Assert.Throws<ArgumentException>(() => SiteSecurityHeaders.Render(oversized));
    }
}
