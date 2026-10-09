// SPDX-License-Identifier: AGPL-3.0-only
// Normalised-DOM comparison of the public pages (WEB.40 parity gate, 2026-10-09 adjudication). Both sides are parsed with
// the HTML5 parser (AngleSharp), so void-element syntax, attribute-name case, entity form, boolean attribute spelling and
// text-node splitting do not count as differences. Scripts, module preloads and comments are not part of the rendered
// structure and are removed, which is the TB-01 change; every other difference is reported.
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ArcForges.Web.Site.Tests.SiteParity;

/// <summary>The normalised structure of one HTML document, one line per start tag, end tag or text run.</summary>
public sealed record NormalisedHtml(IReadOnlyList<string> Lines, int RemovedScripts, int RemovedModulePreloads, int RemovedComments);

internal static class HtmlNormaliser
{
    /// <summary>The placeholder that replaces a content-hashed stylesheet name. Its bytes are compared separately.</summary>
    internal const string StylesheetPlaceholder = "/assets/<stylesheet>.css";

    private static readonly HtmlParser Parser = new();

    internal static NormalisedHtml Normalise(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = Parser.ParseDocument(html);
        var state = new Walk();
        if (document.DocumentElement is { } root)
        {
            state.Element(root);
        }
        return new NormalisedHtml(state.Lines, state.RemovedScripts, state.RemovedModulePreloads, state.RemovedComments);
    }

    /// <summary>The line-level difference between two normalised documents, in an LCS order; empty when they are equal.</summary>
    internal static IReadOnlyList<string> Difference(IReadOnlyList<string> reference, IReadOnlyList<string> candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        var n = reference.Count;
        var m = candidate.Count;
        var common = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                common[i, j] = string.Equals(reference[i], candidate[j], StringComparison.Ordinal)
                    ? common[i + 1, j + 1] + 1
                    : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }
        var result = new List<string>();
        int a = 0, b = 0;
        while (a < n && b < m)
        {
            if (string.Equals(reference[a], candidate[b], StringComparison.Ordinal))
            {
                a++;
                b++;
            }
            else if (common[a + 1, b] >= common[a, b + 1])
            {
                result.Add("- " + reference[a++]);
            }
            else
            {
                result.Add("+ " + candidate[b++]);
            }
        }
        while (a < n)
        {
            result.Add("- " + reference[a++]);
        }
        while (b < m)
        {
            result.Add("+ " + candidate[b++]);
        }
        return result;
    }

    private sealed class Walk
    {
        private static readonly char[] Whitespace = [' ', '\t', '\n', '\r', '\f'];

        /// <summary>The void elements have no end tag and no children, so their syntax (with or without the slash) is not compared.</summary>
        private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
        };

        internal List<string> Lines { get; } = [];
        internal int RemovedScripts { get; private set; }
        internal int RemovedModulePreloads { get; private set; }
        internal int RemovedComments { get; private set; }

        internal void Element(IElement element)
        {
            if (element.LocalName == "script")
            {
                RemovedScripts++;
                return;
            }
            if (element.LocalName == "link" && HasToken(element.GetAttribute("rel"), "modulepreload"))
            {
                RemovedModulePreloads++;
                return;
            }

            Lines.Add("<" + element.LocalName + Attributes(element) + ">");
            if (VoidElements.Contains(element.LocalName))
            {
                return;
            }
            var text = new StringBuilder();
            foreach (var child in element.ChildNodes)
            {
                switch (child)
                {
                    case IText:
                        text.Append(child.TextContent);
                        break;
                    case IElement childElement:
                        Flush(text);
                        Element(childElement);
                        break;
                    case IComment:
                        RemovedComments++;
                        break;
                }
            }
            Flush(text);
            Lines.Add("</" + element.LocalName + ">");
        }

        private void Flush(StringBuilder text)
        {
            var value = string.Join(' ', text.ToString().Split(Whitespace, StringSplitOptions.RemoveEmptyEntries));
            text.Clear();
            if (value.Length > 0)
            {
                Lines.Add("#text " + value);
            }
        }

        private static string Attributes(IElement element)
        {
            var isStylesheet = HasToken(element.GetAttribute("rel"), "stylesheet");
            var builder = new StringBuilder();
            foreach (var attribute in element.Attributes.OrderBy(attribute => attribute.Name, StringComparer.Ordinal))
            {
                var value = attribute.Value;
                if (attribute.Name == "class")
                {
                    value = string.Join(' ', value.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (isStylesheet && attribute.Name == "href" && IsHashedStylesheet(value))
                {
                    value = StylesheetPlaceholder;
                }
                builder.Append(' ').Append(attribute.Name).Append("=\"").Append(value).Append('"');
            }
            return builder.ToString();
        }

        private static bool IsHashedStylesheet(string href) =>
            href.StartsWith("/assets/", StringComparison.Ordinal) && href.EndsWith(".css", StringComparison.Ordinal);

        private static bool HasToken(string? list, string token) =>
            list is not null && list.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries).Contains(token, StringComparer.OrdinalIgnoreCase);
    }
}
