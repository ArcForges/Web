// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;

namespace ArcForges.Web.Ui;

/// <summary>
/// The Content-Security-Policy of the Blazor WebAssembly standalone profiles (Account, Chat and Operations; P2-021 item 2).
/// The script sources are exactly <c>'self'</c> and <c>'wasm-unsafe-eval'</c> plus the SHA-256 hash of every inline script
/// body of the host page. <c>style-src</c> stays <c>'self'</c>, so components that need inline styles are not used. The
/// policy never carries <c>unsafe-inline</c> or <c>unsafe-eval</c>, and an absolute external script is refused.
/// <c>base-uri</c> is <c>'self'</c>: the shells carry <c>&lt;base href="/"&gt;</c> (CLOUD.85 D1), and <c>'none'</c> blocks that
/// base element, so the relative framework loader resolves under the shell path and the shell never starts (S36). The Site
/// policy keeps <c>base-uri 'none'</c>, because the public pages have no base element.
/// </summary>
public static class WasmContentSecurityPolicy
{
    /// <summary>The Cloudflare header line budget that the policy must fit within (the React _headers rule).</summary>
    public const int HeaderLineBudget = 1800;

    /// <summary>The script-src token that lets the WebAssembly runtime compile; the only eval-like token allowed.</summary>
    public const string WasmUnsafeEval = "'wasm-unsafe-eval'";

    private const string ScriptTag = "<script";
    private const string ScriptClose = "</script>";

    /// <summary>Returns the policy for the given host pages.</summary>
    /// <exception cref="InvalidOperationException">A page has an absolute external script, an unterminated element, or the policy exceeds the line budget.</exception>
    public static string FromHostPages(IEnumerable<string> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var hashes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
            foreach (var body in InlineScriptBodies(page))
                if (body.Length > 0)
                    hashes.Add("'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))) + "'");
        var scriptSources = string.Join(' ', new[] { "'self'", WasmUnsafeEval }.Concat(hashes));
        var policy = "default-src 'self'; script-src " + scriptSources
            + "; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'none'";
        if (policy.Length >= HeaderLineBudget)
            throw new InvalidOperationException("The Content-Security-Policy exceeds the Workers header line budget.");
        return policy;
    }

    /// <summary>Splits a policy into its directives, each mapped to its ordered source tokens.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseDirectives(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var directives = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var part in policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!directives.TryAdd(tokens[0], tokens[1..]))
                throw new InvalidOperationException("Duplicate directive " + tokens[0]);
        }
        return directives;
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
            // Fail closed: a source attribute of any form is an external script the policy does not admit.
            var attributes = html[(open + ScriptTag.Length)..tagEnd];
            if (attributes.Contains("src", StringComparison.OrdinalIgnoreCase))
                ValidateSameOriginSource(attributes.ToString());
            var close = html.IndexOf(ScriptClose, tagEnd + 1, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                throw new InvalidOperationException("A script element is not closed.");
            if (!attributes.Contains("src", StringComparison.OrdinalIgnoreCase))
                yield return html[(tagEnd + 1)..close];
            start = close + ScriptClose.Length;
        }
    }

    private static void ValidateSameOriginSource(string attributes)
    {
        var index = attributes.IndexOf("src", StringComparison.OrdinalIgnoreCase);
        var equals = index < 0 ? -1 : attributes.IndexOf('=', index);
        if (equals < 0)
            throw new InvalidOperationException("A script source without a quoted value is refused.");
        var rest = attributes[(equals + 1)..].TrimStart();
        if (rest.Length == 0 || (rest[0] != '"' && rest[0] != '\''))
            throw new InvalidOperationException("A script source without a quoted value is refused.");
        var closing = rest.IndexOf(rest[0], 1);
        if (closing < 0)
            throw new InvalidOperationException("An unterminated script source is refused.");
        var value = rest[1..closing];
        if (value.StartsWith("//", StringComparison.Ordinal) || value.Contains("://", StringComparison.Ordinal))
            throw new InvalidOperationException("An external script is refused by the WebAssembly profile policy.");
    }
}
