// SPDX-License-Identifier: AGPL-3.0-only
// The opt-in gate is pure logic and runs everywhere, so CI proves that the browser checks stay off on a CI host.
using Xunit;

namespace ArcForges.Web.Browser.Tests;

public sealed class LocalOptInTests
{
    private static Dictionary<string, string?> Env(params (string Key, string Value)[] pairs)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            values[key] = value;
        return values;
    }

    [Fact]
    public void TheBrowserChecksAreOffWithoutAnExplicitLocalOptIn()
    {
        Assert.False(LocalOptIn.IsEnabled(Env()));
        Assert.False(LocalOptIn.IsEnabled(Env((LocalOptIn.OptInVariable, "1"))));
        Assert.False(LocalOptIn.IsEnabled(Env((LocalOptIn.BaseUrlVariable, "https://preview.example.test"))));
        Assert.False(LocalOptIn.IsEnabled(Env((LocalOptIn.OptInVariable, "yes"), (LocalOptIn.BaseUrlVariable, "https://preview.example.test"))));
    }

    [Fact]
    public void ALocalRunWithTheOptInAndABaseUrlIsEnabled()
    {
        Assert.True(LocalOptIn.IsEnabled(Env(
            (LocalOptIn.OptInVariable, "1"),
            (LocalOptIn.BaseUrlVariable, "http://127.0.0.1:5173"))));
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("TF_BUILD")]
    [InlineData("GITLAB_CI")]
    public void ACiHostNeverRunsTheBrowserChecksEvenWhenOptedIn(string marker)
    {
        var environment = Env(
            (LocalOptIn.OptInVariable, "1"),
            (LocalOptIn.BaseUrlVariable, "http://127.0.0.1:5173"),
            (marker, "true"));
        Assert.True(LocalOptIn.IsCi(environment));
        Assert.False(LocalOptIn.IsEnabled(environment));
    }
}
