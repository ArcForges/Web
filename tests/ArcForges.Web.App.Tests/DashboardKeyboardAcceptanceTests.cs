// SPDX-License-Identifier: AGPL-3.0-only
using AngleSharp.Dom;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Pages;
using ArcForges.Web.App.Tests.TestDoubles;
using Bunit;
using Xunit;
using Management = ArcForges.Contracts.Management.V2;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardKeyboardAcceptanceTests
{
    private static readonly Guid OtherSession = Guid.Parse("0889a050-c211-4613-af17-b04caec8a0c1");

    [Theory]
    [InlineData("device", "dashboard-revoke-confirm", "Revoke device access?")]
    [InlineData("run", "dashboard-stop-run-confirm", "Stop this Run?")]
    [InlineData("grant", "security-revoke-grant", "Revoke this grant?")]
    [InlineData("session", "security-revoke-session-0889a050c2114613af17b04caec8a0c1", "Revoke this session?")]
    public void DestructiveControlNamesItsDialogAndTargetAndDefaultsToCancel(string scope, string dialogId, string title)
    {
        using var server = DashboardLifecycleFixture.Server((captured, _) => Task.FromResult(Inspect(scope, captured)));
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardWorkspaces), typeof(IDashboardSecurity)]);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = scope switch
        {
            "device" => DashboardLifecycleFixture.Render<DeviceDetails>(context, parameters =>
                parameters.Add(component => component.DeviceId, WorkFixture.DeviceId)),
            "run" => DashboardLifecycleFixture.Render<RunDetailsView>(context, parameters => parameters
                .Add(component => component.Target, WorkFixture.Target)
                .Add(component => component.DeviceId, WorkFixture.DeviceId)
                .Add(component => component.RunId, WorkFixture.RunId)),
            "grant" => DashboardLifecycleFixture.Render<SecurityGrant>(context, parameters =>
                parameters.Add(component => component.Target, WorkFixture.Target)),
            "session" => DashboardLifecycleFixture.Render<SecuritySessions>(context),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll($"dialog[id='{dialogId}']")));
        var opener = view.Find($"button[aria-controls='{dialogId}']");
        Assert.Equal("dialog", opener.GetAttribute("aria-haspopup"));
        Assert.Equal("show-modal", opener.GetAttribute("command"));
        Assert.Equal(dialogId, opener.GetAttribute("commandfor"));
        Assert.False(opener.HasAttribute("disabled"));

        var dialog = view.Find($"dialog[id='{dialogId}']");
        Assert.Equal("closerequest", dialog.GetAttribute("closedby"));
        var label = dialog.GetAttribute("aria-labelledby");
        var description = dialog.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.Equal(title, dialog.QuerySelector($"[id='{label}']")?.TextContent.Trim());
        Assert.False(string.IsNullOrWhiteSpace(dialog.QuerySelector($"[id='{description}']")?.TextContent));
        var form = Assert.Single(dialog.QuerySelectorAll("form[method=dialog]"));
        var cancel = Assert.Single(form.QuerySelectorAll("button[autofocus]"));
        Assert.Equal("Cancel", cancel.TextContent.Trim());
        Assert.False(cancel.HasAttribute("disabled"));
        Assert.Equal("submit", cancel.GetAttribute("type"));

        var target = scope switch
        {
            "device" => WorkFixture.DeviceId,
            "run" => WorkFixture.RunId,
            "grant" => WorkFixture.GrantId,
            _ => OtherSession,
        };
        Assert.Contains(target.ToString("D"), dialog.TextContent, StringComparison.Ordinal);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Post);
    }

    private static HttpResponseMessage Inspect(string scope, CapturedRequest captured)
    {
        if (scope == "device")
        {
            var inspect = WorkFixture.Read(captured, "/arcforges.management.v2.DeviceService/Inspect", Management.DeviceServiceInspectRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.DeviceId), inspect.DeviceId);
            return WorkFixture.Reply(new Management.DeviceServiceInspectResponse
            {
                Meta = WorkFixture.Meta(inspect.Meta, WorkFixture.Revision),
                Value = new()
                {
                    Device = new()
                    {
                        DeviceId = inspect.DeviceId,
                        EnrollmentId = WorkFixture.Id(WorkFixture.EnrollmentId),
                        DisplayName = "Fixture workstation",
                        Revision = new() { Value = WorkFixture.Revision },
                        Presence = "online",
                        NodeEpoch = "fixture-device-epoch",
                    },
                },
            });
        }

        if (scope == "run")
        {
            var inspect = WorkFixture.Read(captured, "/arcforges.sandbox.v2.RunService/Inspect", RunServiceInspectRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.RunId), inspect.RunId);
            return WorkFixture.Reply(new RunServiceInspectResponse
            {
                Meta = WorkFixture.Meta(inspect.Meta, WorkFixture.Revision),
                Value = new() { Run = WorkFixture.Run() },
            });
        }

        if (scope == "grant")
        {
            var list = WorkFixture.Read(captured, "/arcforges.sandbox.v2.NodeService/ListRemoteGrants", NodeServiceListRemoteGrantsRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.SandboxId), list.SandboxId);
            return WorkFixture.Reply(new NodeServiceListRemoteGrantsResponse
            {
                Meta = WorkFixture.Meta(list.Meta),
                Value = new()
                {
                    Page = new(),
                    Grants =
                    {
                        new RemoteGrant
                        {
                            GrantId = WorkFixture.Id(WorkFixture.GrantId), EnrollmentId = WorkFixture.Id(WorkFixture.EnrollmentId),
                            SandboxId = list.SandboxId, Revision = new() { Value = WorkFixture.Target.GrantRevision },
                            State = GrantState.Active, ScopeKind = "sandbox", Methods = { "/arcforges.sandbox.v2.NodeService/RevokeRemoteGrant" },
                        },
                    },
                },
            });
        }

        var sessions = WorkFixture.Read(captured, "/arcforges.publicapi.v1.IdentityService/ListSessions", IdentityServiceListSessionsRequest.Parser);
        return WorkFixture.Reply(new IdentityServiceListSessionsResponse
        {
            Meta = WorkFixture.Meta(sessions.Meta),
            Value = new()
            {
                Page = new(),
                Sessions =
                {
                    new SessionSummary
                    {
                        SessionId = WorkFixture.Id(OtherSession), DeviceId = WorkFixture.Id(WorkFixture.DeviceId), Product = "Other fixture browser",
                        Revision = new() { Value = WorkFixture.Revision }, Current = false,
                    },
                },
            },
        });
    }
}
