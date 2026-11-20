// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Tests.TestDoubles;
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
}
