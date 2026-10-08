// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace ArcForges.Web.Site;

/// <summary>Post-render normalisation of the static HTML: the public pages carry no Blazor runtime markers.</summary>
internal static partial class StaticMarkup
{
    /// <summary>
    /// Removes the Blazor internal attribute markers (for example <c>__internal_preventDefault_onsubmit</c>, which a
    /// form's <c>@onsubmit:preventDefault</c> renders). They only instruct the Blazor runtime, which the public pages
    /// never load, so a static page must not carry them.
    /// </summary>
    public static string RemoveRuntimeMarkers(string html) => RuntimeMarker().Replace(html, string.Empty);

    [GeneratedRegex(@"\s__internal_[A-Za-z_]+(?=[\s>])", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeMarker();
}
