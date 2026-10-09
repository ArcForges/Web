// SPDX-License-Identifier: AGPL-3.0-only
using Xunit;

namespace ArcForges.Web.Ui.Tests;

public sealed class ProfileIdentityTests
{
    [Fact]
    public void ProfileNameIsStable()
    {
        Assert.Equal("ui", ArcForges.Web.Ui.ProfileIdentity.Name);
    }
}
