// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowser;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using Microsoft.JSInterop;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardScopeTests
{
    private const string CliId = "0024h36h2ncsvrh6daqf6dvvqz";
    private const string GuidId = "00112233-4455-6677-8899-aabbccddeeff";
    private const string DeploymentJson = """
        {"Dashboard":{"CenterOrigin":"https://managed.example.test","Mode":"self-hosted","Name":"Managed Center"}}
        """;

    [Fact]
    public void FullCliAndUuidIdentifiersPreserveTheSameBytesAndSignedRevision()
    {
        var expected = Guid.Parse(GuidId);
        Assert.True(DashboardRunTarget.TryCreate(CliId, GuidId, "9223372036854775807", CliId.ToUpperInvariant(), expected, out var target));
        Assert.NotNull(target);
        Assert.Equal(expected, target.NodeId);
        Assert.Equal(expected, target.EnrollmentId);
        Assert.Equal(expected, target.GrantId);
        Assert.Equal(expected, target.SandboxId);
        Assert.Equal(long.MaxValue, target.GrantRevision);
    }

    [Theory]
    [InlineData("00000000000000000000000000", "1")]
    [InlineData("8024h36h2ncsvrh6daqf6dvvqz", "1")]
    [InlineData("0024h36h2ncsvrh6daqf6dvvqz", "01")]
    [InlineData("0024h36h2ncsvrh6daqf6dvvqz", "9223372036854775808")]
    public void IncompleteScopeAndNoncanonicalRevisionsAreRefused(string node, string revision)
    {
        Assert.False(DashboardRunTarget.TryCreate(node, CliId, revision, CliId, Guid.Parse(GuidId), out var target));
        Assert.Null(target);
    }

    [Fact]
    public void OnlyAnExplicitSameOriginDeploymentSuppliesASignInUri()
    {
        var page = new Uri("https://managed.example.test/devices");
        var configuration = DashboardCenterConfiguration.FromDeployment(page, "https://managed.example.test", "self-hosted");
        Assert.Equal(DashboardCenterMode.SelfHosted, configuration.Mode);
        Assert.Equal(new Uri("https://managed.example.test/api/account/sign-in#return=%2Foverview"), configuration.SignInUri);
        foreach (var foreign in new[] { "https://other.example.test", "https://managed.example.test:444", "https://managed.example.test/api", "https://unknown@managed.example.test" })
        {
            var refused = DashboardCenterConfiguration.FromDeployment(page, foreign, "self-hosted");
            Assert.Equal(DashboardCenterMode.Unconfigured, refused.Mode);
            Assert.Null(refused.SignInUri);
        }
    }

    [Fact]
    public async Task TheBootDocumentIsAGetToTheFixedPublicPathOnTheSameOrigin()
    {
        using var transport = ScriptedServer.Always(() => ConfigurationResponse(new StringContent(DeploymentJson, Encoding.UTF8, "application/json")));
        var result = await DashboardConfigurationLoader.ReadAsync(new Uri("https://managed.example.test/devices?show=online"), transport, TestContext.Current.CancellationToken);
        Assert.Equal(DashboardCenterMode.SelfHosted, result.Mode);
        Assert.Equal("Managed Center", result.Name);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(new Uri("https://managed.example.test/appsettings.json"), request.Url);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("application/json", request.AcceptHeader);
        Assert.False(request.HasAuthorizationHeader);
        Assert.False(request.HasCookieHeader);
        Assert.Null(request.CsrfHeader);
        Assert.Empty(request.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.Redirect, null)]
    [InlineData(HttpStatusCode.OK, "{malformed}")]
    [InlineData(HttpStatusCode.OK, "{\"Dashboard\":{\"CenterOrigin\":\"https://managed.example.test\",\"Mode\":\"self-hosted\",\"Name\":\"\\uD800\"}}")]
    [InlineData(HttpStatusCode.OK, "{\"Dashboard\":{\"CenterOrigin\":\"https://managed.example.test\",\"Mode\":\"self-hosted\",\"\\uDC00\":\"invalid name\"}}")]
    [InlineData(HttpStatusCode.OK, "{\"Dashboard\":{\"CenterOrigin\":\"https://other.example.test\",\"Mode\":\"self-hosted\"}}")]
    [InlineData(HttpStatusCode.OK, "{\"Dashboard\":{\"CenterOrigin\":\"https://managed.example.test\",\"Mode\":\"official-hosted\",\"Mode\":\"self-hosted\"}}")]
    public async Task MissingRedirectedMalformedOrForeignSettingsNeverConfigureACenter(HttpStatusCode status, string? json)
    {
        using var transport = ScriptedServer.Always(() =>
        {
            if (json is not null)
                return ConfigurationResponse(new StringContent(json, Encoding.UTF8, "application/json"));
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.Redirect)
                response.Headers.Location = new Uri("https://other.example.test/appsettings.json");
            return response;
        });
        var result = await DashboardConfigurationLoader.ReadAsync(new Uri("https://managed.example.test/"), transport, TestContext.Current.CancellationToken);
        Assert.Equal(DashboardCenterMode.Unconfigured, result.Mode);
        Assert.Null(result.SignInUri);
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizeDeploymentSettingsAreRefusedWithOrWithoutADeclaredLength(bool declared)
    {
        var body = Encoding.UTF8.GetBytes(DeploymentJson.PadRight(4097));
        using var transport = ScriptedServer.Always(() =>
        {
            var content = new StreamContent(new ChunkedStream([body[..4096], body[4096..]]));
            if (declared)
                content.Headers.ContentLength = body.Length;
            return ConfigurationResponse(content);
        });
        var result = await DashboardConfigurationLoader.ReadAsync(new Uri("https://managed.example.test/"), transport, TestContext.Current.CancellationToken);
        Assert.Equal(DashboardCenterMode.Unconfigured, result.Mode);
        Assert.Null(result.Origin);
    }

    private static HttpResponseMessage ConfigurationResponse(HttpContent content)
    {
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public async Task StepUpUsesThePublishedSameOriginRoutesAndTypedJson()
    {
        const string flow = "00112233-4455-6677-8899-aabbccddeeff";
        const string csrf = "browser-step-up-csrf";
        const string target = "management.deviceRevoke";
        var origin = new Uri("https://managed.example.test");
        using var transport = new ScriptedServer((request, _) => Task.FromResult(ConfigurationResponse(new StringContent(
            request.Url.AbsolutePath.EndsWith("/begin", StringComparison.Ordinal)
                ? $$"""{"flowId":"{{flow}}","method":"passkey","challenge":"aW5lcnQ","expiresAt":"2026-12-01T00:00:00Z","purpose":"stepUp"}"""
                : $$"""{"operationClass":"{{target}}","expiresAt":"2026-12-01T00:00:00Z"}""",
            Encoding.UTF8, "application/json"))));
        var probe = new SessionProbe(transport, new ProbeOrigin(origin));
        var token = TestContext.Current.CancellationToken;
        var begin = new WebBrowserBeginStepUpRequest
        {
            OperationClass = target,
            Method = "passkey",
            TargetHash = new string('a', 64),
        };
        var challenge = await ProofCall<WebBrowserAuthChallenge>(probe, "BeginStepUpAsync", begin, csrf, token);
        Assert.Equal(flow, challenge.FlowId);
        Assert.Equal("stepUp", challenge.Purpose);

        var complete = new WebBrowserCompleteStepUpRequest
        {
            FlowId = challenge.FlowId,
            Proof = new WebBrowserAuthProofWebBrowserAuthProofPasskey(new WebBrowserAuthProofPasskey
            {
                Passkey = new WebBrowserWebAuthnAssertion { CredentialId = "AQ", ClientDataJson = "Ag", AuthenticatorData = "Aw", Signature = "BA" },
            }),
        };
        var evidence = await ProofCall<WebBrowserStepUpEvidence>(probe, "CompleteStepUpAsync", complete, csrf, token);
        Assert.Equal(target, evidence.OperationClass);

        Assert.Collection(transport.Requests,
            request => CheckProofRequest(request, origin, "webBrowser.beginStepUp", csrf),
            request => CheckProofRequest(request, origin, "webBrowser.completeStepUp", csrf));
        Assert.Equal(begin, WireJson.Decode(transport.Requests[0].Body, WebBrowserBeginStepUpRequestJsonContext.Default.WebBrowserBeginStepUpRequest));
        Assert.Equal(complete, WireJson.Decode(transport.Requests[1].Body, WebBrowserCompleteStepUpRequestJsonContext.Default.WebBrowserCompleteStepUpRequest));
    }

    [Fact]
    public async Task DashboardBootstrapAndUncertainLogoutUseTheCurrentBrowserApi()
    {
        var origin = new Uri("https://managed.example.test");
        using var transport = new ScriptedServer((request, _) => Task.FromResult(ConfigurationResponse(new ByteArrayContent(
            request.Method == HttpMethod.Get ? Encoding.UTF8.GetBytes(ProbeFixtures.AuthenticatedJson) : ProbeFixtures.ReceiptBody("unknown")))));
        var probe = new SessionProbe(transport, new ProbeOrigin(origin));
        var token = TestContext.Current.CancellationToken;
        var (snapshot, account, session) = await Assert.IsAssignableFrom<Task<(SessionSnapshot, string?, string?)>>(
            ProbeCall(probe, "ReadWebBoundSessionAsync", token));
        Assert.Equal("0f0e6a30-5d1c-4c7e-8b53-5b6b7f9a4a10", account);
        Assert.Equal("6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11", session);
        Assert.Equal("18446744073709551615", Assert.IsType<AuthenticatedSession>(snapshot).RecoveryGeneration);
        var refusal = await Assert.ThrowsAsync<ProbeFailureException>(() => Assert.IsAssignableFrom<Task>(
            ProbeCall(probe, "EndWebSessionAsync", snapshot.CsrfToken, token)));
        Assert.Equal(FailureKind.Unexpected, refusal.Kind);

        Assert.Collection(transport.Requests,
            request => CheckProofRequest(request, origin, "webBrowser.bootstrap", null),
            request => CheckProofRequest(request, origin, "webBrowser.logout", snapshot.CsrfToken));
        Assert.Empty(transport.Requests[1].Body);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(65, false)]
    public async Task ADiscoverablePasskeyPromptAllowsAbsentOrEmptyListsButBoundsExplicitCredentials(int count, bool expectedPrompt)
    {
        var origin = new Uri("https://managed.example.test");
        var challenge = new WebBrowserAuthChallenge
        {
            FlowId = GuidId,
            Method = "passkey",
            Challenge = Convert.ToBase64String(new byte[32]).TrimEnd('='),
            ExpiresAt = "2026-12-01T00:00:00Z",
            Purpose = "stepUp",
            Options = new WebBrowserWebAuthnOptions
            {
                RpId = origin.Host,
                UserVerification = "required",
                Algorithms = [-7],
                AllowedCredentials = count < 0 ? null : Enumerable.Repeat("AQ", count).ToArray(),
            },
        };
        var prompt = new CancelledPasskeyPrompt();
        var passkeys = typeof(IDashboardCenter).Assembly.GetType("ArcForges.Web.App.Dashboard.DashboardPasskeys", throwOnError: true)!;
        var instance = Activator.CreateInstance(passkeys, prompt);
        var proof = passkeys.GetMethod("ProveAsync")?.Invoke(instance, [challenge, origin, TestContext.Current.CancellationToken]);
        var refusal = await Assert.ThrowsAsync<DashboardApiException>(() => Assert.IsAssignableFrom<Task>(proof));
        Assert.Equal(expectedPrompt, prompt.WasCalled);
        Assert.Equal(expectedPrompt ? DashboardFailure.ProofCancelled : DashboardFailure.Incompatible, refusal.Failure);
    }

    private static Task<T> ProofCall<T>(SessionProbe probe, string name, object request, string csrf, CancellationToken token)
        => Assert.IsAssignableFrom<Task<T>>(ProbeCall(probe, name, request, csrf, token));

    private static object? ProbeCall(SessionProbe probe, string name, params object?[] arguments) =>
        typeof(SessionProbe).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(probe, arguments);

    private static void CheckProofRequest(CapturedRequest request, Uri origin, string id, string? csrf)
    {
        var route = Assert.Single(WebBrowserSessionRoutes.All, candidate => candidate.Id == id);
        Assert.Equal(new Uri(origin, "/api" + route.Path), request.Url);
        Assert.Equal(route.Method, request.Method.Method);
        Assert.Equal(csrf, request.CsrfHeader);
        Assert.False(request.HasAuthorizationHeader);
    }

    private sealed class CancelledPasskeyPrompt : IJSRuntime
    {
        public bool WasCalled { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("The Dashboard should pass its cancellation token to the browser.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("navigator.credentials.get", identifier);
            WasCalled = true;
            return ValueTask.FromException<TValue>(new JSException("The fixture dismissed the passkey prompt."));
        }
    }
}
