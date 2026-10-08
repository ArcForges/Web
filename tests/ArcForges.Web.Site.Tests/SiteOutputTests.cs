// SPDX-License-Identifier: AGPL-3.0-only
// Page inventory, determinism, no-script readability and the exact headers and policy of the static public Site.
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ArcForges.Web.Site;
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class SiteOutputTests
{
    /// <summary>The exact public Site policy: no WebAssembly, no unsafe-inline, no unsafe-eval and no external origin.</summary>
    internal const string ExpectedPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    private const string ExpectedHeaders =
        "/*\n" +
        "  Content-Security-Policy: " + ExpectedPolicy + "\n" +
        "  X-Content-Type-Options: nosniff\n" +
        "  Referrer-Policy: no-referrer\n" +
        "  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()\n" +
        "  X-Frame-Options: DENY\n" +
        "  X-Robots-Tag: noindex, nofollow\n" +
        "  Cache-Control: public, no-cache, no-transform\n" +
        "/assets/*\n" +
        "  ! Cache-Control\n" +
        "  Cache-Control: public, max-age=31536000, immutable, no-transform\n" +
        "/__build.json\n" +
        "  ! Cache-Control\n" +
        "  Cache-Control: no-store, no-transform\n" +
        "/__build-info.json\n" +
        "  ! Cache-Control\n" +
        "  Cache-Control: no-store, no-transform\n";

    private static readonly string[] HtmlPaths = ["index.html", "hello/index.html", "cloud-hello/index.html"];

    private static readonly Regex StylesheetPath = new(@"^assets/site\.[0-9a-f]{16}\.css$", RegexOptions.CultureInvariant);

    private static readonly Regex EventHandlerAttribute = new(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public async Task TheOutputIsExactlyThePublicPageSetAndItsHeadersAndAssets()
    {
        var site = await SiteBuilder.BuildAsync(new SiteOptions(), TestContext.Current.CancellationToken);

        var paths = site.Files.Select(file => file.Path).ToArray();
        var stylesheet = Assert.Single(paths, path => StylesheetPath.IsMatch(path));
        Assert.Equal(
            new[] { "404.css", "404.html", "_headers", "cloud-hello/index.html", "favicon.svg", "hello/index.html", "index.html", "robots.txt", stylesheet }.OrderBy(path => path, StringComparer.Ordinal),
            paths);
        Assert.Equal(paths.OrderBy(path => path, StringComparer.Ordinal), paths);
    }

    [Fact]
    public async Task TwoBuildsOfTheSameInputsProduceTheSameBytes()
    {
        var first = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = new string('a', 40) }, TestContext.Current.CancellationToken);
        var second = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = new string('a', 40) }, TestContext.Current.CancellationToken);

        Assert.Equal(first.Files.Select(file => file.Path), second.Files.Select(file => file.Path));
        foreach (var (left, right) in first.Files.Zip(second.Files))
        {
            Assert.Equal(left.Path, right.Path);
            Assert.Equal(left.Sha256, right.Sha256);
            Assert.True(left.Content.AsSpan().SequenceEqual(right.Content), left.Path);
        }
    }

    [Theory]
    [MemberData(nameof(HtmlFiles))]
    public async Task EveryPageIsReadableWithScriptingDisabled(string path)
    {
        var html = await PageHtml(path);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(EventHandlerAttribute, html);
        Assert.DoesNotContain("_framework", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__internal_", html, StringComparison.Ordinal);
        Assert.DoesNotContain("wasm", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<main id=\"main\"", html, StringComparison.Ordinal);
        Assert.Contains("Skip to content", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHomePageHasItsExactTitleDescriptionAndInventory()
    {
        var html = await PageHtml("index.html");

        Assert.Equal("Hello, world. — ArcForges", Title(html));
        Assert.Contains("<meta name=\"description\" content=\"A small, open-source beginning for ArcForges Web.\"", html, StringComparison.Ordinal);
        Assert.Contains("ArcForges Web · Early preview", html, StringComparison.Ordinal);
        Assert.Contains("Hello,", html, StringComparison.Ordinal);
        Assert.Contains("<em>world.</em>", html, StringComparison.Ordinal);
        Assert.Contains("Every good thing starts somewhere.", html, StringComparison.Ordinal);
        Assert.Contains("Welcome to the first page of ArcForges Web.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/hello\"", html, StringComparison.Ordinal);
        Assert.Contains("Make it your hello", html, StringComparison.Ordinal);
        Assert.Contains("A beginning you can explore.", html, StringComparison.Ordinal);
        Assert.Contains("This small preview has one job: say hello.", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"intro-title\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHelloPageIsTheReactPrerenderWithItsControlsDisabledAndTheDefaultGreeting()
    {
        var html = await PageHtml("hello/index.html");

        Assert.Equal("Your hello — ArcForges", Title(html));
        Assert.Contains("A hello, <em>just for you.</em>", html, StringComparison.Ordinal);
        Assert.Contains("Start with your name. This example runs in your browser, and your name stays on this page.", html, StringComparison.Ordinal);
        Assert.Contains("<label for=\"hello-name\">Your name</label>", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("<input[^>]*id=\"hello-name\"[^>]*disabled[^>]*value=\"World\"", RegexOptions.CultureInvariant), html);
        Assert.Contains("aria-invalid=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"name-hint\"", html, StringComparison.Ordinal);
        Assert.Contains("Up to 80 characters. No account needed.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("The example greeting is shown below. Enable JavaScript to personalize it.", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"greeting-label\">Your greeting</span>", html, StringComparison.Ordinal);
        Assert.Contains("Hello, World!", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/cloud-hello/\"", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("<button[^>]*disabled[^>]*>Say hello", RegexOptions.CultureInvariant), html);
    }

    [Fact]
    public async Task TheCloudHelloPageIsTheReactPrerenderWithNoRequestSent()
    {
        var html = await PageHtml("cloud-hello/index.html");

        Assert.Equal("Server connection — ArcForges", Title(html));
        Assert.Contains("A hello, <em>from the server.</em>", html, StringComparison.Ordinal);
        Assert.Contains("Send a fixed “ArcForges” greeting to check the server connection.", html, StringComparison.Ordinal);
        Assert.Contains("Nothing is sent until you choose to connect.", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("<button[^>]*disabled[^>]*>Check connection", RegexOptions.CultureInvariant), html);
        Assert.Contains("Enable JavaScript to check the server connection.", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"greeting-label\">Server response</span>", html, StringComparison.Ordinal);
        Assert.Contains("No request sent yet.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/hello/\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("api/", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryPageLinksTheShellNavigationAndTheSourceLinkOfTheBuild()
    {
        foreach (var path in HtmlPaths)
        {
            var html = await PageHtml(path);
            Assert.Contains("href=\"#main\"", html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"ArcForges home\"", html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Main navigation\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://github.com/ArcForges/Web\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/license.txt\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/third-party-notices.txt\"", html, StringComparison.Ordinal);
            Assert.Contains("A small beginning. Built in the open.", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheSourceLinkPointsAtTheBuildCommitWhenItIsKnown()
    {
        var commit = new string('f', 40);
        var site = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = commit }, TestContext.Current.CancellationToken);

        var html = Encoding.UTF8.GetString(site.Find("index.html")!.Content);
        Assert.Contains("href=\"https://github.com/ArcForges/Web/tree/" + commit + "\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"https://github.com/ArcForges/Web\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("0123456789abcdef0123456789abcdef01234\"567")]
    public void ASourceRefThatIsNotAFullLowerCaseCommitIsRefused(string sourceRef)
    {
        Assert.Throws<ArgumentException>(() => SiteBuilder.SourceUrl(sourceRef));
    }

    [Fact]
    public async Task EveryPageLinksTheContentHashedStylesheetThatTheSiteEmits()
    {
        var site = await SiteBuilder.BuildAsync(new SiteOptions(), TestContext.Current.CancellationToken);

        var stylesheet = site.Files.Single(file => StylesheetPath.IsMatch(file.Path));
        foreach (var path in HtmlPaths)
        {
            var html = Encoding.UTF8.GetString(site.Find(path)!.Content);
            Assert.Contains("<link rel=\"stylesheet\" href=\"/" + stylesheet.Path + "\"", html, StringComparison.Ordinal);
        }
        var css = Encoding.UTF8.GetString(stylesheet.Content);
        Assert.Contains("@media (max-width: 800px)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@import", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@theme", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecurityHeadersAreTheExactReactValuesWithThePublicSitePolicy()
    {
        var site = await SiteBuilder.BuildAsync(new SiteOptions(), TestContext.Current.CancellationToken);

        var headers = Encoding.UTF8.GetString(site.Find("_headers")!.Content);
        Assert.Equal(ExpectedHeaders, headers);
    }

    [Fact]
    public void ThePublicSitePolicyHasNoWebAssemblyOrUnsafeScriptSource()
    {
        Assert.Contains("script-src 'self';", ExpectedPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("wasm-unsafe-eval", ExpectedPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", ExpectedPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", ExpectedPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNotFoundPageAndRobotsAndFaviconAreTheReactStaticFiles()
    {
        var site = await SiteBuilder.BuildAsync(new SiteOptions(), TestContext.Current.CancellationToken);

        var notFound = Encoding.UTF8.GetString(site.Find("404.html")!.Content);
        Assert.Contains("<title>Page not found — ArcForges</title>", notFound, StringComparison.Ordinal);
        Assert.Contains("<h1>Page not found.</h1>", notFound, StringComparison.Ordinal);
        Assert.Contains("This page is not part of the preview.", notFound, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", notFound, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", notFound, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("User-agent: *\nDisallow: /\n", Encoding.UTF8.GetString(site.Find("robots.txt")!.Content));
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", Encoding.UTF8.GetString(site.Find("favicon.svg")!.Content), StringComparison.Ordinal);
        Assert.Contains("\nh1 {\n", Encoding.UTF8.GetString(site.Find("404.css")!.Content), StringComparison.Ordinal);
    }

    public static TheoryData<string> HtmlFiles => new(HtmlPaths);

    private static async Task<string> PageHtml(string path)
    {
        var site = await SiteBuilder.BuildAsync(new SiteOptions(), TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(site.Find(path)?.Content ?? throw new InvalidOperationException("Missing page: " + path));
    }

    private static string Title(string html)
    {
        var match = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "No title element.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
