// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Pages;
using Bunit;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardSecurityTests
{
    private const string ListSessions = "/arcforges.publicapi.v1.IdentityService/ListSessions";
    private const string RevokeSession = "/arcforges.publicapi.v1.IdentityService/RevokeSession";
    private const string ListCredentials = "/arcforges.publicapi.v1.IdentityService/ListAuthIdentities";
    private const string ListGrants = "/arcforges.sandbox.v2.NodeService/ListRemoteGrants";
    private const string RevokeGrant = "/arcforges.sandbox.v2.NodeService/RevokeRemoteGrant";
    private static readonly Guid CurrentSession = Guid.Parse("6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11");
    private static readonly Guid OtherSession = Guid.Parse("47667112-2898-4be3-a4db-34d9c877eb24");

    [Fact]
    public async Task LostSessionRevocationKeepsItsExactCommandAndAbsenceDoesNotResolveIt()
    {
        IdentityServiceRevokeSessionRequest? original = null;
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            if (captured.Url.AbsolutePath.EndsWith(ListSessions, StringComparison.Ordinal))
            {
                var list = WorkFixture.Read(captured, ListSessions, IdentityServiceListSessionsRequest.Parser);
                var sessions = new IdentityServiceListSessionsValue { Page = new() };
                sessions.Sessions.Add(Session(CurrentSession, "Current fixture browser"));
                if (original is null)
                    sessions.Sessions.Add(Session(OtherSession, "Other fixture browser"));
                return Task.FromResult(WorkFixture.Reply(new IdentityServiceListSessionsResponse
                {
                    Meta = WorkFixture.Meta(list.Meta),
                    Value = sessions,
                }));
            }

            var revoke = WorkFixture.Read(captured, RevokeSession, IdentityServiceRevokeSessionRequest.Parser);
            Assert.Null(original);
            Assert.Equal(WorkFixture.Id(OtherSession), revoke.SessionId);
            Assert.Equal(16, revoke.Meta.CommandId.Value.Length);
            Assert.Equal(ulong.MaxValue, revoke.Meta.RecoveryGeneration);
            original = revoke;
            throw new HttpRequestException("Fixture lost the revocation response.");
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardSecurity)]);
        var view = DashboardLifecycleFixture.Render<SecuritySessions>(context);
        view.WaitForAssertion(() => Assert.Contains("Other fixture browser", view.Markup, StringComparison.Ordinal));
        Assert.Contains("Use Sign out", view.Markup, StringComparison.Ordinal);
        Assert.Single(view.FindAll("dialog"));

        await DashboardLifecycleFixture.ClickAsync(view, "Confirm revocation");
        view.WaitForAssertion(() => Assert.Contains("Session revocation result unknown", view.Markup, StringComparison.Ordinal));
        Assert.NotNull(original);
        Assert.Contains(new Guid(original.Meta.CommandId.Value.Span, bigEndian: true).ToString("D"), view.Markup, StringComparison.Ordinal);

        await DashboardLifecycleFixture.ClickAsync(view, "Refresh");

        view.WaitForAssertion(() => Assert.DoesNotContain("Other fixture browser", view.Markup, StringComparison.Ordinal));
        Assert.Contains("An absent session can also have expired", view.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmed session revocation", view.Markup, StringComparison.Ordinal);
        Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(RevokeSession, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthorityRefusalClearsPreviouslyRenderedCredentialDetails()
    {
        int reads = 0;
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            var request = WorkFixture.Read(captured, ListCredentials, IdentityServiceListAuthIdentitiesRequest.Parser);
            var answer = new IdentityServiceListAuthIdentitiesResponse { Meta = WorkFixture.Meta(request.Meta) };
            if (++reads == 1)
            {
                answer.Value = new()
                {
                    Page = new(),
                    Items =
                    {
                        new CredentialSummary { CredentialId = WorkFixture.Id(WorkFixture.GrantId), Kind = "passkey", DisplayName = "Fixture method" },
                    },
                };
            }
            else
            {
                answer.Error = new ArcError { Category = ErrorCategory.Authorization, Code = "permission_denied", Effect = EffectCertainty.DidNotHappen };
            }
            return Task.FromResult(WorkFixture.Reply(answer));
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardSecurity)]);
        var view = DashboardLifecycleFixture.Render<SecurityCredentials>(context);
        view.WaitForAssertion(() => Assert.Contains("Fixture method", view.Markup, StringComparison.Ordinal));

        await DashboardLifecycleFixture.ClickAsync(view, "Refresh");

        view.WaitForAssertion(() => Assert.Contains("does not allow this action", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Fixture method", view.Markup, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("table"));
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(GrantState.Active)]
    [InlineData(GrantState.Revoked)]
    public void ReadOnlyOrRevokedGrantHasNoMutationAndDirectsExpansionToTheNode(GrantState state)
    {
        var target = state == GrantState.Active ? WorkFixture.Target with { OwnerEpoch = null } : WorkFixture.Target;
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            var request = WorkFixture.Read(captured, ListGrants, NodeServiceListRemoteGrantsRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.SandboxId), request.SandboxId);
            Assert.Null(request.Meta.CommandId);
            var response = new NodeServiceListRemoteGrantsResponse
            {
                Meta = WorkFixture.Meta(request.Meta),
                Value = new()
                {
                    Page = new(),
                    Grants =
                    {
                        new RemoteGrant
                        {
                            GrantId = WorkFixture.Id(target.GrantId), EnrollmentId = WorkFixture.Id(target.EnrollmentId),
                            SandboxId = request.SandboxId, Revision = new() { Value = target.GrantRevision },
                            State = state, ScopeKind = "sandbox", Methods = { RevokeGrant },
                        },
                    },
                },
            };
            return Task.FromResult(WorkFixture.Reply(response));
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardSecurity), typeof(IDashboardWorkspaces)]);
        var view = DashboardLifecycleFixture.Render<SecurityGrant>(context, parameters => parameters.Add(component => component.Target, target));

        view.WaitForAssertion(() => Assert.Contains(target.GrantId.ToString("D"), view.Markup, StringComparison.Ordinal));
        Assert.Contains(view.FindAll("section[aria-labelledby='dashboard-security-selected-grant'] code"),
            code => code.TextContent.StartsWith("arcforges grant set --file grant.json --expected-revision ", StringComparison.Ordinal));
        Assert.DoesNotContain(view.FindAll("button"), button => button.TextContent.Trim() == "Revoke grant");
        if (state == GrantState.Active)
        {
            Assert.Contains("Supply the Node owner epoch", view.Markup, StringComparison.Ordinal);
            Assert.True(view.Find("dialog button[value=revoke]").HasAttribute("disabled"));
        }
        else
        {
            Assert.Contains("Revoked", view.Markup, StringComparison.Ordinal);
            Assert.Empty(view.FindAll("dialog"));
        }
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Post);
    }

    private static SessionSummary Session(Guid id, string product) => new()
    {
        SessionId = WorkFixture.Id(id),
        DeviceId = WorkFixture.Id(WorkFixture.DeviceId),
        Product = product,
        Revision = new() { Value = WorkFixture.Revision },
        Current = false,
    };
}
