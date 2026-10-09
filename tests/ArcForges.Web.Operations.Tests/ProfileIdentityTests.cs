// SPDX-License-Identifier: AGPL-3.0-only
using Xunit;

namespace ArcForges.Web.Operations.Tests;

public sealed class ProfileIdentityTests
{
    [Fact]
    public void ProfileNameIsStable()
    {
        Assert.Equal("operations", ArcForges.Web.Operations.ProfileIdentity.Name);
    }
}
