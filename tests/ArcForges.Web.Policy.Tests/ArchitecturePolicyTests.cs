// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.Policy.Tests.Architecture;
using ArcForges.Web.Policy.Tests.Fixtures;
using ArcForges.Web.Policy.Tests.Licence;
using Xunit;

namespace ArcForges.Web.Policy.Tests;

/// <summary>
/// The GOV.11 successor refusal fixtures: the passing repository is admitted, every rule refuses its negative example
/// and the accepted forms are not refused. The fixture identifiers name the rule family and the sub-rule.
/// </summary>
public sealed class ArchitecturePolicyTests
{
    public static TheoryData<string> RefusalIdentifiers()
    {
        var data = new TheoryData<string>();
        foreach (var identifier in PolicyCases.Refusals.Keys.Order(StringComparer.Ordinal))
            data.Add(identifier);
        return data;
    }

    public static TheoryData<string> AcceptedIdentifiers()
    {
        var data = new TheoryData<string>();
        foreach (var identifier in PolicyCases.Accepted.Keys.Order(StringComparer.Ordinal))
            data.Add(identifier);
        return data;
    }

    [Fact]
    public void PassingRepositoryIsAdmittedByEveryArchitectureAndLicenceRule()
    {
        var sources = PolicyBaseline.Create();
        Assert.Empty(ArchitecturePolicy.Audit(sources));
        Assert.Empty(LicencePolicy.Audit(sources));
    }

    [Theory]
    [MemberData(nameof(RefusalIdentifiers))]
    public void EveryRuleRefusesItsNegativeExample(string identifier)
    {
        var refusal = PolicyCases.Refusals[identifier];
        var sources = PolicyBaseline.Create();
        refusal.Mutate(sources);
        var findings = ArchitecturePolicy.Audit(sources).Concat(LicencePolicy.Audit(sources)).ToArray();
        Assert.Contains(findings, finding => finding.Rule == refusal.Rule);
    }

    [Theory]
    [MemberData(nameof(AcceptedIdentifiers))]
    public void AcceptedFormsAreNotRefused(string identifier)
    {
        var sources = PolicyBaseline.Create();
        PolicyCases.Accepted[identifier](sources);
        Assert.Empty(ArchitecturePolicy.Audit(sources));
        Assert.Empty(LicencePolicy.Audit(sources));
    }

    [Fact]
    public void EveryGovernedRuleFamilyHasANegativeExample()
    {
        var families = new[]
        {
            "workspace", "pins", "obsolete-target", "portable-reference", "production-command", "sdk-ui", "wire-source",
            "private-import", "desktop-dom", "computed-import", "unresolved-import", "release-fixture", "inventory",
            "boundary", "spdx", "override", "reference", "owner", "source-reference", "gradle", "whitespace",
            "final-newline",
        };
        foreach (var family in families)
            Assert.Contains(PolicyCases.Refusals.Values, refusal => refusal.Rule == family);
    }
}
