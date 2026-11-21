// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using ArcForges.Contracts.Management.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using ArcForges.Web.App.Pages;
using ArcForges.Web.App.Tests.TestDoubles;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardWorkScopeTests
{
    private const string InspectDevice = "/arcforges.management.v2.DeviceService/Inspect";

    [Fact]
    public async Task ScopeSelectionKeepsTheExactSandboxGrantAndAllowsReadOnlySelectionWithoutAnEpoch()
    {
        using var server = WorkFixture.Server((captured, _) =>
        {
            var request = WorkFixture.Read(captured, InspectDevice, DeviceServiceInspectRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.DeviceId), request.DeviceId);
            return Task.FromResult(DeviceReply(request, "Selected device", "online"));
        });
        using var context = CreateContext(server);
        DashboardWorkTarget? selected = null;
        var view = context.Render<DashboardLayout>(parameters => parameters.Add<DashboardWorkScopeView>(layout => layout.Body, scope => scope
            .Add(component => component.DeviceId, WorkFixture.DeviceId)
            .Add(component => component.SandboxId, WorkFixture.SandboxId)
            .Add<DashboardWorkTarget>(component => component.ChildContent, target =>
            {
                selected = target;
                return "Fixture scoped content";
            })));
        view.WaitForAssertion(() => Assert.Contains("on Selected device", view.Markup, StringComparison.Ordinal));

        await view.InvokeAsync(() =>
        {
            Assert.Equal(4, view.FindAll(".dashboard-work-scope input").Count);
            view.FindAll(".dashboard-work-scope input")[0].Input(WorkFixture.NodeId.ToString("D"));
            view.FindAll(".dashboard-work-scope input")[1].Input(WorkFixture.GrantId.ToString("D"));
            view.FindAll(".dashboard-work-scope input")[2].Input("01");
            view.Find(".dashboard-work-scope form").Submit();
        });
        Assert.Null(selected);
        Assert.Contains("exact positive grant revision", view.Markup, StringComparison.Ordinal);

        await view.InvokeAsync(() =>
        {
            view.FindAll(".dashboard-work-scope input")[2].Input(WorkFixture.Target.GrantRevision.ToString(CultureInfo.InvariantCulture));
            view.Find(".dashboard-work-scope form").Submit();
        });

        Assert.Equal(WorkFixture.Target with { OwnerEpoch = null }, selected);
        Assert.Contains("Read-only selection", view.Markup, StringComparison.Ordinal);
        Assert.Contains("Fixture scoped content", view.Markup, StringComparison.Ordinal);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ChangingDeviceCancelsTheOldReadAndDiscardsItsLateOfflineResult()
    {
        var token = Xunit.TestContext.Current.CancellationToken;
        var nextId = Guid.Parse("2151dc2a-7684-4a69-be91-2142a63ff8e9");
        var oldRead = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = WorkFixture.Server(async (captured, cancellation) =>
        {
            var request = WorkFixture.Read(captured, InspectDevice, DeviceServiceInspectRequest.Parser);
            if (request.DeviceId.Equals(WorkFixture.Id(WorkFixture.DeviceId)))
            {
                oldRead.SetResult(cancellation);
                await releaseOld.Task;
                oldReturned.SetResult();
                return DeviceReply(request, "Previous offline device", "offline");
            }

            Assert.Equal(WorkFixture.Id(nextId), request.DeviceId);
            return DeviceReply(request, "Replacement device", "online");
        });
        using var context = CreateContext(server);
        var view = context.Render<DashboardLayout>(parameters => UseDevice(parameters, WorkFixture.DeviceId));
        try
        {
            var canceledRead = await oldRead.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            view.Render(parameters => UseDevice(parameters, nextId));
            view.WaitForAssertion(() => Assert.Contains("on Replacement device", view.Markup, StringComparison.Ordinal));
            Assert.True(canceledRead.IsCancellationRequested);

            releaseOld.SetResult();
            await oldReturned.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            await view.InvokeAsync(() => Task.CompletedTask);

            Assert.Contains("on Replacement device", view.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Reconnect the device", view.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Previous offline device", view.Markup, StringComparison.Ordinal);
            Assert.Equal(2, server.Requests.Count(request => request.Method == HttpMethod.Post));
        }
        finally
        {
            releaseOld.TrySetResult();
        }
    }

    private static void UseDevice(ComponentParameterCollectionBuilder<DashboardLayout> parameters, Guid deviceId) =>
        parameters.Add<DashboardWorkScopeView>(layout => layout.Body, scope => scope
            .Add(component => component.DeviceId, deviceId)
            .Add<DashboardWorkTarget>(component => component.ChildContent, _ => "Fixture scoped content"));

    private static BunitContext CreateContext(ScriptedServer server)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(WorkFixture.Center(server));
        return context;
    }

    private static HttpResponseMessage DeviceReply(DeviceServiceInspectRequest request, string name, string presence) =>
        WorkFixture.Reply(new DeviceServiceInspectResponse
        {
            Meta = WorkFixture.Meta(request.Meta, WorkFixture.Revision),
            Value = new()
            {
                Device = new()
                {
                    DeviceId = request.DeviceId,
                    EnrollmentId = WorkFixture.Id(WorkFixture.EnrollmentId),
                    DisplayName = name,
                    Revision = new() { Value = WorkFixture.Revision },
                    Presence = presence,
                    NodeEpoch = "fixture-device-epoch",
                },
            },
        });
}
