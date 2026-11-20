// SPDX-License-Identifier: AGPL-3.0-only
// The App profile's policy rules: exact script-src and style-src, bounded browser interop, no inline style or Virtualize,
// and RunAOTCompilation off. The host page is read directly from source.
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ArcForges.Web.Ui;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class WasmProfilePolicyTests
{
    private static readonly string Root = RepositoryRoot.Find();
    private const string ActivityModulePath = "_framework/arcforges-dashboard-activity.20aaf66b92.js";

    private static string AppSource(string relative) => File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.App", relative));

    [Fact]
    public void TheAppPolicyHasExactlyTheWasmScriptTokensAndSelfOnlyStyles()
    {
        var policy = WasmContentSecurityPolicy.FromHostPages([AppSource("wwwroot/index.html")]);
        var directives = WasmContentSecurityPolicy.ParseDirectives(policy);

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

    /// <summary>
    /// The exact policy that the emitted host page produces (PRF.11 U2, base-uri corrected by S36 and WEB.40 follow-up Web #38).
    /// The shells carry the base element, so base-uri is 'self'; the Site policy keeps 'none'. Any change to the directive set
    /// or to its order is a reviewed decision, because the proof deployment serves exactly this string on both profile paths.
    /// </summary>
    internal const string ExactPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'none'";

    [Fact]
    public void TheEmittedPolicyIsTheExactReviewedString()
    {
        Assert.Equal(ExactPolicy, WasmContentSecurityPolicy.FromHostPages([AppSource("wwwroot/index.html")]));
    }

    [Fact]
    public void TheHostPageDeclaresExactlyOneBaseAtTheRootAndTheShellsRelyOnIt()
    {
        // CLOUD.85 D1: the Account and Chat shells are served at /account/ and /chat/ under base href "/", so the framework
        // resolves from the root. The host page carries one base element and it names the root.
        var html = AppSource("wwwroot/index.html");
        var bases = Regex.Matches(html, @"<base\s[^>]*>", RegexOptions.IgnoreCase);
        var only = Assert.Single(bases);
        Assert.Matches(@"^<base\s+href=""/""\s*/?>$", only.Value);
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
    public void TheHostPageHasOnlyTheFrameworkLoaderAndNoInlineStyle()
    {
        var html = AppSource("wwwroot/index.html");
        Assert.DoesNotMatch(new Regex("<style", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex("\\sstyle\\s*=", RegexOptions.IgnoreCase), html);
        var scripts = Regex.Matches(html, "<script(?<attributes>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var loader = Assert.Single(scripts);
        Assert.Equal("src=\"_framework/blazor.webassembly.js\"", loader.Groups["attributes"].Value.Trim());
        Assert.Empty(loader.Groups["body"].Value);
    }

    [Fact]
    public void TheActivityModuleIsImportedByItsExactContentAddressInTheFrameworkNamespace()
    {
        var root = Path.Combine(Root, "src", "ArcForges.Web.App", "wwwroot");
        var path = Assert.Single(Directory.EnumerateFiles(root, "arcforges-dashboard-activity*.js", SearchOption.AllDirectories));
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        Assert.Equal("_framework/arcforges-dashboard-activity." + hash[..10] + ".js", relative);
        Assert.Equal(ActivityModulePath, relative);
        Assert.Contains("\"./" + relative + "\"", AppSource("Dashboard/DashboardActivity.cs"), StringComparison.Ordinal);
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
    public void TheAppUsesJavaScriptInteropOnlyForLifecycleAndPasskeys()
    {
        var app = Path.Combine(Root, "src", "ArcForges.Web.App");
        var sources = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(app, "*.razor", SearchOption.AllDirectories));
        var interop = new List<string>();
        foreach (var file in sources)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("IJSInProcessRuntime", source, StringComparison.Ordinal);
            if (source.Contains("IJSRuntime", StringComparison.Ordinal))
                interop.Add(Path.GetRelativePath(app, file).Replace('\\', '/'));
        }
        Assert.Equal(new[] { "Dashboard/DashboardActivity.cs", "Dashboard/DashboardPasskeys.cs" }, interop.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheProfileIsStandaloneWebAssemblyWithAheadOfTimeCompilationOff()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.App", "ArcForges.Web.App.csproj"));
        Assert.Contains("Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"", project, StringComparison.Ordinal);
        Assert.Contains("<RunAOTCompilation>false</RunAOTCompilation>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore.Components.Server", project, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProfileStylesheetCarriesNoTailwindPreambleAndDefinesItsTokensOnRoot()
    {
        // S37: nothing compiles the profile stylesheet, so Tailwind at-rules would only request a missing /tailwindcss.
        var css = AppSource("wwwroot/app.css");
        foreach (var token in new[] { "@import \"tailwindcss\"", "@source", "@theme", "@apply", "@tailwind" })
            Assert.DoesNotContain(token, css, StringComparison.Ordinal);
        Assert.Contains("--font-sans:", css, StringComparison.Ordinal);
        Assert.Matches(new Regex(":root\\s*\\{[^}]*--font-sans:", RegexOptions.Singleline), css);
    }
}
