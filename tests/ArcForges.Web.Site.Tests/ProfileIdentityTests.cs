// SPDX-License-Identifier: AGPL-3.0-only
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class ProfileIdentityTests
{
    [Fact]
    public void ProfileNameIsStable()
    {
        Assert.Equal("site", ArcForges.Web.Site.ProfileIdentity.Name);
    }
}
