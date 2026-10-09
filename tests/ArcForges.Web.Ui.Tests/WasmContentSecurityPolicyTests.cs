// SPDX-License-Identifier: AGPL-3.0-only
// The shared WebAssembly profile policy (WEB.40; P2-021 item 2): positive and negative fixtures for the exact token set.
using ArcForges.Web.Ui;
using Xunit;

namespace ArcForges.Web.Ui.Tests;

public sealed class WasmContentSecurityPolicyTests
{
    private const string HostPage = "<!DOCTYPE html><html><head></head><body><div id=\"app\"></div><script src=\"_framework/blazor.webassembly.js\"></script></body></html>";

    [Fact]
    public void ScriptSourcesAreExactlySelfAndWasmUnsafeEvalWithoutHashesForAPageWithoutInlineScript()
    {
        var directives = WasmContentSecurityPolicy.ParseDirectives(WasmContentSecurityPolicy.FromHostPages([HostPage]));
        Assert.Equal(new[] { "'self'", "'wasm-unsafe-eval'" }, directives["script-src"]);
    }

    [Fact]
    public void StyleSourcesAreSelfOnlyAndTheDefaultIsSelf()
    {
        var directives = WasmContentSecurityPolicy.ParseDirectives(WasmContentSecurityPolicy.FromHostPages([HostPage]));
        Assert.Equal(new[] { "'self'" }, directives["style-src"]);
        Assert.Equal(new[] { "'self'" }, directives["default-src"]);
        Assert.Equal(new[] { "'none'" }, directives["object-src"]);
    }

    [Fact]
    public void NoUnsafeTokenAppearsInAnyDirective()
    {
        var policy = WasmContentSecurityPolicy.FromHostPages([HostPage, "<script>window.start = 1;</script>"]);
        Assert.DoesNotContain("'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInlineScriptAddsExactlyItsSha256HashToScriptSources()
    {
        var page = "<script>window.start = 1;</script>";
        var script = WasmContentSecurityPolicy.ParseDirectives(WasmContentSecurityPolicy.FromHostPages([page]))["script-src"];
        Assert.Equal(3, script.Count);
        Assert.Equal("'self'", script[0]);
        Assert.Equal("'wasm-unsafe-eval'", script[1]);
        Assert.Equal(
            "'sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("window.start = 1;"))) + "'",
            script[2]);
    }

    [Fact]
    public void AnAbsoluteExternalScriptIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src=\"https://cdn.example.test/x.js\"></script>"]));
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src='//cdn.example.test/x.js'></script>"]));
    }

    [Fact]
    public void AnUnquotedOrUnterminatedScriptSourceIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src=app.js></script>"]));
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script src=\"app.js></script>"]));
        Assert.Throws<InvalidOperationException>(() => WasmContentSecurityPolicy.FromHostPages(["<script>unclosed"]));
    }

    [Fact]
    public void ThePolicyStaysWithinTheHeaderLineBudget()
    {
        var policy = WasmContentSecurityPolicy.FromHostPages([HostPage]);
        Assert.True(policy.Length < WasmContentSecurityPolicy.HeaderLineBudget);
    }
}
