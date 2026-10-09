// SPDX-License-Identifier: AGPL-3.0-only
// The App profile's policy rules (WEB.40 validation; P2-021 item 2): the exact script-src and style-src token sets, no inline
// style, no Virtualize, no JavaScript interop, and RunAOTCompilation off. The host page is read from the built source.
using System.Text.RegularExpressions;
using ArcForges.Web.Ui;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class WasmProfilePolicyTests
{
    private static readonly string Root = RepositoryRoot.Find();

    private static string AppSource(string relative) => File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.App", relative));

    [Fact]
    public void TheAppPolicyHasExactlyTheWasmScriptTokensAndSelfOnlyStyles()
    {
        var policy = WasmContentSecurityPolicy.FromHostPages([AppSource("wwwroot/index.html")]);
        var directives = WasmContentSecurityPolicy.ParseDirectives(policy);

        // The host page has no inline script, so the only script tokens are self and the WebAssembly compile token.
        Assert.Equal(new[] { "'self'", "'wasm-unsafe-eval'" }, directives["script-src"]);
        Assert.Equal(new[] { "'self'" }, directives["style-src"]);
        Assert.Equal(new[] { "'self'" }, directives["default-src"]);
        Assert.Equal(new[] { "'self'" }, directives["connect-src"]);
        // The shells carry <base href="/"> (CLOUD.85 D1), so the profile policy must admit the base element (S36).
        Assert.Equal(new[] { "'self'" }, directives["base-uri"]);
        Assert.DoesNotContain("'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", policy, StringComparison.Ordinal);
        Assert.True(policy.Length < WasmContentSecurityPolicy.HeaderLineBudget);
    }

    [Fact]
    public void ARequiredInlineScriptAddsItsHashAndNothingElseToScriptSources()
    {
        const string page = "<html><head><script>window.start = 1;</script></head><body><script src=\"_framework/blazor.webassembly.js\"></script></body></html>";
        var directives = WasmContentSecurityPolicy.ParseDirectives(WasmContentSecurityPolicy.FromHostPages([page]));
        var sources = directives["script-src"];
        Assert.Equal("'self'", sources[0]);
        Assert.Equal("'wasm-unsafe-eval'", sources[1]);
        var hash = Assert.Single(sources.Skip(2));
        Assert.Matches("^'sha256-[A-Za-z0-9+/]{43}='$", hash);
    }

    [Fact]
    public void AnExternalOrUnquotedScriptSourceIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src=\"https://cdn.example.test/a.js\"></script>"]));
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src=\"//cdn.example.test/a.js\"></script>"]));
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src=app.js></script>"]));
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script>unterminated"]));
    }

    [Fact]
    public void TheHostPageHasNoInlineScriptNoStyleAttributeAndNoInlineStyleElement()
    {
        var html = AppSource("wwwroot/index.html");
        Assert.DoesNotMatch(new Regex("<style", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex("\\sstyle\\s*=", RegexOptions.IgnoreCase), html);
        foreach (Match script in Regex.Matches(html, "<script(?<attributes>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            Assert.True(string.IsNullOrWhiteSpace(script.Groups["body"].Value), "The host page has an inline script body.");
    }

    [Fact]
    public void NoRazorComponentEmitsAnInlineStyleOrUsesVirtualize()
    {
        var pages = Directory.EnumerateFiles(Path.Combine(Root, "src", "ArcForges.Web.App"), "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "src", "ArcForges.Web.Ui"), "*.razor", SearchOption.AllDirectories));
        foreach (var file in pages)
        {
            var source = File.ReadAllText(file);
            Assert.True(!Regex.IsMatch(source, "\\sstyle\\s*=", RegexOptions.IgnoreCase), "Inline style attribute: " + file);
            Assert.DoesNotContain("Virtualize", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheAppUsesNoJavaScriptInteropBecauseNoAuditedPointIsNeeded()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(Root, "src", "ArcForges.Web.App"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "src", "ArcForges.Web.App"), "*.razor", SearchOption.AllDirectories));
        foreach (var file in sources)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("IJSRuntime", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IJSInProcessRuntime", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheProfileIsStandaloneWebAssemblyWithAheadOfTimeCompilationOff()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.App", "ArcForges.Web.App.csproj"));
        Assert.Contains("Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"", project, StringComparison.Ordinal);
        Assert.Contains("<RunAOTCompilation>false</RunAOTCompilation>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore.Components.Server", project, StringComparison.Ordinal);
    }
}
