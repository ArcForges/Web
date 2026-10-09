// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Operations;

/// <summary>
/// The separate-origin boundary of the Operations console (WEB.40 skeleton; P2-021 item 2). The console keeps its own origin
/// and its own identity: no cookie, session or CSRF value is shared with the Account and Chat profiles, which are served from
/// another origin. The boundary is a value the tests assert, so a later change has to change the record too.
/// </summary>
public static class ProfileBoundary
{
    /// <summary>The origins whose cookies or session values this profile may share. None.</summary>
    public static IReadOnlyList<string> SharedCookieOrigins { get; } = [];

    /// <summary>The Content-Security-Policy scope: the profile's own origin only.</summary>
    public const string PolicyScope = "own-origin";
}
