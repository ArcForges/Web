// SPDX-License-Identifier: AGPL-3.0-only
// The PRF.11 live gate is pure logic and runs everywhere, so CI proves that the live specs stay off on a CI host and without an
// explicit https proof origin (U7). The live specs themselves never run here.
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LivePrf11OptInTests
{
    private static Dictionary<string, string?> Env(params (string Key, string Value)[] pairs)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            values[key] = value;
        return values;
    }

    private const string Origin = "https://proof.arcforges.com/";

    [Fact]
    public void TheLiveSpecsAreOffWithoutAnExplicitOptInAndAProofOrigin()
    {
        Assert.False(LivePrf11OptIn.IsEnabled(Env()));
        Assert.False(LivePrf11OptIn.IsEnabled(Env((LivePrf11OptIn.OptInVariable, "1"))));
        Assert.False(LivePrf11OptIn.IsEnabled(Env((LivePrf11OptIn.ProofOriginVariable, Origin))));
        Assert.False(LivePrf11OptIn.IsEnabled(Env((LivePrf11OptIn.OptInVariable, "yes"), (LivePrf11OptIn.ProofOriginVariable, Origin))));
    }

    [Fact]
    public void ALocalLiveRunWithTheOptInAndTheProofOriginIsEnabled()
    {
        Assert.True(LivePrf11OptIn.IsEnabled(Env(
            (LivePrf11OptIn.OptInVariable, "1"),
            (LivePrf11OptIn.ProofOriginVariable, Origin))));
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("TF_BUILD")]
    [InlineData("GITLAB_CI")]
    public void ACiHostNeverRunsTheLiveSpecsEvenWhenOptedIn(string marker)
    {
        var environment = Env(
            (LivePrf11OptIn.OptInVariable, "1"),
            (LivePrf11OptIn.ProofOriginVariable, Origin),
            (LivePrf11OptIn.CloudReadyVariable, "1"),
            (marker, "true"));
        Assert.False(LivePrf11OptIn.IsEnabled(environment));
        Assert.False(LivePrf11OptIn.IsCloudReady(environment));
    }

    [Theory]
    [InlineData("http://proof.arcforges.com/")]
    [InlineData("https://proof.arcforges.com/account/")]
    [InlineData("https://proof.arcforges.com/?x=1")]
    [InlineData("https://user:secret@proof.arcforges.com/")]
    [InlineData("https://proof.arcforges.com/#frag")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void AProofOriginMustBeAnHttpsOriginWithNoPathQueryUserOrFragment(string origin)
    {
        var environment = Env((LivePrf11OptIn.OptInVariable, "1"), (LivePrf11OptIn.ProofOriginVariable, origin));
        Assert.Null(LivePrf11OptIn.ProofOrigin(environment));
        Assert.False(LivePrf11OptIn.IsEnabled(environment));
    }

    [Fact]
    public void TheCloudPartsAreReadyOnlyWhenTheCloudSwitchIsSetOnALocalLiveRun()
    {
        var local = Env(
            (LivePrf11OptIn.OptInVariable, "1"),
            (LivePrf11OptIn.ProofOriginVariable, Origin));
        Assert.False(LivePrf11OptIn.IsCloudReady(local));

        var ready = Env(
            (LivePrf11OptIn.OptInVariable, "1"),
            (LivePrf11OptIn.ProofOriginVariable, Origin),
            (LivePrf11OptIn.CloudReadyVariable, "1"));
        Assert.True(LivePrf11OptIn.IsCloudReady(ready));
    }
}
