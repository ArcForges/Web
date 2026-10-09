// SPDX-License-Identifier: AGPL-3.0-only
// The Operations profile skeleton (WEB.40): the shell route renders, the policy is the same exact token set as the App
// profiles, and the separate-origin boundary shares nothing. No operator feature is tested here (OPS.05 and OPS.11).
using ArcForges.Web.Ui;
using Bunit;
using Xunit;

namespace ArcForges.Web.Operations.Tests;

public sealed class OperationsProfileTests
{
    private static readonly string Root = RepositoryRoot.Find();

    [Fact]
    public void TheShellRouteRendersTheSeparateOriginSkeletonAndNoOperatorFeature()
    {
        using var context = new BunitContext();
        var cut = context.Render<ArcForges.Web.Operations.Pages.Shell>();
        Assert.Contains("Operations console", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void ThePolicyHasTheSameExactTokenSetAsTheAppProfiles()
    {
        var html = File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.Operations", "wwwroot", "index.html"));
        var directives = WasmContentSecurityPolicy.ParseDirectives(WasmContentSecurityPolicy.FromHostPages([html]));
        Assert.Equal(new[] { "'self'", "'wasm-unsafe-eval'" }, directives["script-src"]);
        Assert.Equal(new[] { "'self'" }, directives["style-src"]);
        Assert.Equal(new[] { "'self'" }, directives["base-uri"]);
        var policy = WasmContentSecurityPolicy.FromHostPages([html]);
        Assert.DoesNotContain("'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSeparateOriginBoundarySharesNoCookieOrSessionWithAnotherProfile()
    {
        Assert.Empty(ProfileBoundary.SharedCookieOrigins);
        Assert.Equal("own-origin", ProfileBoundary.PolicyScope);
    }

    [Fact]
    public void TheOperationsProfileIsStandaloneWebAssemblyWithAheadOfTimeCompilationOff()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "ArcForges.Web.Operations", "ArcForges.Web.Operations.csproj"));
        Assert.Contains("Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"", project, StringComparison.Ordinal);
        Assert.Contains("<RunAOTCompilation>false</RunAOTCompilation>", project, StringComparison.Ordinal);
    }
}
