// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using ArcForges.Web.App.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardViewTests
{
    private static readonly Guid DeviceId = Guid.Parse("fc096a30-64b3-4971-843e-fb209f73a798");
    private static readonly Guid EnrollmentId = Guid.Parse("86210f3f-a613-47ec-9ad1-89a8d9751482");
    private static readonly Guid NodeId = Guid.Parse("aa24bdcc-5e62-4caf-af76-72561b7b28c7");
    private static readonly Guid GrantId = Guid.Parse("b9864996-c3cf-4998-a12b-4e0c6126d55d");
    private static readonly Guid SandboxId = Guid.Parse("5aac84da-7da7-469b-9e52-96435d7734e6");
    private static readonly DateTimeOffset Observed = new(2026, 11, 16, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResumingWithAnotherAccountDiscardsLateReadsFromThePreviousAccount()
    {
        var center = new DashboardFixture();
        var activity = new DashboardActivityFixture();
        using var context = CreateContext(center);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDashboardActivity>(activity);
        var view = context.Render<DashboardLayout>(parameters => parameters.Add<Devices>(layout => layout.Body, _ => { }));
        view.WaitForAssertion(() => Assert.Contains("Workstation", view.Markup, StringComparison.Ordinal));

        var pending = new TaskCompletionSource<DashboardDevicePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        center.NextRegistry = pending.Task;
        var late = view.Find(".dashboard-intro button").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.True(center.SlowRead.CanBeCanceled));

        var priorDevice = center.Device;
        center.Session = center.Session with { AccountId = Guid.NewGuid(), SessionId = Guid.NewGuid(), RecoveryGeneration = 2 };
        center.Device = center.Device with { Id = Guid.NewGuid(), Name = "New account device" };
        await view.InvokeAsync(activity.Signal);
        view.WaitForAssertion(() => Assert.Contains("New account device", view.Markup, StringComparison.Ordinal));
        Assert.True(center.SlowRead.IsCancellationRequested);

        pending.SetResult(new DashboardDevicePage([priorDevice with { Name = "Old account late reply" }], null));
        await late;
        Assert.DoesNotContain("Old account late reply", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOverlappingResumeKeepsProtectedContentHiddenUntilTheLatestCookieIsVerified()
    {
        var center = new DashboardFixture();
        var activity = new DashboardActivityFixture();
        using var context = CreateContext(center);
        context.Services.AddSingleton<IDashboardActivity>(activity);
        var view = context.Render<DashboardLayout>(parameters => parameters.Add<Devices>(layout => layout.Body, _ => { }));
        view.WaitForAssertion(() => Assert.Contains("Workstation", view.Markup, StringComparison.Ordinal));

        var first = new TaskCompletionSource<DashboardSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new TaskCompletionSource<DashboardSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = center.DelaySessionRead(first.Task);
        var latestStarted = center.DelaySessionRead(latest.Task);
        await view.InvokeAsync(activity.Signal);
        await firstStarted.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        view.WaitForAssertion(() => Assert.True(view.Find(".dashboard-main > div").HasAttribute("hidden")));
        Assert.Equal(2, center.SessionReads);

        await view.InvokeAsync(() =>
        {
            activity.Signal();
            activity.Signal();
        });
        Assert.Equal(2, center.SessionReads);

        first.SetResult(center.Session with { DisplayName = "Superseded owner" });
        await latestStarted.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(3, center.SessionReads);
        Assert.True(view.Find(".dashboard-main > div").HasAttribute("hidden"));
        Assert.DoesNotContain("Superseded owner", view.Markup, StringComparison.Ordinal);

        center.Device = center.Device with { Id = Guid.NewGuid(), Name = "New account device" };
        latest.SetResult(center.Session with { AccountId = Guid.NewGuid(), SessionId = Guid.NewGuid(), RecoveryGeneration = 2 });
        view.WaitForAssertion(() => Assert.Contains("New account device", view.Markup, StringComparison.Ordinal));
        Assert.False(view.Find(".dashboard-main > div").HasAttribute("hidden"));
        Assert.DoesNotContain("Workstation", view.Markup, StringComparison.Ordinal);
        Assert.Equal(3, center.SessionReads);
    }

    [Fact]
    public void OnlyTheDashboardRoutesAreReachableInTheNewApp()
    {
        var routes = typeof(Overview).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(route => route.Template)
            .OrderBy(template => template, StringComparer.Ordinal);

        Assert.Equal(new[]
        {
            "/devices",
            "/devices/{DeviceId:guid}",
            "/devices/{DeviceId:guid}/resources",
            "/devices/{DeviceId:guid}/runs",
            "/devices/{DeviceId:guid}/runs/{RunId:guid}",
            "/devices/{DeviceId:guid}/sandboxes",
            "/devices/{DeviceId:guid}/sandboxes/{SandboxId:guid}",
            "/overview",
        }, routes);
    }

    [Fact]
    public void ExpiredSessionHidesTheRegistryAndKeepsSignInOnTheConfiguredCenter()
    {
        var center = new DashboardFixture
        {
            Session = new DashboardSession(true, "Owner", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddMinutes(-1)),
        };
        using var context = CreateContext(center);
        var view = context.Render<DashboardLayout>(parameters => parameters.Add<Devices>(layout => layout.Body, _ => { }));

        view.WaitForAssertion(() => Assert.Equal("Session expired", view.Find("h1").TextContent.Trim()));
        Assert.Empty(view.FindAll(".dashboard-table"));
        Assert.Equal("https://center.example.org/api/account/sign-in#return=%2Foverview",
            view.Find(".dashboard-session a").GetAttribute("href"));
        Assert.Equal(0, center.RegistryReads);
    }

    [Fact]
    public async Task DeviceReadNeedsAnExactAuthorizedScopeBeforeItCanShowAnEmptySandbox()
    {
        var center = new DashboardFixture();
        using var context = CreateContext(center);
        var view = RenderDevice(context);

        view.WaitForAssertion(() => Assert.Contains("Run data unavailable", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("No runs in this Sandbox", view.Markup, StringComparison.Ordinal);
        Assert.Null(Assert.Single(center.RunReads));

        await FillScopeAsync(0, NodeId.ToString("D"));
        await FillScopeAsync(1, SandboxId.ToString("D"));
        await FillScopeAsync(2, GrantId.ToString("D"));
        await FillScopeAsync(3, "9007199254740993");
        await view.InvokeAsync(() => view.Find(".dashboard-run-fields").Submit());

        view.WaitForAssertion(() => Assert.Contains("No runs in this Sandbox", view.Markup, StringComparison.Ordinal));
        Assert.Equal(new DashboardRunTarget(NodeId, EnrollmentId, GrantId, 9007199254740993, SandboxId), center.RunReads[1]);

        center.Device = center.Device with { Presence = DashboardDevicePresence.Offline };
        view.Find(".dashboard-detail-intro button").Click();

        view.WaitForAssertion(() => Assert.Contains("Device offline", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("No runs in this Sandbox", view.Markup, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".dashboard-run-fields"));

        center.Device = center.Device with { Presence = DashboardDevicePresence.Online, NodeEpoch = "replacement-epoch" };
        view.Find(".dashboard-detail-intro button").Click();

        view.WaitForAssertion(() => Assert.Contains("Run data unavailable", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("No runs in this Sandbox", view.Markup, StringComparison.Ordinal);
        Assert.Single(center.RunReads, scope => scope is not null);

        Task FillScopeAsync(int index, string value) =>
            view.InvokeAsync(() => view.FindAll(".dashboard-run-fields input")[index].Input(value));
    }

    [Fact]
    public void RegistryKeepsTheLastRowsAndTimestampWhenTheRefreshingReadFails()
    {
        var center = new DashboardFixture();
        using var context = CreateContext(center);
        var view = context.Render<DashboardLayout>(parameters => parameters.Add<Devices>(layout => layout.Body, _ => { }));

        view.WaitForAssertion(() => Assert.Contains("Workstation", view.Markup, StringComparison.Ordinal));
        var timestamp = view.Find(".dashboard-update time").GetAttribute("datetime");
        center.ListFailure = new DashboardApiException(DashboardFailure.Unavailable);
        view.Find(".dashboard-intro button").Click();

        view.WaitForAssertion(() => Assert.Contains("Device registry is stale", view.Markup, StringComparison.Ordinal));
        Assert.Equal(timestamp, view.Find(".dashboard-update time").GetAttribute("datetime"));
        Assert.Contains("Workstation", view.Find(".dashboard-table").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("No enrolled devices", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUncertainRevokeKeepsTheExactCommandAndOnlyOffersADeviceRead()
    {
        var center = new DashboardFixture();
        using var context = CreateContext(center);
        var view = RenderDevice(context);
        var dialog = view.Find("dialog");

        Assert.Contains(DeviceId.ToString("D"), dialog.TextContent, StringComparison.Ordinal);
        Assert.Contains(center.Device.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), dialog.TextContent, StringComparison.Ordinal);
        Assert.Equal("Cancel", dialog.QuerySelector("[autofocus]")?.TextContent);
        view.Find("dialog button[value=revoke]").Click();

        view.WaitForAssertion(() => Assert.Contains("Revocation result unknown", view.Markup, StringComparison.Ordinal));
        var issued = Assert.Single(center.Revocations);
        Assert.Equal((DeviceId, center.Device.Revision), (issued.Device, issued.Revision));
        Assert.NotEqual(Guid.Empty, issued.Command);
        Assert.Contains(issued.Command.ToString("D"), view.Markup, StringComparison.Ordinal);
        Assert.Equal("true", view.Find(".dashboard-access-panel > button").GetAttribute("aria-disabled"));
        Assert.Empty(view.FindAll(".dashboard-run-fields"));
        Assert.DoesNotContain("No runs in this Sandbox", view.Markup, StringComparison.Ordinal);

        view.Find(".dashboard-detail-intro button").Click();
        Assert.Single(center.Revocations);
        Assert.Contains("Revocation result unknown", view.Markup, StringComparison.Ordinal);

        center.Device = center.Device with { Presence = DashboardDevicePresence.Revoked, Revision = center.Device.Revision + 1 };
        view.Find(".dashboard-detail-intro button").Click();
        view.WaitForAssertion(() => Assert.Contains("The current enrollment is revoked.", view.Markup, StringComparison.Ordinal));
        Assert.Single(center.Revocations);
    }

    private static BunitContext CreateContext(DashboardFixture center)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IDashboardCenter>(center);
        return context;
    }

    private static IRenderedComponent<DashboardLayout> RenderDevice(BunitContext context) =>
        context.Render<DashboardLayout>(parameters => parameters.Add<DeviceDetails>(layout => layout.Body,
            device => device.Add(item => item.DeviceId, DeviceId)));

    private sealed class DashboardFixture : IDashboardCenter
    {
        private static readonly Uri Origin = new("https://center.example.org");
        private readonly Queue<(Task<DashboardSession> Response, TaskCompletionSource Started)> _sessionReads = new();

        public DashboardCenterConfiguration Configuration { get; } =
            DashboardCenterConfiguration.FromDeployment(Origin, Origin.AbsoluteUri, "self-hosted");

        public DashboardDevice Device { get; set; } = new(DeviceId, EnrollmentId, "Workstation", 19,
            DashboardDevicePresence.Online, "Linux", Observed, [], null);

        public DashboardSession Session { get; set; } = new(true, "Owner", null, null, Guid.NewGuid(), Guid.NewGuid(), 1);
        public int SessionReads { get; private set; }
        public DashboardApiException? ListFailure { get; set; }
        public int RegistryReads { get; private set; }
        public Task<DashboardDevicePage>? NextRegistry { get; set; }
        public CancellationToken SlowRead { get; private set; }
        public List<DashboardRunTarget?> RunReads { get; } = new();
        public List<(Guid Device, long Revision, Guid Command)> Revocations { get; } = new();

        public Task<DashboardSession> ReadSessionAsync(CancellationToken cancellationToken)
        {
            SessionReads++;
            if (!_sessionReads.TryDequeue(out var delayed))
            {
                return Task.FromResult(Session);
            }
            delayed.Started.SetResult();
            return delayed.Response;
        }

        public Task DelaySessionRead(Task<DashboardSession> response)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _sessionReads.Enqueue((response, started));
            return started.Task;
        }
        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DashboardDevicePage> ListDevicesAsync(string? cursor, CancellationToken cancellationToken)
        {
            RegistryReads++;
            if (NextRegistry is { } next)
            {
                NextRegistry = null;
                SlowRead = cancellationToken;
                return next;
            }
            return ListFailure is not null
                ? Task.FromException<DashboardDevicePage>(ListFailure)
                : Task.FromResult(new DashboardDevicePage([Device], null));
        }

        public Task<DashboardDevice> InspectDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(Device);

        public Task<DashboardDevice> RevokeDeviceAsync(Guid deviceId, long expectedRevision, Guid commandId, CancellationToken cancellationToken)
        {
            Revocations.Add((deviceId, expectedRevision, commandId));
            return Task.FromException<DashboardDevice>(new DashboardApiException(DashboardFailure.Uncertain));
        }

        public Task<DashboardRunSummary> ReadRunSummaryAsync(DashboardDevice device, CancellationToken cancellationToken, DashboardRunTarget? target = null)
        {
            RunReads.Add(target);
            return Task.FromResult(device.Presence == DashboardDevicePresence.Offline
                ? new DashboardRunSummary(DashboardRunState.Offline)
                : target is null
                    ? new DashboardRunSummary(DashboardRunState.Unknown)
                    : new DashboardRunSummary(DashboardRunState.Empty, 0, Observed, target.SandboxId));
        }
    }

    private sealed class DashboardActivityFixture : IDashboardActivity
    {
        public event Action? Resumed;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void Signal() => Resumed?.Invoke();
    }
}
