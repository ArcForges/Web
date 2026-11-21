// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.PublicApi.V1;

namespace ArcForges.Web.App.Pages;

internal sealed record DashboardProfileAttempt(Guid CommandId, long Revision, string? DisplayName, string? Locale, string? Timezone)
{
    internal bool Sending { get; set; }

    internal ProfileUpdate CreatePatch()
    {
        var patch = new ProfileUpdate();
        if (DisplayName is not null)
            patch.DisplayName = DisplayName;
        if (Locale is not null)
            patch.Locale = Locale;
        if (Timezone is not null)
            patch.Timezone = Timezone;
        return patch;
    }
}
