// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using ArcForges.Web.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArcForges.Web.Site;

/// <summary>
/// Builds the static public Site at build time with the first-party Razor HtmlRenderer. The output has no WebAssembly,
/// no JavaScript and no runtime dependency: every page is complete HTML and one content-hashed stylesheet, and two
/// builds of the same inputs produce the same bytes.
/// </summary>
public static class SiteBuilder
{
    /// <summary>The source link of a build without a source ref.</summary>
    public const string RepositoryUrl = "https://github.com/ArcForges/Web";

    private const string HtmlType = "text/html; charset=utf-8";
    private const string TextType = "text/plain; charset=utf-8";

    /// <summary>Builds the Site. The result lists every file in ordinal path order.</summary>
    public static async Task<SiteOutput> BuildAsync(SiteOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sourceUrl = SourceUrl(options.SourceRef);
        var stylesheet = StaticAssets.Bytes("site.css");
        var stylesheetPath = "assets/site." + Sha256Hex(stylesheet)[..16] + ".css";
        var stylesheetHref = "/" + stylesheetPath;

        var pages = new List<SiteFile>();
        var html = new List<string>();
        using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        foreach (var page in SitePages.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await RenderAsync(renderer, page, sourceUrl, stylesheetHref).ConfigureAwait(false);
            html.Add(document);
            pages.Add(new SiteFile(page.OutputPath, Encoding.UTF8.GetBytes(document), HtmlType));
        }

        var policy = SiteContentSecurityPolicy.FromPages(html);
        var files = new List<SiteFile>(pages)
        {
            new("404.html", StaticAssets.Bytes("404.html"), HtmlType),
            new("404.css", StaticAssets.Bytes("404.css"), "text/css; charset=utf-8"),
            new("robots.txt", StaticAssets.Bytes("robots.txt"), TextType),
            new("favicon.svg", StaticAssets.Bytes("favicon.svg"), "image/svg+xml"),
            new(stylesheetPath, stylesheet, "text/css; charset=utf-8"),
            new("_headers", Encoding.UTF8.GetBytes(SiteSecurityHeaders.Render(policy)), TextType),
        };
        return new SiteOutput(files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Returns the source link for a source ref, which must be a full lower-case commit id when given.</summary>
    public static string SourceUrl(string? sourceRef)
    {
        if (sourceRef is null)
            return RepositoryUrl;
        if (sourceRef.Length != 40 || !sourceRef.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            throw new ArgumentException("The source ref must be a 40-character lower-case commit id.", nameof(sourceRef));
        return RepositoryUrl + "/tree/" + sourceRef;
    }

    private static async Task<string> RenderAsync(HtmlRenderer renderer, SitePage page, string sourceUrl, string stylesheetHref)
    {
        RenderFragment body = builder =>
        {
            builder.OpenComponent(0, page.Content);
            builder.CloseComponent();
        };
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Title"] = page.Title,
            ["Description"] = page.Description,
            ["Stylesheet"] = stylesheetHref,
            ["SourceUrl"] = sourceUrl,
            ["Body"] = body,
        });
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SiteDocument>(parameters).ConfigureAwait(false);
            return "<!DOCTYPE html>" + StaticMarkup.RemoveRuntimeMarkers(output.ToHtmlString());
        }).ConfigureAwait(false);
    }

    private static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
}
