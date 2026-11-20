// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardScopeTests
{
    private const string CliId = "0024h36h2ncsvrh6daqf6dvvqz";
    private const string GuidId = "00112233-4455-6677-8899-aabbccddeeff";

    [Fact]
    public void FullCliAndUuidIdentifiersPreserveTheSameBytesAndSignedRevision()
    {
        var expected = Guid.Parse(GuidId);
        Assert.True(DashboardRunTarget.TryCreate(CliId, GuidId, "9223372036854775807", CliId.ToUpperInvariant(), expected, out var target));
        Assert.NotNull(target);
        Assert.Equal(expected, target.NodeId);
        Assert.Equal(expected, target.EnrollmentId);
        Assert.Equal(expected, target.GrantId);
        Assert.Equal(expected, target.SandboxId);
        Assert.Equal(long.MaxValue, target.GrantRevision);
    }

    [Theory]
    [InlineData("00000000000000000000000000", "1")]
    [InlineData("8024h36h2ncsvrh6daqf6dvvqz", "1")]
    [InlineData("0024h36h2ncsvrh6daqf6dvvqz", "01")]
    [InlineData("0024h36h2ncsvrh6daqf6dvvqz", "9223372036854775808")]
    public void IncompleteScopeAndNoncanonicalRevisionsAreRefused(string node, string revision)
    {
        Assert.False(DashboardRunTarget.TryCreate(node, CliId, revision, CliId, Guid.Parse(GuidId), out var target));
        Assert.Null(target);
    }

    [Fact]
    public void OnlyAnExplicitSameOriginDeploymentSuppliesASignInUri()
    {
        var page = new Uri("https://managed.example.test/devices");
        var configuration = DashboardCenterConfiguration.FromDeployment(page, "https://managed.example.test", "self-hosted");
        Assert.Equal(DashboardCenterMode.SelfHosted, configuration.Mode);
        Assert.Equal(new Uri("https://managed.example.test/api/account/sign-in#return=%2Foverview"), configuration.SignInUri);
        foreach (var foreign in new[] { "https://other.example.test", "https://managed.example.test:444", "https://managed.example.test/api", "https://unknown@managed.example.test" })
        {
            var refused = DashboardCenterConfiguration.FromDeployment(page, foreign, "self-hosted");
            Assert.Equal(DashboardCenterMode.Unconfigured, refused.Mode);
            Assert.Null(refused.SignInUri);
        }
    }
}
