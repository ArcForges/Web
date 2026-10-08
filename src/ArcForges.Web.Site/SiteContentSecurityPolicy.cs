// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;

namespace ArcForges.Web.Site;

/// <summary>
/// The strict Content-Security-Policy of the public Site. The script sources are 'self' plus the SHA-256 hash of every
/// inline script body the pages emit (the React build's derivation). An external script is refused, and the policy
/// never carries unsafe-inline, unsafe-eval or wasm-unsafe-eval: the public pages need no WebAssembly.
/// </summary>
public static class SiteContentSecurityPolicy
{
    /// <summary>The Workers header line budget that the policy must fit within.</summary>
    public const int HeaderLineBudget = 1800;

    private const string ScriptTag = "<script";
    private const string ScriptClose = "</script>";

    /// <summary>Returns the policy for the emitted pages.</summary>
    /// <exception cref="InvalidOperationException">A page emits an external script or an unterminated script element.</exception>
    public static string FromPages(IEnumerable<string> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var hashes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
            foreach (var body in InlineScriptBodies(page))
                if (body.Length > 0)
                    hashes.Add("'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))) + "'");
        var scriptSources = string.Join(' ', new[] { "'self'" }.Concat(hashes));
        var policy = $"default-src 'self'; script-src {scriptSources}; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        if (policy.Length >= HeaderLineBudget)
            throw new InvalidOperationException("The Content-Security-Policy exceeds the Workers header line budget.");
        return policy;
    }

    private static IEnumerable<string> InlineScriptBodies(string html)
    {
        var start = 0;
        while (true)
        {
            var open = html.IndexOf(ScriptTag, start, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
                yield break;
            var tagEnd = html.IndexOf('>', open);
            if (tagEnd < 0)
                throw new InvalidOperationException("A script element is not terminated.");
            // Fail closed: any source attribute, even inside another attribute value, is an external script.
            if (html.AsSpan(open + ScriptTag.Length, tagEnd - open - ScriptTag.Length).Contains("src", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An external script is refused by the public Site policy.");
            var close = html.IndexOf(ScriptClose, tagEnd + 1, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                throw new InvalidOperationException("A script element is not closed.");
            yield return html[(tagEnd + 1)..close];
            start = close + ScriptClose.Length;
        }
    }
}
