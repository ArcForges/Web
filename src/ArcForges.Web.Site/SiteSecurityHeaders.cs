// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Site;

/// <summary>The Cloudflare <c>_headers</c> file of the public Site: the same rules and values as the React build.</summary>
public static class SiteSecurityHeaders
{
    /// <summary>Renders the file for the given policy. The text is the React securityHeaders template, byte for byte.</summary>
    public static string Render(string contentSecurityPolicy)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentSecurityPolicy);
        if (contentSecurityPolicy.Length >= SiteContentSecurityPolicy.HeaderLineBudget)
            throw new ArgumentException("The policy exceeds the Workers header line budget.", nameof(contentSecurityPolicy));
        return string.Join(
            '\n',
            "/*",
            "  Content-Security-Policy: " + contentSecurityPolicy,
            "  X-Content-Type-Options: nosniff",
            "  Referrer-Policy: no-referrer",
            "  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()",
            "  X-Frame-Options: DENY",
            "  X-Robots-Tag: noindex, nofollow",
            "  Cache-Control: public, no-cache, no-transform",
            "/assets/*",
            "  ! Cache-Control",
            "  Cache-Control: public, max-age=31536000, immutable, no-transform",
            "/__build.json",
            "  ! Cache-Control",
            "  Cache-Control: no-store, no-transform",
            "/__build-info.json",
            "  ! Cache-Control",
            "  Cache-Control: no-store, no-transform") + "\n";
    }
}
