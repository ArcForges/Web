// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.Tooling.Profiles;
using Xunit;

namespace ArcForges.Web.Tooling.Tests;

/// <summary>
/// The rules of the Blazor profile bundle that the Cloud proof deployment consumes: the framework cache rule and the
/// fingerprint requirement behind it. Each rule has a passing and a failing example.
/// </summary>
public sealed class ProfileBundleRulesTests
{
    private static readonly IReadOnlyDictionary<string, string> Policies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["account"] = "default-src 'self'",
        ["chat"] = "default-src 'self'",
    };

    [Fact]
    public void FingerprintedFrameworkFilesAndTheirPrecompressedSiblingsAreAccepted()
    {
        ProfileBundle.RequireFingerprintedFramework(
        [
            "_framework/dotnet.native.b6l13xorvf.js",
            "_framework/dotnet.native.b6l13xorvf.js.br",
            "_framework/dotnet.runtime.v06hirbjsv.js",
            "_framework/ArcForges.Web.App.w1s8cjv5ju.wasm",
            "_framework/ArcForges.Web.App.w1s8cjv5ju.wasm.gz",
            "_framework/icudt_CJK.tjcz0u77k5.dat",
            "index.html",
            "app.css",
        ]);
    }

    [Fact]
    public void TheTwoUnfingerprintedLoadersAreAcceptedAndRevalidated()
    {
        ProfileBundle.RequireFingerprintedFramework(
        [
            "_framework/blazor.webassembly.js",
            "_framework/blazor.webassembly.js.gz",
            "_framework/dotnet.js",
        ]);
        Assert.Equal(["_framework/blazor.webassembly.js", "_framework/dotnet.js"], ProfileBundle.UnfingerprintedFrameworkLoaders);
    }

    [Theory]
    [InlineData("_framework/app.js")]
    [InlineData("_framework/ArcForges.Web.App.wasm")]
    [InlineData("_framework/Foo.Bar.wasm")]
    [InlineData("_framework/blazor.boot.json")]
    [InlineData("_framework/ArcForges.Web.App.w1s8cjv5j.wasm")]
    [InlineData("_framework/ArcForges.Web.App.W1S8CJV5JU.wasm")]
    [InlineData("_framework/nested/ArcForges.Web.App.w1s8cjv5ju.wasm")]
    public void AnUnfingerprintedFrameworkFileFailsTheBuild(string path)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProfileBundle.RequireFingerprintedFramework([path]));
        Assert.Contains("Unfingerprinted framework file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadersCarryTheImmutableFrameworkRuleAndTheLoaderOverrides()
    {
        var headers = ProfileBundle.HeadersText(Policies);

        Assert.Contains(
            "/_framework/*\n  ! Cache-Control\n  Cache-Control: public, max-age=31536000, immutable, no-transform\n",
            headers,
            StringComparison.Ordinal);
        Assert.Contains(
            "/_framework/blazor.webassembly.js\n  ! Cache-Control\n  Cache-Control: public, no-cache, no-transform\n",
            headers,
            StringComparison.Ordinal);
        Assert.Contains(
            "/_framework/dotnet.js\n  ! Cache-Control\n  Cache-Control: public, no-cache, no-transform\n",
            headers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadersAllowNoUnsafeScriptAndNameEachProfilePathOnce()
    {
        var headers = ProfileBundle.HeadersText(Policies);

        Assert.DoesNotContain("unsafe-eval", headers, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", headers, StringComparison.Ordinal);
        Assert.DoesNotContain("/index.html", headers, StringComparison.Ordinal);
        Assert.Equal(2, headers.Split("/account/*").Length - 1 + (headers.Split("/chat/*").Length - 1));
    }

    [Fact]
    public void AProfilePolicyBeyondTheHeaderLineBudgetIsRefused()
    {
        var oversized = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["account"] = new string('a', ProfileBundle.HeaderLineBudget),
            ["chat"] = "default-src 'self'",
        };

        Assert.Throws<InvalidOperationException>(() => ProfileBundle.HeadersText(oversized));
    }
}
