// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.Ui;
using Microsoft.AspNetCore.Components;

namespace ArcForges.Web.Site;

/// <summary>One public page of the Site: its output path, its exact title and description and its content component.</summary>
internal sealed record SitePage(string OutputPath, string Title, string? Description, Type Content);

/// <summary>The public pages, in output order. The titles and descriptions are the React route meta values.</summary>
internal static class SitePages
{
    public static IReadOnlyList<SitePage> All { get; } =
    [
        new("index.html", "Hello, world. — ArcForges", "A small, open-source beginning for ArcForges Web.", typeof(HomeContent)),
        new("hello/index.html", "Your hello — ArcForges", null, typeof(HelloExample)),
        new("cloud-hello/index.html", "Server connection — ArcForges", null, typeof(CloudHelloExample)),
    ];
}
