// SPDX-License-Identifier: AGPL-3.0-only
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ArcForges.Contracts.PublicApi.Http.V1.Browser;
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowser;
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowserError;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace ArcForges.Web.App.Probe;

/// <summary>What the page may show about the current session. Ids and the cookie are never part of it.</summary>
public abstract record SessionSnapshot(string CsrfToken);

/// <summary>The visitor has no session; only the CSRF token of the anonymous bootstrap is kept.</summary>
public sealed record AnonymousSession(string CsrfToken) : SessionSnapshot(CsrfToken);

/// <summary>The visitor has a session. The recovery generation is the exact canonical decimal text.</summary>
public sealed record AuthenticatedSession(
    string CsrfToken,
    string? DisplayName,
    string? Locale,
    string? Timezone,
    string ExpiresAt,
    string? IdleExpiresAt,
    int Workspaces,
    string RecoveryGeneration) : SessionSnapshot(CsrfToken);

/// <summary>The same-origin base address of the probes (scheme and authority of the page).</summary>
public sealed record ProbeOrigin(Uri Value);

/// <summary>
/// The same-origin session exception routes (the TypeScript session.ts port): the bootstrap GET and the logout POST with
/// the <c>X-AF-CSRF</c> header. Route paths and methods come from the generated <see cref="BrowserSessionRoutes"/> catalogue.
/// Requests carry the session cookie of the origin, follow no redirect in script, and are never cached.
/// </summary>
public sealed partial class SessionProbe
{
    /// <summary>The CSRF request header of cookie-authenticated unsafe operations (Design AU-08).</summary>
    public const string CsrfHeader = "X-AF-CSRF";

    /// <summary>The bootstrap body bound, from the generated schema (x-arcforges-max-bytes).</summary>
    public const int BootstrapBodyLimit = 16384;

    /// <summary>The logout receipt body bound.</summary>
    public const int ReceiptBodyLimit = 4096;

    /// <summary>The CSRF token shape of the bootstrap schema: 16 to 128 URL-safe characters.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex CsrfToken();

    private static readonly WebBrowserSessionRoute WebBootstrap = WebRoute("webBrowser.bootstrap");
    private static readonly WebBrowserSessionRoute WebLogout = WebRoute("webBrowser.logout");
    private static readonly WebBrowserSessionRoute WebBeginStepUp = WebRoute("webBrowser.beginStepUp");
    private static readonly WebBrowserSessionRoute WebCompleteStepUp = WebRoute("webBrowser.completeStepUp");

    private readonly HttpMessageInvoker _invoker;
    private readonly Uri _origin;

    /// <summary>Creates the probe over a transport handler that keeps no state of its own.</summary>
    public SessionProbe(HttpMessageHandler handler, ProbeOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(origin);
        _invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        _origin = origin.Value;
    }

    /// <summary>GETs the bootstrap route, the cookie riding along, and returns what the page may show.</summary>
    /// <exception cref="ProbeFailureException">Any typed failure of the call.</exception>
    public async Task<SessionSnapshot> ReadSessionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_origin, Route("browser.bootstrap").Path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await JsonBodyAsync(response, BootstrapBodyLimit, cancellationToken).ConfigureAwait(false);
        var body = WireJson.Decode(bytes, BrowserBootstrapResponseJsonContext.Default.BrowserBootstrapResponse);
        return ToSnapshot(body);
    }

    internal async Task<(SessionSnapshot Snapshot, string? AccountId, string? SessionId)> ReadWebBoundSessionAsync(CancellationToken cancellationToken)
    {
        using var request = WebRequest(WebBootstrap);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await JsonBodyAsync(response, BootstrapBodyLimit, cancellationToken).ConfigureAwait(false);
        var body = WireJson.Decode(bytes, WebBrowserBootstrapResponseJsonContext.Default.WebBrowserBootstrapResponse);
        var session = body.Session;
        var profile = body.Profile;
        var csrf = ValidateBootstrap(body.CsrfToken, body.Authenticated, session is not null, profile is not null);
        if (session is null)
            return (new AnonymousSession(csrf), null, null);

        var snapshot = new AuthenticatedSession(csrf, profile?.DisplayName, profile?.Locale, profile?.Timezone,
            session.ExpiresAt, session.IdleExpiresAt, session.WorkspaceIds.Length, RecoveryText(session.RecoveryGeneration));
        return (snapshot, session.UserId, session.SessionId);
    }

    /// <summary>
    /// POSTs the logout route with the CSRF token and no body. Only the receipt effect <c>happened</c> ends the session on
    /// the page; <c>didNotHappen</c> and <c>unknown</c> are failures, because the cookie may still be valid.
    /// </summary>
    /// <exception cref="ProbeFailureException">Any typed failure of the call.</exception>
    public async Task EndSessionAsync(string csrfToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(csrfToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_origin, Route("browser.logout").Path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(CsrfHeader, csrfToken);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await JsonBodyAsync(response, ReceiptBodyLimit, cancellationToken).ConfigureAwait(false);
        var receipt = WireJson.Decode(bytes, BrowserReceiptJsonContext.Default.BrowserReceipt);
        ConfirmLogout(receipt.Effect);
    }

    internal async Task EndWebSessionAsync(string csrfToken, CancellationToken cancellationToken)
    {
        var reply = await PostWebSessionAsync(WebLogout, null, 0, ReceiptBodyLimit, csrfToken, cancellationToken).ConfigureAwait(false);
        var receipt = WireJson.Decode(reply, WebBrowserReceiptJsonContext.Default.WebBrowserReceipt);
        ConfirmLogout(receipt.Effect);
    }

    private static void ConfirmLogout(string effect)
    {
        if (effect != "happened")
            throw new ProbeFailureException(effect == "didNotHappen" ? FailureKind.Rejected : FailureKind.Unexpected);
    }

    internal async Task<WebBrowserAuthChallenge> BeginStepUpAsync(WebBrowserBeginStepUpRequest proof, string csrfToken, CancellationToken cancellationToken)
    {
        var body = WireJson.Encode(proof, WebBrowserBeginStepUpRequestJsonContext.Default.WebBrowserBeginStepUpRequest);
        var reply = await PostWebSessionAsync(WebBeginStepUp, body, 4096, 65536, csrfToken, cancellationToken).ConfigureAwait(false);
        return WireJson.Decode(reply, WebBrowserAuthChallengeJsonContext.Default.WebBrowserAuthChallenge);
    }

    internal async Task<WebBrowserStepUpEvidence> CompleteStepUpAsync(WebBrowserCompleteStepUpRequest proof, string csrfToken, CancellationToken cancellationToken)
    {
        var body = WireJson.Encode(proof, WebBrowserCompleteStepUpRequestJsonContext.Default.WebBrowserCompleteStepUpRequest);
        var reply = await PostWebSessionAsync(WebCompleteStepUp, body, 131072, 4096, csrfToken, cancellationToken).ConfigureAwait(false);
        return WireJson.Decode(reply, WebBrowserStepUpEvidenceJsonContext.Default.WebBrowserStepUpEvidence);
    }

    private async Task<byte[]> PostWebSessionAsync(WebBrowserSessionRoute route, byte[]? body, int sendLimit, int receiveLimit,
        string csrfToken, CancellationToken cancellationToken)
    {
        if (!CsrfToken().IsMatch(csrfToken) || (body?.Length ?? 0) > sendLimit)
            throw new ProbeFailureException(FailureKind.Rejected);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = WebRequest(route);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        request.Headers.Add(CsrfHeader, csrfToken);

        using var response = await SendAsync(request, deadline.Token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 400 and < 500 &&
            string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await BoundedBody.ReadAsync(response.Content, 4096, deadline.Token).ConfigureAwait(false);
            var error = WireJson.Decode(bytes, WebBrowserErrorReplyJsonContext.Default.WebBrowserErrorReply).Error;
            var kind = error.Category == "authentication" ? FailureKind.Unauthenticated : FailureMapping.FromStatus((int)response.StatusCode);
            var correlation = Guid.TryParseExact(error.CorrelationId, "D", out var id) && id != Guid.Empty ? id : (Guid?)null;
            throw new ProbeFailureException(kind, correlationId: correlation);
        }

        return await JsonBodyAsync(response, receiveLimit, deadline.Token).ConfigureAwait(false);
    }

    private HttpRequestMessage WebRequest(WebBrowserSessionRoute route)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), new Uri(_origin, "/api" + route.Path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        request.SetBrowserRequestOption("redirect", "error");
        request.SetBrowserResponseStreamingEnabled(true);
        return request;
    }

    /// <summary>The page snapshot of a bootstrap answer. A contradictory answer is malformed, never a session.</summary>
    internal static SessionSnapshot ToSnapshot(BrowserBootstrapResponse body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = body.Session;
        var csrf = ValidateBootstrap(body.CsrfToken, body.Authenticated, session is not null, body.Profile is not null);
        if (session is null)
            return new AnonymousSession(csrf);
        var profile = body.Profile;
        return new AuthenticatedSession(
            csrf,
            profile?.DisplayName,
            profile?.Locale,
            profile?.Timezone,
            session.ExpiresAt,
            session.IdleExpiresAt,
            session.WorkspaceIds.Count(),
            RecoveryText(session.RecoveryGeneration));
    }

    private static string ValidateBootstrap(string? csrf, bool authenticated, bool hasSession, bool hasProfile)
    {
        if (csrf is null || !CsrfToken().IsMatch(csrf) || authenticated != hasSession || (!authenticated && hasProfile))
            throw new ProbeFailureException(FailureKind.Malformed);
        return csrf;
    }

    private static string RecoveryText(string text) => Exact.TryUnsigned(text, out var generation)
        ? generation.Text
        : throw new ProbeFailureException(FailureKind.Malformed);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _invoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not ProbeFailureException)
        {
            throw BoundedBody.FromTransport(exception, cancellationToken);
        }
    }

    private static async Task<byte[]> JsonBodyAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        // A status of zero is an opaque redirect the page refuses to follow: the answer is unavailable, as a refused fetch is.
        if ((int)response.StatusCode == 0)
            throw new ProbeFailureException(FailureKind.Unavailable);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            throw new ProbeFailureException(FailureMapping.FromStatus((int)response.StatusCode));
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new ProbeFailureException(FailureKind.Malformed);
        return await BoundedBody.ReadAsync(response.Content, limit, cancellationToken).ConfigureAwait(false);
    }

    private static BrowserSessionRoute Route(string id) =>
        BrowserSessionRoutes.All.FirstOrDefault(candidate => candidate.Id == id)
        ?? throw new InvalidOperationException($"The generated Contracts catalogue has no browser session route {id}.");

    private static WebBrowserSessionRoute WebRoute(string id) => WebBrowserSessionRoutes.All.Single(route => route.Id == id);
}
