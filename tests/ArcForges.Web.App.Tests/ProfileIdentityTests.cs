// SPDX-License-Identifier: AGPL-3.0-only
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class ProfileIdentityTests
{
    [Fact]
    public void ProfileNameIsStable()
    {
        Assert.Equal("app", ArcForges.Web.App.ProfileIdentity.Name);
    }
}
