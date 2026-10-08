// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.Policy.Tests.Architecture;
using ArcForges.Web.Policy.Tests.Licence;
using Xunit;

namespace ArcForges.Web.Policy.Tests;

/// <summary>The policy gate over the real Web repository: the tracked and nonignored inputs must pass every rule.</summary>
public sealed class RepositoryPolicyTests
{
    [Fact]
    public void TheRepositoryPassesEveryArchitectureRule()
    {
        var sources = PolicySources.FromRepository(RepositoryRoot.Find());
        Assert.NotEmpty(sources);
        Assert.Empty(ArchitecturePolicy.Audit(sources));
    }

    [Fact]
    public void TheRepositoryPassesEveryLicenceBoundaryRule()
    {
        var sources = PolicySources.FromRepository(RepositoryRoot.Find());
        Assert.Empty(LicencePolicy.Audit(sources));
    }
}
