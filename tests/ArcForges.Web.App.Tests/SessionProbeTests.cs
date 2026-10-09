// SPDX-License-Identifier: AGPL-3.0-only
// Port of tests/unit/app-session.test.ts: the same-origin session probe (bootstrap, logout, the bounded body and the typed
// failures). The server is a test-only double (TestDoubles/ScriptedServer.cs); the documents are wire fixtures.
using System.Net;
using System.Text;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class SessionProbeTests
{
    private const string Json = "application/json";

    private static SessionProbe ProbeFor(HttpMessageHandler handler) =>
        new(handler, new ProbeOrigin(ProbeFixtures.Origin));

    private static CancellationToken Never => CancellationToken.None;

    private static async Task<FailureKind> FailureOf(Func<Task> call)
    {
        var failure = await Assert.ThrowsAsync<ProbeFailureException>(call);
        return failure.Kind;
    }

    private static async Task<FailureKind> ReadFailure(HttpMessageHandler handler) =>
        await FailureOf(() => ProbeFor(handler).ReadSessionAsync(Never));

    [Fact]
    public async Task BootstrapIsASameOriginCookieGetDrivenByTheGeneratedRouteCatalogue()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson)));
        var session = await ProbeFor(server).ReadSessionAsync(Never);

        var seen = Assert.Single(server.Requests);
        Assert.Equal($"{ProbeFixtures.Origin.Scheme}://{ProbeFixtures.Origin.Authority}/session/v1/bootstrap", seen.Url.ToString());
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.Null(seen.CsrfHeader);
        Assert.False(seen.HasAuthorizationHeader);
        Assert.Equal(Json, seen.AcceptHeader);
        Assert.Equal(
            new AuthenticatedSession(
                ProbeFixtures.AuthenticatedCsrf,
                "Ada Lovelace",
                "en-GB",
                "Europe/London",
                "2026-10-03T08:00:00.000000Z",
                "2026-10-02T20:30:00.000000Z",
                2,
                "18446744073709551615"),
            session);
    }

    [Fact]
    public async Task AnAnonymousAnswerCarriesOnlyTheCsrfToken()
    {
        var server = ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson)));
        var session = await ProbeFor(server).ReadSessionAsync(Never);
        Assert.Equal(new AnonymousSession(ProbeFixtures.AnonymousCsrf), session);
    }

    [Fact]
    public async Task AnAuthenticatedSessionWithoutAProfileStillPresents()
    {
        var node = ProbeFixtures.AuthenticatedNode();
        node.Remove("profile");
        var server = ScriptedServer.Always(() => Responses.Json(ProbeFixtures.Bytes(node)));

        var session = Assert.IsType<AuthenticatedSession>(await ProbeFor(server).ReadSessionAsync(Never));
        Assert.Null(session.DisplayName);
        Assert.Null(session.Locale);
    }

    [Fact]
    public async Task ContradictoryOrInexactBootstrapAnswersAreMalformedNeverASession()
    {
        var withoutSession = ProbeFixtures.AuthenticatedNode();
        withoutSession.Remove("session");

        var anonymousWithSession = ProbeFixtures.AnonymousNode();
        anonymousWithSession["session"] = ProbeFixtures.AuthenticatedNode()["session"]!.DeepClone();

        var anonymousWithProfile = ProbeFixtures.AnonymousNode();
        anonymousWithProfile["profile"] = ProbeFixtures.AuthenticatedNode()["profile"]!.DeepClone();

        var leadingZero = ProbeFixtures.AuthenticatedNode();
        leadingZero["session"]!["recoveryGeneration"] = "01";

        var aboveUint64 = ProbeFixtures.AuthenticatedNode();
        aboveUint64["session"]!["recoveryGeneration"] = "18446744073709551616";

        var cases = new (string Name, byte[] Body)[]
        {
            ("authenticated without a session", ProbeFixtures.Bytes(withoutSession)),
            ("anonymous with a session", ProbeFixtures.Bytes(anonymousWithSession)),
            ("anonymous with a profile", ProbeFixtures.Bytes(anonymousWithProfile)),
            ("leading zero generation", ProbeFixtures.Bytes(leadingZero)),
            ("generation above uint64", ProbeFixtures.Bytes(aboveUint64)),
        };
        foreach (var (name, body) in cases)
            Assert.True(
                await ReadFailure(ScriptedServer.Always(() => Responses.Json(body))) == FailureKind.Malformed,
                name);
    }

    [Fact]
    public async Task TheGeneratedCodecRefusesMalformedUnknownFieldAndWrongTypeDocuments()
    {
        var bodies = new[]
        {
            string.Empty,
            "{",
            "[]",
            "null",
            "{\"csrfToken\":\"x\"}",
            "{\"csrfToken\":\"x\",\"authenticated\":\"yes\"}",
            "{\"csrfToken\":\"x\",\"authenticated\":false,\"extra\":1}",
            "{\"csrfToken\":\"x\",\"authenticated\":false,\"authenticated\":true}",
            "{\"csrfToken\":\"" + new string('x', 20) + "\",\"authenticated\":false",
        };
        foreach (var body in bodies)
            Assert.True(
                await ReadFailure(ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(body)))) == FailureKind.Malformed,
                body);
    }

    [Fact]
    public async Task OnlyAJsonTwoHundredIsReadEveryOtherStatusIsATypedFailureWithAFixedMessage()
    {
        var statuses = new (HttpStatusCode Status, FailureKind Kind)[]
        {
            ((HttpStatusCode)301, FailureKind.Malformed),
            (HttpStatusCode.BadRequest, FailureKind.Rejected),
            (HttpStatusCode.Unauthorized, FailureKind.Unauthenticated),
            (HttpStatusCode.Forbidden, FailureKind.Forbidden),
            (HttpStatusCode.NotFound, FailureKind.Rejected),
            (HttpStatusCode.RequestTimeout, FailureKind.Timeout),
            (HttpStatusCode.RequestEntityTooLarge, FailureKind.Limit),
            ((HttpStatusCode)429, FailureKind.Limit),
            (HttpStatusCode.InternalServerError, FailureKind.Unexpected),
            (HttpStatusCode.BadGateway, FailureKind.Unavailable),
            (HttpStatusCode.ServiceUnavailable, FailureKind.Unavailable),
            (HttpStatusCode.GatewayTimeout, FailureKind.Timeout),
        };
        foreach (var (status, kind) in statuses)
        {
            // A body that would parse as a session proves the status is checked first.
            var failure = await Assert.ThrowsAsync<ProbeFailureException>(() =>
                ProbeFor(ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson), status)))
                    .ReadSessionAsync(Never));
            Assert.True(failure.Kind == kind, status.ToString());
            Assert.DoesNotContain(ProbeFixtures.AuthenticatedCsrf, failure.Message, StringComparison.Ordinal);
        }

        foreach (var contentType in new[] { "text/html", "application/x-ndjson", "application/jsonp" })
            Assert.True(
                await ReadFailure(ScriptedServer.Always(() => Responses.Raw(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson), contentType))) == FailureKind.Malformed,
                contentType);
        Assert.True(
            await ReadFailure(ScriptedServer.Always(() => Responses.Raw(Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson), null))) == FailureKind.Malformed,
            "no content type");

        var parameters = await ProbeFor(ScriptedServer.Always(() =>
            Responses.Raw(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson), "application/json; charset=utf-8"))).ReadSessionAsync(Never);
        Assert.IsType<AnonymousSession>(parameters);
    }

    [Fact]
    public async Task TheBodyIsBoundedBeforeItIsParsed()
    {
        var exact = Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson);
        var padded = new byte[16384 + 1];
        Array.Fill(padded, (byte)0x20);
        exact.CopyTo(padded, 16384 + 1 - exact.Length);
        // At the schema bound a document is still read: the padding is JSON whitespace.
        var atBound = new byte[16384];
        Array.Fill(atBound, (byte)0x20);
        exact.CopyTo(atBound, 16384 - exact.Length);

        Assert.IsType<AnonymousSession>(await ProbeFor(ScriptedServer.Always(() => Responses.Json(atBound))).ReadSessionAsync(Never));
        Assert.True(await ReadFailure(ScriptedServer.Always(() => Responses.Json(padded))) == FailureKind.Malformed);

        // A declared length above the bound is refused without reading, and a stream above it is cut off after at most
        // one chunk beyond the bound, for the session and the receipt alike.
        foreach (var bound in new[] { 16384, 4096 })
        {
            var endless = new CountingStream((byte)0x20, 1024);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(endless),
            };
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(Json);
            var handler = ScriptedServer.Always(() => response);
            var failure = await FailureOfBound(handler, bound);
            Assert.True(failure == FailureKind.Malformed, bound.ToString());
            Assert.True(endless.Disposed, bound.ToString());
            Assert.True(endless.BytesYielded > bound, bound.ToString());
            Assert.True(endless.BytesYielded <= bound + 4096, bound.ToString());
        }

        var declared = Responses.Json(exact);
        declared.Content.Headers.Remove("Content-Length");
        declared.Content.Headers.TryAddWithoutValidation("Content-Length", "16385");
        Assert.True(await ReadFailure(ScriptedServer.Always(() => declared)) == FailureKind.Malformed);

        var malformedLength = Responses.Json(exact);
        malformedLength.Content.Headers.Remove("Content-Length");
        malformedLength.Content.Headers.TryAddWithoutValidation("Content-Length", "12e3");
        Assert.True(await ReadFailure(ScriptedServer.Always(() => malformedLength)) == FailureKind.Malformed);
    }

    [Fact]
    public async Task TheStreamBoundIsExactOneByteOverItIsTheFirstByteRefused()
    {
        foreach (var bound in new[] { 16384, 4096 })
        {
            var answer = new CountingStream((byte)0x20, 1);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(answer) };
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(Json);
            var handler = ScriptedServer.Always(() => response);
            Assert.True(await FailureOfBound(handler, bound) == FailureKind.Malformed, bound.ToString());
            Assert.Equal(bound + 1, answer.Reads);
        }
    }

    [Fact]
    public async Task ABodySplitAcrossChunksReadsAsOneDocumentAFailingStreamIsUnavailable()
    {
        var bytes = Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson);
        var chunks = new[] { bytes[..7], bytes[7..90], bytes[90..] };
        var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkedStream(chunks)) };
        answer.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(Json);
        Assert.IsType<AuthenticatedSession>(await ProbeFor(ScriptedServer.Always(() => answer)).ReadSessionAsync(Never));

        var broken = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ChunkedStream([bytes[..20]], new IOException("connection reset"))),
        };
        broken.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(Json);
        Assert.True(await ReadFailure(ScriptedServer.Always(() => broken)) == FailureKind.Unavailable);
    }

    [Fact]
    public async Task NetworkFailureIsUnavailableAndCancellationIsDistinctFromIt()
    {
        var failing = new DelegatingFailure(new HttpRequestException("Failed to fetch"));
        Assert.True(await ReadFailure(failing) == FailureKind.Unavailable);

        using var controller = new CancellationTokenSource();
        var pending = ProbeFor(new NeverAnswers()).ReadSessionAsync(controller.Token);
        await controller.CancelAsync();
        Assert.True(await FailureOf(() => pending) == FailureKind.Cancelled);

        using var already = new CancellationTokenSource();
        await already.CancelAsync();
        Assert.True(await FailureOf(() => ProbeFor(ScriptedServer.Always(() => Responses.Json(Encoding.UTF8.GetBytes(ProbeFixtures.AnonymousJson))))
            .ReadSessionAsync(already.Token)) == FailureKind.Cancelled);
    }

    [Fact]
    public async Task LogoutSendsTheCsrfTokenAsTheOnlyCredentialHeaderWithNoBody()
    {
        var server = ScriptedServer.Always(() => Responses.Json(ProbeFixtures.ReceiptBody("happened")));
        await ProbeFor(server).EndSessionAsync(ProbeFixtures.AuthenticatedCsrf, Never);

        var seen = Assert.Single(server.Requests);
        Assert.Equal($"{ProbeFixtures.Origin.Scheme}://{ProbeFixtures.Origin.Authority}/session/v1/logout", seen.Url.ToString());
        Assert.Equal(HttpMethod.Post, seen.Method);
        // The literal wire name is pinned here, independent of the module constant.
        Assert.Equal("X-AF-CSRF", SessionProbe.CsrfHeader, ignoreCase: false);
        Assert.Equal(ProbeFixtures.AuthenticatedCsrf, seen.CsrfHeader);
        Assert.False(seen.HasAuthorizationHeader);
        Assert.Empty(seen.Body);
    }

    [Fact]
    public async Task LogoutReportsAnEndedSessionARefusedTokenAndAMalformedReceiptDistinctly()
    {
        static Task Run(HttpResponseMessage response) =>
            ProbeFor(ScriptedServer.Always(() => response)).EndSessionAsync("t", CancellationToken.None);

        Assert.True(await FailureOf(() => Run(Responses.Text("{}", HttpStatusCode.Unauthorized))) == FailureKind.Unauthenticated);
        Assert.True(await FailureOf(() => Run(Responses.Text("{}", HttpStatusCode.Forbidden))) == FailureKind.Forbidden);
        Assert.True(await FailureOf(() => Run(Responses.Text("{}", HttpStatusCode.BadRequest))) == FailureKind.Rejected);
        Assert.True(await FailureOf(() => Run(Responses.Text("{}", HttpStatusCode.ServiceUnavailable))) == FailureKind.Unavailable);
        Assert.True(await FailureOf(() => Run(Responses.Json(ProbeFixtures.ReceiptBody("didNotHappen")))) == FailureKind.Rejected);
        Assert.True(await FailureOf(() => Run(Responses.Json(ProbeFixtures.ReceiptBody("unknown")))) == FailureKind.Unexpected);
        Assert.True(await FailureOf(() => Run(Responses.Json(Encoding.UTF8.GetBytes("{}")))) == FailureKind.Malformed);
        Assert.True(await FailureOf(() => Run(Responses.Json(Encoding.UTF8.GetBytes("{\"commandId\":\"x\"}")))) == FailureKind.Malformed);
        Assert.True(await FailureOf(() => Run(Responses.Json(Enumerable.Repeat((byte)0x20, 4097).ToArray()))) == FailureKind.Malformed);
    }

    private static async Task<FailureKind> FailureOfBound(HttpMessageHandler handler, int bound)
    {
        // The session route has the 16 KiB bound; the logout route has the 4 KiB receipt bound.
        if (bound == 16384)
            return await ReadFailure(handler);
        return await FailureOf(() => ProbeFor(handler).EndSessionAsync("t", Never));
    }

    /// <summary>A test-only transport that fails before any answer.</summary>
    private sealed class DelegatingFailure(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(failure);
    }

    /// <summary>A test-only transport that never answers and ends only when the caller cancels, as a stalled fetch does.</summary>
    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
