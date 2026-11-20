// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ArcForges.Web.App.Probe;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace ArcForges.Web.App.Dashboard;

/// <summary>Reads only the deployment-owned public settings served beside this dashboard.</summary>
public static class DashboardConfigurationLoader
{
    private const int BodyLimit = 4096;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public static async Task<DashboardCenterConfiguration> ReadAsync(Uri page, HttpMessageHandler transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(transport);
        var unconfigured = DashboardCenterConfiguration.FromDeployment(page, null, null);
        if (!page.IsAbsoluteUri || page.UserInfo.Length != 0 ||
            (page.Scheme != Uri.UriSchemeHttps && !(page.Scheme == Uri.UriSchemeHttp && page.IsLoopback)))
        {
            return unconfigured;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(page, "/appsettings.json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        request.SetBrowserRequestOption("redirect", "error");
        request.SetBrowserResponseStreamingEnabled(true);

        using var invoker = new HttpMessageInvoker(transport, disposeHandler: false);
        try
        {
            using var response = await invoker.SendAsync(request, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK ||
                !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return unconfigured;
            }

            var bytes = await BoundedBody.ReadAsync(response.Content, BodyLimit, deadline.Token);
            return Parse(page, bytes, unconfigured);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ProbeFailureException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return unconfigured;
        }
    }

    private static DashboardCenterConfiguration Parse(Uri page, ReadOnlyMemory<byte> bytes, DashboardCenterConfiguration unconfigured)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count(field => field.NameEquals("Dashboard")) != 1 ||
            root.GetProperty("Dashboard") is not { ValueKind: JsonValueKind.Object } dashboard)
        {
            return unconfigured;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in dashboard.EnumerateObject())
        {
            if (field.Name is not ("CenterOrigin" or "Mode" or "Name") ||
                field.Value.ValueKind != JsonValueKind.String ||
                !fields.TryAdd(field.Name, field.Value.GetString() ?? string.Empty))
            {
                return unconfigured;
            }
        }
        return DashboardCenterConfiguration.FromDeployment(page, fields.GetValueOrDefault("CenterOrigin"),
            fields.GetValueOrDefault("Mode"), fields.GetValueOrDefault("Name"));
    }
}
