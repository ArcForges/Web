// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace ArcForges.Web.Policy.Tests.Architecture;

/// <summary>
/// The C# source rules: private namespaces, generated wire types only, the two admitted JSON and HTTP surfaces, the
/// desktop DOM prohibition and the release fixture routes. A file is release code when it is under src/.
/// </summary>
public static partial class ArchitecturePolicy
{
    private static readonly Regex CSharpUsing = new(
        @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[\w.]+\s*=\s*)?([\w.]+)\s*;",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex RazorUsing = new(@"@using\s+(?:static\s+)?([\w.]+)", RegexOptions.CultureInvariant);
    private static readonly Regex WireDeclaration = new(
        @"\b(?:class|record|struct|interface)\s+(\w*(?:Request|Response|Dto|Message))\b", RegexOptions.CultureInvariant);
    private static readonly Regex JsonApi = new(
        @"\bSystem\.Text\.Json\b|\bNewtonsoft\.Json\b|\bJsonSerializer\b|\bJsonNode\b|\bJsonObject\b|\bJsonArray\b|\bJsonDocument\b|\bJsonElement\b|\bUtf8JsonWriter\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex HttpApi = new(
        @"\bHttpClient\b|\bHttpRequestMessage\b|\bHttpResponseMessage\b|\bHttpMethod\b|\b(?:FromJsonAsync|AsJsonAsync|ReadFromJsonAsync|GetStringAsync|GetByteArrayAsync|GetStreamAsync)\b|(?<![\w.])fetch\s*\(",
        RegexOptions.CultureInvariant);
    private static readonly Regex DesktopApi = new(
        @"\bIJSRuntime\b|Microsoft\.JSInterop|Microsoft\.AspNetCore\.Components\.Web\b|\bwindow\b|\bdocument\b|\bHTMLElement\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex DesktopPath = new(@"(?:^|/)desktop[^/]*/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BlazorServerCode = new(
        @"@rendermode|RenderMode\.Interactive|AddInteractiveServer|InteractiveServer|Microsoft\.AspNetCore\.Components\.Server\b|AddRazorComponents|MapRazorComponents|\bCircuit\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex PageRoute = new("@page\\s+\"([^\"]*)\"", RegexOptions.CultureInvariant);
    private static readonly Regex FixtureRouteSegment = new(
        "/(?:tests?|fixtures?|mocks?|debug|dev)(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RazorComment = new("@\\*[\\s\\S]*?\\*@|<!--[\\s\\S]*?-->", RegexOptions.CultureInvariant);

    /// <summary>The one admitted JSON codec: the generated Contracts JSON context decodes the same-origin documents.</summary>
    private static readonly string[] JsonAdmissions = ["src/ArcForges.Web.App/Probe/WireJson.cs"];

    /// <summary>
    /// The admitted HTTP surfaces: the BrowserSession HTTP exception calls (SessionProbe), the generated gRPC-Web
    /// handler of the anonymous greeting (HelloProbe) and the cloud-hello server connection check (ServerConnection).
    /// </summary>
    private static readonly string[] HttpAdmissions =
    [
        "src/ArcForges.Web.App/Probe/SessionProbe.cs",
        "src/ArcForges.Web.App/Probe/HelloProbe.cs",
        "src/ArcForges.Web.Ui/ServerConnection.cs",
    ];

    private static void AuditCSharpSources(PolicyContext context)
    {
        foreach (var file in context.Find(name => name.EndsWith(".cs", StringComparison.Ordinal)
            || name.EndsWith(".razor", StringComparison.Ordinal)))
        {
            var text = context.Text(file) ?? string.Empty;
            var razor = file.EndsWith(".razor", StringComparison.Ordinal);
            var code = razor ? RazorComment.Replace(text, " ") : PolicyText.StripCode(text, templates: false);

            foreach (Match use in (razor ? RazorUsing : CSharpUsing).Matches(code))
                if (PrivateNamespace.IsMatch(use.Groups[1].Value))
                    context.Report("private-import", file, "Private or server namespace: " + use.Groups[1].Value);

            if (DesktopPath.IsMatch(file) && DesktopApi.IsMatch(code))
                context.Report("desktop-dom", file, "Desktop graphs must not use the DOM or JavaScript interop.");

            if (!file.StartsWith("src/", StringComparison.Ordinal))
                continue;

            foreach (Match declaration in WireDeclaration.Matches(code))
                context.Report("wire-source", file, "Wire shapes must come from the published generated package: " + declaration.Groups[1].Value);
            if (!JsonAdmissions.Contains(file, StringComparer.Ordinal) && JsonApi.IsMatch(code))
                context.Report("wire-source", file, "Production wire serialization uses the generated Contracts codecs; JSON needs an explicit admission.");
            if (!HttpAdmissions.Contains(file, StringComparer.Ordinal) && HttpApi.IsMatch(code))
                context.Report("wire-source", file, "Business network calls must use the published generated transport.");
            if (BlazorServerCode.IsMatch(code))
                context.Report("obsolete-target", file, "Blazor Server render modes and circuits are forbidden in release code.");
            foreach (Match route in PageRoute.Matches(code))
                if (FixtureRouteSegment.IsMatch(route.Groups[1].Value))
                    context.Report("release-fixture", file, "Fixture or debug route in the release route graph: " + route.Groups[1].Value);
        }
    }
}
