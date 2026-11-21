// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Pages;
using Bunit;
using Google.Protobuf;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Management = ArcForges.Contracts.Management.V2;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardReconnectAcceptanceTests
{
    private const string Inspect = "/arcforges.sandbox.v2.RunService/Inspect";
    private const string Stop = "/arcforges.sandbox.v2.RunService/Stop";
    private const string Operation = "/arcforges.sandbox.v2.NodeService/GetOperation";
    private const string Watch = "/arcforges.sandbox.v2.RunService/WatchOutput";
    private const string Commerce = "/arcforges.publicapi.v1.CommerceService/";

    [Fact]
    public async Task ResumeHidesTheCachedCheckoutUntilTheOriginalPurchaseReadCompletesWithoutWriting()
    {
        const string link = "https://checkout.example.test/ready";
        var offerId = WorkFixture.Id(WorkFixture.GrantId);
        var token = Xunit.TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CommerceServiceCreatePurchaseIntentRequest? original = null;
        using var server = DashboardLifecycleFixture.Server(async (captured, cancellation) =>
        {
            switch (captured.Url.Segments[^1])
            {
                case "GetSnapshot":
                    var entitlement = WorkFixture.Read(captured, "/arcforges.publicapi.v1.EntitlementService/GetSnapshot", EntitlementServiceGetSnapshotRequest.Parser);
                    var snapshot = new EntitlementSnapshot { Version = 0 };
                    snapshot.Capabilities.AddRange(new[] { "center.nodes", "remote.view", "remote.control" }.Select(capability =>
                        new Availability { Capability = capability, Allowed = false, Reasons = { "no_entitlement" } }));
                    return WorkFixture.Reply(new EntitlementServiceGetSnapshotResponse { Meta = WorkFixture.Meta(entitlement.Meta), Value = new() { Snapshot = snapshot } });
                case "GetSubscription":
                    var subscription = WorkFixture.Read(captured, Commerce + "GetSubscription", CommerceServiceGetSubscriptionRequest.Parser);
                    return WorkFixture.Reply(new CommerceServiceGetSubscriptionResponse
                    {
                        Meta = WorkFixture.Meta(subscription.Meta),
                        Error = new ArcError { Category = ErrorCategory.State, Code = "not_found", Effect = EffectCertainty.DidNotHappen },
                    });
                case "ListBillingHistory":
                    var history = WorkFixture.Read(captured, Commerce + "ListBillingHistory", CommerceServiceListBillingHistoryRequest.Parser);
                    return WorkFixture.Reply(new CommerceServiceListBillingHistoryResponse { Meta = WorkFixture.Meta(history.Meta), Value = new() { Page = new() } });
                case "GetCatalogue":
                    var catalogue = WorkFixture.Read(captured, Commerce + "GetCatalogue", CommerceServiceGetCatalogueRequest.Parser);
                    Assert.Equal("GB", catalogue.Region);
                    return WorkFixture.Reply(new CommerceServiceGetCatalogueResponse
                    {
                        Meta = WorkFixture.Meta(catalogue.Meta),
                        Value = new()
                        {
                            CatalogueVersion = "fixture-catalogue-v1",
                            Items = { new Offer { OfferId = offerId, Kind = "subscription", Region = "GB", Currency = "GBP",
                                Interval = "month", PriceVersion = "fixture-price-v1", Amount = new() { Value = "12.37" } } },
                        },
                    });
                case "CreatePurchaseIntent":
                    Assert.Null(original);
                    original = WorkFixture.Read(captured, Commerce + "CreatePurchaseIntent", CommerceServiceCreatePurchaseIntentRequest.Parser);
                    return WorkFixture.Reply(new CommerceServiceCreatePurchaseIntentResponse
                    {
                        Meta = WorkFixture.Meta(original.Meta),
                        Value = new() { Purchase = new() { PurchaseIntentId = original.PurchaseIntentId, OfferId = offerId, State = "pending" } },
                    });
                case "CreateCheckoutAttempt":
                    var checkout = WorkFixture.Read(captured, Commerce + "CreateCheckoutAttempt", CommerceServiceCreateCheckoutAttemptRequest.Parser);
                    Assert.Equal(original?.PurchaseIntentId, checkout.PurchaseIntentId);
                    return WorkFixture.Reply(new CommerceServiceCreateCheckoutAttemptResponse
                    {
                        Meta = WorkFixture.Meta(checkout.Meta),
                        Value = new()
                        {
                            Checkout = new()
                            {
                                PurchaseIntentId = checkout.PurchaseIntentId,
                                CheckoutAttemptId = checkout.CheckoutAttemptId,
                                Url = link,
                                ExpiresAt = new() { UnixSeconds = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }
                            }
                        },
                    });
                default:
                    var read = WorkFixture.Read(captured, Commerce + "GetPurchaseState", CommerceServiceGetPurchaseStateRequest.Parser);
                    Assert.Equal(original?.PurchaseIntentId, read.PurchaseIntentId);
                    Assert.Null(read.Meta.CommandId);
                    entered.SetResult();
                    await release.Task.WaitAsync(cancellation);
                    return WorkFixture.Reply(new CommerceServiceGetPurchaseStateResponse
                    {
                        Meta = WorkFixture.Meta(read.Meta),
                        Value = new() { Purchase = new() { PurchaseIntentId = read.PurchaseIntentId, OfferId = offerId, State = "expired" } },
                    });
            }
        });
        using var context = DashboardLifecycleFixture.Context(server, "official-hosted", typeof(IDashboardSubscription));
        var activity = new Activity();
        context.Services.AddSingleton<IDashboardActivity>(activity);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = DashboardLifecycleFixture.Render<Subscription>(context);
        const string form = "section[aria-labelledby='subscription-catalogue'] form";
        view.WaitForAssertion(() => Assert.Contains("No hosted subscription is recorded", view.Markup, StringComparison.Ordinal));
        await view.InvokeAsync(() => view.Find(form + " input").Input("GB"));
        await view.InvokeAsync(() => view.Find(form).Submit());
        view.WaitForAssertion(() => Assert.Contains("12.37 GBP per month", view.Markup, StringComparison.Ordinal));
        await DashboardLifecycleFixture.ClickAsync(view, "Review purchase");
        await DashboardLifecycleFixture.ClickAsync(view, "Confirm");
        view.WaitForAssertion(() => Assert.Contains("Purchase prepared", view.Markup, StringComparison.Ordinal));
        await DashboardLifecycleFixture.ClickAsync(view, "Review checkout");
        await DashboardLifecycleFixture.ClickAsync(view, "Confirm");
        view.WaitForAssertion(() => Assert.Single(view.FindAll($"a[href='{link}']")));

        try
        {
            await view.InvokeAsync(activity.Signal);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            view.WaitForAssertion(() => Assert.Empty(view.FindAll($"a[href='{link}']")));
            release.SetResult();
            view.WaitForAssertion(() => Assert.Contains("The purchase expired", view.Markup, StringComparison.Ordinal));
            Assert.Empty(view.FindAll($"a[href='{link}']"));
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith("/CreatePurchaseIntent", StringComparison.Ordinal));
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith("/CreateCheckoutAttempt", StringComparison.Ordinal));
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith("/GetPurchaseState", StringComparison.Ordinal));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task OverlappingSessionResumesKeepTheOldScopeHiddenUntilTheLatestNodeRead()
    {
        var token = Xunit.TestContext.Current.CancellationToken;
        int inspections = 0;
        int boots = 0;
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestBootstrap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLatest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = DashboardLifecycleFixture.Server(async (captured, cancellation) =>
        {
            var inspect = WorkFixture.Read(captured, "/arcforges.management.v2.DeviceService/Inspect", Management.DeviceServiceInspectRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.DeviceId), inspect.DeviceId);
            int attempt = ++inspections;
            if (attempt == 2)
            {
                firstRead.SetResult();
                await releaseFirst.Task;
            }
            else if (attempt == 3)
            {
                latestRead.SetResult();
                await releaseLatest.Task.WaitAsync(cancellation);
            }
            Assert.InRange(attempt, 1, 3);
            return WorkFixture.Reply(new Management.DeviceServiceInspectResponse
            {
                Meta = WorkFixture.Meta(inspect.Meta, WorkFixture.Revision),
                Value = new()
                {
                    Device = new()
                    {
                        DeviceId = inspect.DeviceId,
                        EnrollmentId = WorkFixture.Id(WorkFixture.EnrollmentId),
                        DisplayName = "Scoped fixture device",
                        Revision = new() { Value = WorkFixture.Revision },
                        Presence = "online",
                        NodeEpoch = attempt == 3 ? "replacement-epoch" : "first-epoch",
                    },
                },
            });
        }, () =>
        {
            if (++boots == 3)
                latestBootstrap.SetResult();
            return DashboardLifecycleFixture.Bootstrap();
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardWorkspaces)]);
        var activity = new Activity();
        context.Services.AddSingleton<IDashboardActivity>(activity);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        const string form = ".dashboard-work-scope form";
        var view = DashboardLifecycleFixture.Render<DashboardWorkScopeView>(context, parameters => parameters
            .Add(component => component.DeviceId, WorkFixture.DeviceId)
            .Add(component => component.SandboxId, WorkFixture.SandboxId)
            .Add<DashboardWorkTarget>(component => component.ChildContent, target => $"Fixture grant applied: {target.GrantId:D}"));
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(form)));
        await view.InvokeAsync(() =>
        {
            view.FindAll(form + " input")[0].Input(WorkFixture.NodeId.ToString("D"));
            view.FindAll(form + " input")[1].Input(WorkFixture.GrantId.ToString("D"));
            view.FindAll(form + " input")[2].Input(WorkFixture.Target.GrantRevision.ToString(CultureInfo.InvariantCulture));
            view.Find(form).Submit();
        });
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".dashboard-work-view")));

        try
        {
            await view.InvokeAsync(activity.Signal);
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[hidden] .dashboard-work-view")));
            Assert.Equal(2, inspections);

            await view.InvokeAsync(activity.Signal);
            await latestBootstrap.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            releaseFirst.SetResult();
            await latestRead.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[hidden] .dashboard-work-view")));
            releaseLatest.SetResult();

            view.WaitForAssertion(() =>
            {
                Assert.Empty(view.FindAll(".dashboard-work-view"));
                Assert.Contains("Select a local grant", view.Markup, StringComparison.Ordinal);
            });
            Assert.DoesNotContain("Fixture grant applied", view.Markup, StringComparison.Ordinal);
            Assert.Equal(3, server.Requests.Count(request => request.Method == HttpMethod.Post));
            Assert.Equal(3, boots);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseLatest.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineSessionRevalidationRetainsTheOriginalUnknownCommandWithoutReplayingIt(bool lateReply)
    {
        var token = Xunit.TestContext.Current.CancellationToken;
        int boots = 0;
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunServiceStopRequest? original = null;
        using var server = DashboardLifecycleFixture.Server(async (captured, _) =>
        {
            if (captured.Url.AbsolutePath.EndsWith(Inspect, StringComparison.Ordinal))
            {
                var inspect = WorkFixture.Read(captured, Inspect, RunServiceInspectRequest.Parser);
                Assert.Equal(WorkFixture.Id(WorkFixture.RunId), inspect.RunId);
                return WorkFixture.Reply(new RunServiceInspectResponse
                {
                    Meta = WorkFixture.Meta(inspect.Meta, WorkFixture.Revision),
                    Value = new() { Run = WorkFixture.Run() },
                });
            }
            if (captured.Url.AbsolutePath.EndsWith(Stop, StringComparison.Ordinal))
            {
                Assert.Null(original);
                original = WorkFixture.Read(captured, Stop, RunServiceStopRequest.Parser);
                Assert.Equal(WorkFixture.Revision, original.Meta.ExpectedRev.Value);
                stopEntered.SetResult();
                if (!lateReply)
                    throw new HttpRequestException("Fixture lost the original stop response.");

                await releaseStop.Task;
                return WorkFixture.Reply(new RunServiceStopResponse
                {
                    Meta = WorkFixture.Meta(original.Meta, WorkFixture.Revision + 1),
                    Value = new()
                    {
                        Run = WorkFixture.Run(WorkFixture.Revision + 1, RunState.Exited),
                        Operation = WorkFixture.Operation(original.Meta.CommandId, original.OwnerEpoch, OperationState.Succeeded, EffectCertainty.Happened),
                    },
                });
            }

            var read = WorkFixture.Read(captured, Operation, NodeServiceGetOperationRequest.Parser);
            Assert.NotNull(original);
            Assert.Equal(original.Meta.CommandId, read.CommandId);
            Assert.Equal(original.OwnerEpoch, read.OwnerEpoch);
            Assert.Null(read.Meta.CommandId);
            return WorkFixture.Reply(new NodeServiceGetOperationResponse
            {
                Meta = WorkFixture.Meta(read.Meta),
                Value = new()
                {
                    Operation = WorkFixture.Operation(read.CommandId, read.OwnerEpoch, OperationState.Unknown, EffectCertainty.Unknown),
                },
            });
        }, () => ++boots == 2 ? throw new HttpRequestException("Fixture Center offline.") : DashboardLifecycleFixture.Bootstrap());
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardWorkspaces)]);
        var activity = new Activity();
        context.Services.AddSingleton<IDashboardActivity>(activity);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = DashboardLifecycleFixture.Render<RunDetailsView>(context, parameters => parameters
            .Add(component => component.Target, WorkFixture.Target)
            .Add(component => component.DeviceId, WorkFixture.DeviceId)
            .Add(component => component.RunId, WorkFixture.RunId));
        view.WaitForAssertion(() => Assert.False(view.Find("#dashboard-stop-run-confirm button[value=stop]").HasAttribute("disabled")));

        var stop = view.Find("#dashboard-stop-run-confirm button[value=stop]").ClickAsync(new MouseEventArgs());
        try
        {
            await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            if (!lateReply)
            {
                await stop.WaitAsync(TimeSpan.FromSeconds(5), token);
                view.WaitForAssertion(() => Assert.Contains("No outcome has been confirmed", view.Markup, StringComparison.Ordinal));
            }
            Assert.NotNull(original);
            var command = new Guid(original.Meta.CommandId.Value.Span, bigEndian: true).ToString("D");

            await view.InvokeAsync(activity.Signal);
            view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".dashboard-session")));
            Assert.Empty(view.FindAll("#dashboard-stop-run-confirm"));
            Assert.DoesNotContain(command, view.Markup, StringComparison.Ordinal);
            releaseStop.SetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5), token);

            await DashboardLifecycleFixture.ClickAsync(view, "Check session");
            view.WaitForAssertion(() =>
            {
                Assert.Contains(command, view.Markup, StringComparison.Ordinal);
                Assert.Contains("No outcome has been confirmed", view.Markup, StringComparison.Ordinal);
                Assert.True(view.Find("#dashboard-stop-run-confirm button[value=stop]").HasAttribute("disabled"));
            });
            Assert.DoesNotContain(server.Requests, request => request.Url.AbsolutePath.EndsWith(Operation, StringComparison.Ordinal));

            await DashboardLifecycleFixture.ClickAsync(view, "Check command outcome");
            view.WaitForAssertion(() => Assert.Contains("outcome remains unknown", view.Markup, StringComparison.Ordinal));
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(Stop, StringComparison.Ordinal));
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(Operation, StringComparison.Ordinal));
            Assert.Equal(3, boots);
        }
        finally
        {
            releaseStop.TrySetResult();
        }
    }

    [Fact]
    public async Task UnverifiedSessionRefusesNewStopAndKeepsUncancelledOriginalOutcomeUncertain()
    {
        var token = Xunit.TestContext.Current.CancellationToken;
        using var mutationLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var target = WorkFixture.Target;
        var original = new DashboardWorkCommand(Guid.NewGuid(), WorkFixture.OwnerEpoch, WorkFixture.Revision);
        var other = original with { CommandId = Guid.NewGuid() };
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var boots = 0;
        using var server = DashboardLifecycleFixture.Server(async (captured, cancellation) =>
        {
            if (captured.Url.AbsolutePath.EndsWith(Stop, StringComparison.Ordinal))
            {
                var stop = WorkFixture.Read(captured, Stop, RunServiceStopRequest.Parser);
                Assert.Equal(WorkFixture.Id(WorkFixture.RunId), stop.RunId);
                Assert.Equal(WorkFixture.Id(original.CommandId), stop.Meta.CommandId);
                Assert.Equal(WorkFixture.Id(original.OwnerEpoch), stop.OwnerEpoch);
                Assert.Equal(original.ExpectedRevision, stop.Meta.ExpectedRev.Value);
                stopEntered.SetResult();
                await releaseStop.Task.WaitAsync(cancellation);
                return WorkFixture.Reply(new RunServiceStopResponse
                {
                    Meta = WorkFixture.Meta(stop.Meta, WorkFixture.Revision + 1),
                    Value = new()
                    {
                        Run = WorkFixture.Run(WorkFixture.Revision + 1, RunState.Exited),
                        Operation = WorkFixture.Operation(stop.Meta.CommandId, stop.OwnerEpoch, OperationState.Succeeded, EffectCertainty.Happened),
                    },
                });
            }

            var read = WorkFixture.Read(captured, Operation, NodeServiceGetOperationRequest.Parser);
            Assert.Equal(WorkFixture.Id(original.CommandId), read.CommandId);
            Assert.Equal(WorkFixture.Id(original.OwnerEpoch), read.OwnerEpoch);
            Assert.Null(read.Meta.CommandId);
            Assert.Null(read.Meta.ExpectedRev);
            return WorkFixture.Reply(new NodeServiceGetOperationResponse
            {
                Meta = WorkFixture.Meta(read.Meta),
                Value = new()
                {
                    Operation = WorkFixture.Operation(read.CommandId, read.OwnerEpoch, OperationState.Succeeded, EffectCertainty.Happened),
                },
            });
        }, () => ++boots == 2 ? throw new HttpRequestException("Fixture Center offline.") : DashboardLifecycleFixture.Bootstrap());

        var center = DashboardLifecycleFixture.Center(server);
        var work = Assert.IsAssignableFrom<IDashboardWorkspaces>(center);
        var initial = await center.ReadSessionAsync(token);
        Assert.True(initial.Authenticated);
        var originalStop = work.StopRunAsync(target, original, WorkFixture.RunId, null, mutationLifetime.Token);
        try
        {
            await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            var offline = await Assert.ThrowsAsync<DashboardApiException>(() => center.ReadSessionAsync(token));
            Assert.Equal(DashboardFailure.Unavailable, offline.Failure);

            var requestsBeforeNewStop = server.Count;
            var refused = await Assert.ThrowsAsync<DashboardApiException>(() => work.StopRunAsync(target, other, WorkFixture.RunId, null, token));
            Assert.Equal(DashboardFailure.Unavailable, refused.Failure);
            Assert.Equal(requestsBeforeNewStop, server.Count);

            releaseStop.SetResult();
            var originalFailure = await Assert.ThrowsAsync<DashboardApiException>(() => originalStop.WaitAsync(TimeSpan.FromSeconds(5), token));
            Assert.Equal(DashboardFailure.Uncertain, originalFailure.Failure);
            Assert.False(mutationLifetime.IsCancellationRequested);

            var restored = await center.ReadSessionAsync(token);
            Assert.True(restored.Authenticated);
            Assert.Equal(initial.AccountId, restored.AccountId);
            Assert.Equal(initial.SessionId, restored.SessionId);
            Assert.Equal(initial.RecoveryGeneration, restored.RecoveryGeneration);
            var resolved = await work.GetOperationAsync(target, original, token);
            Assert.Equal(OperationState.Succeeded, resolved.State);
            Assert.Equal(EffectCertainty.Happened, resolved.Effect);
            Assert.Equal(WorkFixture.Id(original.CommandId), resolved.CommandId);
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(Stop, StringComparison.Ordinal));
            Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(Operation, StringComparison.Ordinal));
            Assert.Equal(3, boots);
        }
        finally
        {
            releaseStop.TrySetResult();
        }
    }

    [Fact]
    public async Task ResumingCancelsSlowOutputAndRequiresAnExplicitNewRead()
    {
        const string epoch = "fixture-run-epoch";
        var token = Xunit.TestContext.Current.CancellationToken;
        int watches = 0;
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = DashboardLifecycleFixture.Server(async (captured, cancellation) =>
        {
            var watch = WorkFixture.Read(captured, Watch, RunServiceWatchOutputRequest.Parser);
            Assert.Equal(epoch, watch.Epoch);
            Assert.False(watch.HasOffset);
            Assert.Equal(StreamKind.Stdout, watch.Stream);
            bool first = ++watches == 1;
            if (first)
            {
                entered.SetResult(cancellation);
                await release.Task;
            }

            var bytes = ByteString.CopyFromUtf8(first ? "late fixture output" : "fresh fixture output");
            return WorkFixture.Reply(new RunServiceWatchOutputFrame
            {
                Meta = WorkFixture.Meta(watch.Meta),
                Chunk = new() { Epoch = epoch, Stream = StreamKind.Stdout, Offset = 0, NextOffset = (ulong)bytes.Length, Data = bytes },
            });
        });
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardWorkspaces)]);
        var activity = new Activity();
        context.Services.AddSingleton<IDashboardActivity>(activity);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = DashboardLifecycleFixture.Render<RunOutputView>(context, parameters => parameters
            .Add(component => component.Target, WorkFixture.Target)
            .Add(component => component.RunId, WorkFixture.RunId)
            .Add(component => component.RunEpoch, epoch)
            .Add(component => component.Mode, TerminalMode.Pipes));
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("#dashboard-run-output-title")));

        var oldRead = view.FindAll("button").Single(button => button.TextContent.Trim() == "Read output").ClickAsync(new MouseEventArgs());
        try
        {
            var oldCancellation = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            await view.InvokeAsync(activity.Signal);
            view.WaitForAssertion(() => Assert.True(oldCancellation.IsCancellationRequested));
            release.SetResult();
            await oldRead.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.DoesNotContain("late fixture output", view.Markup, StringComparison.Ordinal);
            Assert.Equal(1, watches);

            await DashboardLifecycleFixture.ClickAsync(view, "Read output");
            view.WaitForAssertion(() => Assert.Contains("fresh fixture output", view.Find("pre").TextContent, StringComparison.Ordinal));
            Assert.Equal(2, watches);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private sealed class Activity : IDashboardActivity
    {
        public event Action? Resumed;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        internal void Signal() => Resumed?.Invoke();
    }
}
