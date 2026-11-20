// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.App.Dashboard;

public enum DashboardCenterMode { Unconfigured, SelfHosted, OfficialHosted }

/// <summary>The public deployment settings of this dashboard's single same-origin Center.</summary>
public sealed class DashboardCenterConfiguration
{
    private DashboardCenterConfiguration(Uri? origin, DashboardCenterMode mode, string? name)
    {
        Origin = origin;
        Mode = mode;
        Name = name;
    }

    public Uri? Origin { get; }
    public DashboardCenterMode Mode { get; }
    public string? Name { get; }

    public Uri? SignInUri => Origin is null ? null : new Uri(Origin, "/api/account/sign-in#return=%2Foverview");

    public static DashboardCenterConfiguration FromDeployment(Uri page, string? origin, string? mode, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var selected = mode switch
        {
            "self-hosted" => DashboardCenterMode.SelfHosted,
            "official-hosted" => DashboardCenterMode.OfficialHosted,
            _ => DashboardCenterMode.Unconfigured,
        };
        if (!page.IsAbsoluteUri || selected == DashboardCenterMode.Unconfigured ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var center) ||
            (center.Scheme != Uri.UriSchemeHttps && !(center.Scheme == Uri.UriSchemeHttp && center.IsLoopback && selected == DashboardCenterMode.SelfHosted)) ||
            center.UserInfo.Length != 0 || center.AbsolutePath != "/" || center.Query.Length != 0 || center.Fragment.Length != 0 ||
            !string.Equals(page.GetLeftPart(UriPartial.Authority), center.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
        {
            return new DashboardCenterConfiguration(null, DashboardCenterMode.Unconfigured, null);
        }

        return new DashboardCenterConfiguration(center, selected, string.IsNullOrWhiteSpace(name) ? null : name);
    }
}
