// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowser;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using ArcForges.Web.App.Pages;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardAccountLifecycleTests
{
    private const string GetProfile = "/arcforges.publicapi.v1.IdentityService/GetProfile";
    private const string UpdateProfile = "/arcforges.publicapi.v1.IdentityService/UpdateProfile";
    private const string GetDeletion = "/arcforges.publicapi.v1.IdentityService/GetAccountDeletion";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoCurrentSessionKeepsLifecycleControlsBehindAuthentication(bool expired)
    {
        using var server = DashboardLifecycleFixture.Server(
            (_, _) => throw new InvalidOperationException("An unauthenticated Account page reached an account RPC."),
            () => DashboardLifecycleFixture.Bootstrap(authenticated: expired, expired: expired));
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardAccount)]);
        var view = DashboardLifecycleFixture.Render<Account>(context);

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".dashboard-session a")));
        Assert.Empty(view.FindAll(".dashboard-main form"));
        Assert.DoesNotContain("Ada Lovelace", view.Markup, StringComparison.Ordinal);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task UnconfirmedProfileUpdateCanOnlyRetryTheOriginalCommandAndExactRevision()
    {
        IdentityServiceUpdateProfileRequest? original = null;
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            if (captured.Url.AbsolutePath.EndsWith(GetProfile, StringComparison.Ordinal))
                return Task.FromResult(Profile(captured));
            if (captured.Url.AbsolutePath.EndsWith(GetDeletion, StringComparison.Ordinal))
            {
                var read = WorkFixture.Read(captured, GetDeletion, IdentityServiceGetAccountDeletionRequest.Parser);
                return Task.FromResult(WorkFixture.Reply(new IdentityServiceGetAccountDeletionResponse
                {
                    Meta = WorkFixture.Meta(read.Meta),
                    Error = new ArcError { Category = ErrorCategory.State, Code = "not_found", Effect = EffectCertainty.DidNotHappen },
                }));
            }

            var update = WorkFixture.Read(captured, UpdateProfile, IdentityServiceUpdateProfileRequest.Parser);
            Assert.Equal(16, update.Meta.CommandId.Value.Length);
            Assert.Equal(WorkFixture.Revision, update.Meta.ExpectedRev.Value);
            Assert.Equal(ulong.MaxValue, update.Meta.RecoveryGeneration);
            Assert.Equal(new ProfileUpdate { DisplayName = "Edited fixture" }, update.Patch);
            if (original is null)
            {
                original = update;
                throw new HttpRequestException("Fixture lost the response for this profile command.");
            }

            Assert.Equal(original.Meta.CommandId, update.Meta.CommandId);
            Assert.Equal(original.Meta.ExpectedRev, update.Meta.ExpectedRev);
            Assert.Equal(original.Patch, update.Patch);
            return Task.FromResult(WorkFixture.Reply(new IdentityServiceUpdateProfileResponse
            {
                Meta = WorkFixture.Meta(update.Meta),
                Error = new ArcError { Category = ErrorCategory.Conflict, Code = "revision_conflict", Effect = EffectCertainty.DidNotHappen },
            }));
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardAccount)]);
        var view = DashboardLifecycleFixture.Render<Account>(context);
        const string form = "section[aria-labelledby='account-profile-heading'] form";
        view.WaitForAssertion(() =>
        {
            Assert.NotEmpty(view.FindAll(form));
            Assert.Contains("No account deletion operation is recorded", view.Markup, StringComparison.Ordinal);
        });
        await view.InvokeAsync(() =>
        {
            view.Find(form + " input").Input("Edited fixture");
            view.Find(form).Submit();
        });

        view.WaitForAssertion(() => Assert.Contains("Retry original save", view.Markup, StringComparison.Ordinal));
        Assert.True(view.Find(form + " button[type=submit]").HasAttribute("disabled"));
        Assert.Single(server.Requests, request => request.Url.AbsolutePath == "/api" + UpdateProfile);
        await DashboardLifecycleFixture.ClickAsync(view, "Retry original save");

        view.WaitForAssertion(() => Assert.Contains("Refresh it before saving new edits", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Retry original save", view.Markup, StringComparison.Ordinal);
        Assert.True(view.Find(form + " button[type=submit]").HasAttribute("disabled"));
        Assert.Equal(2, server.Requests.Count(request => request.Url.AbsolutePath == "/api" + UpdateProfile));
    }

    [Fact]
    public void PendingDeletionRendersSameOriginProofEntryAndKeepsProfileMutationClosed()
    {
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            if (captured.Url.AbsolutePath.EndsWith(GetProfile, StringComparison.Ordinal))
                return Task.FromResult(Profile(captured));

            var read = WorkFixture.Read(captured, GetDeletion, IdentityServiceGetAccountDeletionRequest.Parser);
            return Task.FromResult(WorkFixture.Reply(new IdentityServiceGetAccountDeletionResponse
            {
                Meta = WorkFixture.Meta(read.Meta),
                Value = new()
                {
                    Deletion = new() { DeletionId = WorkFixture.Id(WorkFixture.GrantId), State = "processing" },
                },
            }));
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardAccount)]);
        var view = DashboardLifecycleFixture.Render<Account>(context);

        view.WaitForAssertion(() =>
        {
            Assert.Contains("Deletion pending", view.Find("section[aria-labelledby='account-lifecycle-heading'] [role=status]").TextContent,
                StringComparison.Ordinal);
            Assert.NotEmpty(view.FindAll("section[aria-labelledby='account-profile-heading'] button[type=submit]"));
        });
        Assert.Contains("Local Sandboxes, files and running processes remain", view.Markup, StringComparison.Ordinal);
        Assert.Single(view.FindAll($"a[href='{new Uri(DashboardLifecycleFixture.Origin, "/api/account/lifecycle")}']"));
        Assert.Single(view.FindAll($"a[href='{new Uri(DashboardLifecycleFixture.Origin, "/api/account/recovery")}']"));
        Assert.True(view.Find("section[aria-labelledby='account-profile-heading'] button[type=submit]").HasAttribute("disabled"));
        Assert.Equal(2, server.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    private static HttpResponseMessage Profile(CapturedRequest request)
    {
        var read = WorkFixture.Read(request, GetProfile, IdentityServiceGetProfileRequest.Parser);
        return WorkFixture.Reply(new IdentityServiceGetProfileResponse
        {
            Meta = WorkFixture.Meta(read.Meta, WorkFixture.Revision),
            Value = new()
            {
                Profile = new()
                {
                    DisplayName = "Original fixture",
                    Locale = "en-GB",
                    Timezone = "Europe/London",
                    Revision = new() { Value = WorkFixture.Revision },
                },
            },
        });
    }
}

internal static class DashboardLifecycleFixture
{
    internal static readonly Uri Origin = new("https://manage.example.test/");
    private static readonly string BootstrapPath = "/api" +
        WebBrowserSessionRoutes.All.Single(route => route.Id == "webBrowser.bootstrap").Path;

    internal static ScriptedServer Server(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> routes,
        Func<HttpResponseMessage>? bootstrap = null) => new((request, token) =>
    {
        if (request.Url != new Uri(Origin, BootstrapPath))
            return routes(request, token);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.CsrfHeader);
        Assert.False(request.HasAuthorizationHeader);
        Assert.Empty(request.Body);
        return Task.FromResult(bootstrap?.Invoke() ?? Bootstrap());
    });

    internal static HttpResponseMessage Bootstrap(bool authenticated = true, bool expired = false)
    {
        if (!authenticated)
            return Responses.Json(ProbeFixtures.Bytes(ProbeFixtures.AnonymousNode()));

        var document = ProbeFixtures.AuthenticatedNode();
        var now = TimeProvider.System.GetUtcNow();
        document["session"]!["expiresAt"] = Format(now.AddHours(2));
        document["session"]!["idleExpiresAt"] = Format(now.AddHours(expired ? -1 : 1));
        return Responses.Json(ProbeFixtures.Bytes(document));
    }

    internal static BunitContext Context(ScriptedServer server, string mode = "self-hosted", params Type[] services)
    {
        var center = Center(server, mode);
        var context = new BunitContext();
        context.Services.AddSingleton(center);
        context.Services.AddSingleton(TimeProvider.System);
        foreach (var service in services)
        {
            Assert.True(service.IsInstanceOfType(center));
            context.Services.AddSingleton(service, center);
        }
        return context;
    }

    internal static IDashboardCenter Center(ScriptedServer server, string mode = "self-hosted")
    {
        var configuration = DashboardCenterConfiguration.FromDeployment(Origin, Origin.AbsoluteUri, mode);
        return Assert.IsAssignableFrom<IDashboardCenter>(Construct("DashboardCenter", configuration,
            new SessionProbe(server, new ProbeOrigin(Origin)), Construct("DashboardGrpcTransport", server),
            Construct("DashboardPasskeys", new UnexpectedJavaScript()), TimeProvider.System));
    }

    internal static IRenderedComponent<DashboardLayout> Render<T>(BunitContext context,
        Action<ComponentParameterCollectionBuilder<T>>? configure = null) where T : IComponent =>
        context.Render<DashboardLayout>(parameters => parameters.Add<T>(layout => layout.Body, configure ?? (_ => { })));

    internal static Task ClickAsync(IRenderedComponent<DashboardLayout> view, string label) =>
        view.InvokeAsync(() => Assert.Single(view.FindAll("button"), button => button.TextContent.Trim() == label)
            .ClickAsync(new MouseEventArgs()));

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static object Construct(string name, params object[] arguments) => Activator.CreateInstance(
        typeof(IDashboardCenter).Assembly.GetType("ArcForges.Web.App.Dashboard." + name, throwOnError: true)!, arguments)!;

    private sealed class UnexpectedJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException(identifier);
    }
}
