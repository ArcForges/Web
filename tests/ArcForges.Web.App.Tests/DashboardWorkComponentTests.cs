// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using ArcForges.Web.App.Pages;
using ArcForges.Web.App.Tests.TestDoubles;
using Bunit;
using Google.Protobuf;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardWorkComponentTests
{
    private const string InspectRun = "/arcforges.sandbox.v2.RunService/Inspect";
    private const string StopRun = "/arcforges.sandbox.v2.RunService/Stop";
    private const string GetOperation = "/arcforges.sandbox.v2.NodeService/GetOperation";
    private const string CreateSandbox = "/arcforges.sandbox.v2.SandboxService/Create";
    private const string ListRemoteGrants = "/arcforges.sandbox.v2.NodeService/ListRemoteGrants";
    private const string GetResourceBudget = "/arcforges.sandbox.v2.ResourceService/GetBudget";
    private const string SetResourcePolicy = "/arcforges.sandbox.v2.ResourceService/SetPolicy";

    [Fact]
    public async Task StartingFromTheFormPreservesTheConfirmedSandboxRevisionAndArgumentBoundaries()
    {
        using var server = WorkFixture.Server((request, _) =>
        {
            var start = WorkFixture.Read(request, "/arcforges.sandbox.v2.RunService/Start", RunServiceStartRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.SandboxId), start.SandboxId);
            Assert.Equal(WorkFixture.Id(WorkFixture.OwnerEpoch), start.OwnerEpoch);
            Assert.Equal(WorkFixture.Revision, start.Meta.ExpectedRev.Value);
            Assert.Equal(new[] { "fixture-tool", "an argument with spaces", "" }, start.Argv);
            Assert.Equal("work/data", start.WorkingDirectory);
            Assert.Empty(start.Environment);
            Assert.Equal(TerminalMode.Pipes, start.TerminalMode);
            return Task.FromResult(WorkFixture.Reply(new RunServiceStartResponse
            {
                Meta = WorkFixture.Meta(start.Meta),
                Value = new()
                {
                    Run = WorkFixture.Run(state: RunState.Starting),
                    Operation = WorkFixture.Operation(start.Meta.CommandId, start.OwnerEpoch, OperationState.Accepted, EffectCertainty.Unknown),
                },
            }));
        });
        using var context = CreateContext(server);
        var view = Render<SandboxRunStart>(context, parameters => parameters
            .Add(component => component.Target, WorkFixture.Target)
            .Add(component => component.DeviceId, WorkFixture.DeviceId)
            .Add(component => component.SandboxRevision, WorkFixture.Revision));
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("form input")));

        await view.InvokeAsync(() =>
        {
            view.FindAll("input")[0].Input("fixture-tool");
            view.FindAll("input")[1].Input("work/data");
            FindButton(view, "Add argument").Click();
            view.FindAll("input")[2].Input("an argument with spaces");
            FindButton(view, "Add argument").Click();
        });
        await view.InvokeAsync(() => view.Find("form").Submit());

        view.WaitForAssertion(() => Assert.Contains("Command accepted", view.Markup, StringComparison.Ordinal));
        Assert.Equal(2, server.Count);
        Assert.Single(view.FindAll($"a[href='/devices/{WorkFixture.DeviceId:D}/runs/{WorkFixture.RunId:D}']"));
        Assert.True(FindButton(view, "Start Run").HasAttribute("disabled"));
    }

    [Fact]
    public async Task UnconfirmedStopOnlyReadsTheOriginalReceiptUntilTheOutcomeIsKnown()
    {
        RunServiceStopRequest? original = null;
        var checks = 0;
        using var server = WorkFixture.Server((request, _) =>
        {
            if (request.Url.AbsolutePath.EndsWith(InspectRun, StringComparison.Ordinal))
            {
                var inspected = checks < 2 ? WorkFixture.Run() : WorkFixture.Run(WorkFixture.Revision + 1, RunState.Exited);
                return Task.FromResult(InspectReply(request, inspected));
            }

            if (request.Url.AbsolutePath.EndsWith(StopRun, StringComparison.Ordinal))
            {
                Assert.Null(original);
                original = WorkFixture.Read(request, StopRun, RunServiceStopRequest.Parser);
                Assert.Equal(WorkFixture.Id(WorkFixture.RunId), original.RunId);
                Assert.Equal(WorkFixture.Id(WorkFixture.OwnerEpoch), original.OwnerEpoch);
                Assert.Equal(WorkFixture.Revision, original.Meta.ExpectedRev.Value);
                throw new HttpRequestException("Fixture lost the response after sending the command.");
            }

            var lookup = WorkFixture.Read(request, GetOperation, NodeServiceGetOperationRequest.Parser);
            Assert.NotNull(original);
            Assert.Equal(original.Meta.CommandId, lookup.CommandId);
            Assert.Equal(original.OwnerEpoch, lookup.OwnerEpoch);
            Assert.Null(lookup.Meta.CommandId);
            Assert.Null(lookup.Meta.ExpectedRev);
            bool resolved = ++checks >= 2;
            return Task.FromResult(WorkFixture.Reply(new NodeServiceGetOperationResponse
            {
                Meta = WorkFixture.Meta(lookup.Meta),
                Value = new()
                {
                    Operation = WorkFixture.Operation(lookup.CommandId, lookup.OwnerEpoch,
                        resolved ? OperationState.Succeeded : OperationState.Unknown,
                        resolved ? EffectCertainty.Happened : EffectCertainty.Unknown),
                },
            }));
        });
        using var context = CreateContext(server);
        var view = RenderRun(context, WorkFixture.Target);
        view.WaitForAssertion(() => Assert.False(FindButton(view, "Confirm stop").HasAttribute("disabled")));

        await ClickAsync(view, "Confirm stop");
        view.WaitForAssertion(() => Assert.Contains("No outcome has been confirmed", view.Markup, StringComparison.Ordinal));
        Assert.True(FindButton(view, "Confirm stop").HasAttribute("disabled"));
        Assert.NotNull(original);
        Assert.Contains(new Guid(original.Meta.CommandId.Value.Span, bigEndian: true).ToString("D"), view.Markup, StringComparison.Ordinal);

        await ClickAsync(view, "Check command outcome");
        view.WaitForAssertion(() => Assert.Contains("outcome remains unknown", view.Markup, StringComparison.Ordinal));
        Assert.True(FindButton(view, "Confirm stop").HasAttribute("disabled"));
        await ClickAsync(view, "Check command outcome");

        view.WaitForAssertion(() => Assert.Contains("Command completed", view.Markup, StringComparison.Ordinal));
        Assert.Contains("Exited", view.Markup, StringComparison.Ordinal);
        Assert.True(FindButton(view, "Confirm stop").HasAttribute("disabled"));
        Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(StopRun, StringComparison.Ordinal));
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task FailedRefreshMarksTheRetainedRunStaleAndDisablesStop()
    {
        int reads = 0;
        using var server = WorkFixture.Server((request, _) =>
        {
            if (++reads == 1)
                return Task.FromResult(InspectReply(request, WorkFixture.Run()));

            var inspect = WorkFixture.Read(request, InspectRun, RunServiceInspectRequest.Parser);
            return Task.FromResult(WorkFixture.Reply(new RunServiceInspectResponse
            {
                Meta = WorkFixture.Meta(inspect.Meta),
                Error = new ArcError { Category = ErrorCategory.State, Code = "node_offline", Effect = EffectCertainty.DidNotHappen },
            }));
        });
        using var context = CreateContext(server);
        var view = RenderRun(context, WorkFixture.Target);
        view.WaitForAssertion(() => Assert.False(FindButton(view, "Confirm stop").HasAttribute("disabled")));

        await ClickAsync(view, "Refresh");

        view.WaitForAssertion(() => Assert.Contains("Results may be stale", view.Markup, StringComparison.Ordinal));
        Assert.Equal("fixture workload", view.Find("h2").TextContent);
        Assert.True(FindButton(view, "Confirm stop").HasAttribute("disabled"));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void ReadOnlyScopeCannotOfferRunMutations()
    {
        var target = WorkFixture.Target with { OwnerEpoch = null };
        using var server = WorkFixture.Server((request, _) => Task.FromResult(InspectReply(request, WorkFixture.Run())));
        using var context = CreateContext(server);

        var run = RenderRun(context, target);
        run.WaitForAssertion(() => Assert.Contains("owner epoch is required", run.Markup, StringComparison.Ordinal));
        Assert.True(FindButton(run, "Confirm stop").HasAttribute("disabled"));
        var start = Render<SandboxRunStart>(context, parameters => parameters
            .Add(component => component.Target, target)
            .Add(component => component.DeviceId, WorkFixture.DeviceId)
            .Add(component => component.SandboxRevision, WorkFixture.Revision));

        start.WaitForAssertion(() => Assert.Contains("Node owner epoch", start.Markup, StringComparison.Ordinal));
        Assert.Empty(start.FindAll("form"));
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task RetainedOutputRendersAsTextAndItsGapSuppliesTheNextExactOffset()
    {
        const string epoch = "fixture-run-epoch";
        const string text = "<img src=x onerror=alert(1)>\u001b[31m";
        const ulong earliest = 9007199254740993;
        var bytes = ByteString.CopyFromUtf8(text);
        int reads = 0;
        using var server = WorkFixture.Server((request, _) =>
        {
            var watch = WorkFixture.Read(request, "/arcforges.sandbox.v2.RunService/WatchOutput", RunServiceWatchOutputRequest.Parser);
            Assert.Equal(StreamKind.Stdout, watch.Stream);
            Assert.Equal(epoch, watch.Epoch);
            Assert.Equal(WorkFixture.Id(WorkFixture.RunId), watch.RunId);
            Assert.False(watch.Meta.HasRecoveryGeneration);
            if (++reads == 1)
            {
                Assert.False(watch.HasOffset);
                return Task.FromResult(WorkFixture.Reply(new RunServiceWatchOutputFrame
                {
                    Meta = WorkFixture.Meta(watch.Meta),
                    Gap = new() { Epoch = epoch, Reason = "retention", RequestedOffset = 0, EarliestOffset = earliest },
                }));
            }

            Assert.Equal(earliest, watch.Offset);
            return Task.FromResult(WorkFixture.Reply(new RunServiceWatchOutputFrame
            {
                Meta = WorkFixture.Meta(watch.Meta),
                Chunk = new() { Epoch = epoch, Stream = StreamKind.Stdout, Offset = earliest, NextOffset = earliest + (ulong)bytes.Length, Data = bytes },
            }));
        });
        using var context = CreateContext(server);
        var view = Render<RunOutputView>(context, parameters => parameters
            .Add(component => component.Target, WorkFixture.Target)
            .Add(component => component.RunId, WorkFixture.RunId)
            .Add(component => component.RunEpoch, epoch)
            .Add(component => component.Mode, TerminalMode.Pipes));
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("#dashboard-run-output-title")));

        await ClickAsync(view, "Read output");
        view.WaitForAssertion(() => Assert.Contains("9007199254740993", view.Markup, StringComparison.Ordinal));
        Assert.Empty(view.FindAll("pre"));
        await ClickAsync(view, "Read more output");

        view.WaitForAssertion(() => Assert.Contains("<img src=x onerror=alert(1)>", view.Find("pre").TextContent, StringComparison.Ordinal));
        Assert.Empty(view.FindAll("img"));
        Assert.DoesNotContain('\u001b', view.Find("pre").TextContent);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task CreationUsesTheExactLocalTemplateAndCompactedReceiptNeverInventsARevision()
    {
        var target = WorkFixture.Target with { SandboxId = null };
        var template = new SandboxConfig
        {
            ProfileId = "fixture-profile",
            ProfileVersion = "1.2.3",
            Network = new() { Egress = NetworkMode.Deny, Ingress = NetworkMode.Deny },
            Resources = new() { CpuMaxMillicores = 3000, MemoryMaxBytes = 1_073_741_824, PressureAction = PressureAction.Throttle },
        };
        const long templateRevision = WorkFixture.Revision;
        SandboxServiceCreateRequest? original = null;
        using var server = WorkFixture.Server((request, _) =>
        {
            if (request.Url.AbsolutePath.EndsWith(ListRemoteGrants, StringComparison.Ordinal))
            {
                var list = WorkFixture.Read(request, ListRemoteGrants, NodeServiceListRemoteGrantsRequest.Parser);
                Assert.Null(list.SandboxId);
                return Task.FromResult(WorkFixture.Reply(new NodeServiceListRemoteGrantsResponse
                {
                    Meta = WorkFixture.Meta(list.Meta),
                    Value = new()
                    {
                        Page = new(),
                        Grants =
                        {
                            new RemoteGrant
                            {
                                GrantId = WorkFixture.Id(target.GrantId), EnrollmentId = WorkFixture.Id(target.EnrollmentId),
                                Revision = new() { Value = target.GrantRevision }, ScopeKind = "provision", State = GrantState.Active,
                                Methods = { CreateSandbox }, TemplateRevision = new() { Value = templateRevision }, CreationTemplate = template,
                            },
                        },
                    },
                }));
            }

            if (request.Url.AbsolutePath.EndsWith(CreateSandbox, StringComparison.Ordinal))
            {
                Assert.Null(original);
                original = WorkFixture.Read(request, CreateSandbox, SandboxServiceCreateRequest.Parser);
                Assert.Equal("Build fixture", original.DisplayName);
                Assert.Equal(WorkFixture.Id(WorkFixture.OwnerEpoch), original.OwnerEpoch);
                Assert.Equal(WorkFixture.Id(target.GrantId), original.ProvisionGrantId);
                Assert.Equal(templateRevision, original.TemplateRevision.Value);
                Assert.Null(original.Meta.ExpectedRev);
                Assert.Equal(template, original.Config);
                return Task.FromResult(WorkFixture.Reply(new SandboxServiceCreateResponse
                {
                    Meta = WorkFixture.Meta(original.Meta, 1),
                    Value = new()
                    {
                        Sandbox = new() { SandboxId = WorkFixture.Id(WorkFixture.SandboxId), Revision = new() { Value = 1 }, State = SandboxState.Creating },
                    },
                }));
            }

            var lookup = WorkFixture.Read(request, GetOperation, NodeServiceGetOperationRequest.Parser);
            Assert.NotNull(original);
            Assert.Equal(original.Meta.CommandId, lookup.CommandId);
            Assert.Equal(original.OwnerEpoch, lookup.OwnerEpoch);
            Assert.Null(lookup.Meta.CommandId);
            return Task.FromResult(WorkFixture.Reply(new NodeServiceGetOperationResponse
            {
                Meta = WorkFixture.Meta(lookup.Meta),
                Value = new()
                {
                    Operation = new()
                    {
                        CommandId = lookup.CommandId,
                        OwnerEpoch = lookup.OwnerEpoch,
                        Method = "SandboxService.Create",
                        AggregateId = WorkFixture.Id(WorkFixture.SandboxId),
                        State = OperationState.Succeeded,
                        Effect = EffectCertainty.Happened,
                        ResponseCompacted = true,
                    },
                },
            }));
        });
        using var context = CreateContext(server);
        var view = Render<SandboxProvisionForm>(context, parameters => parameters
            .Add(component => component.Target, target)
            .Add(component => component.DeviceId, WorkFixture.DeviceId));
        view.WaitForAssertion(() => Assert.False(FindButton(view, "Create Sandbox").HasAttribute("disabled")));
        await view.InvokeAsync(() =>
        {
            view.Find("form input").Input("Build fixture");
            view.Find("form").Submit();
        });

        view.WaitForAssertion(() => Assert.Contains("Creation accepted. Result pending.", view.Markup, StringComparison.Ordinal));
        var detail = $"a[href='/devices/{WorkFixture.DeviceId:D}/sandboxes/{WorkFixture.SandboxId:D}']";
        Assert.Empty(view.FindAll(detail));
        Assert.True(FindButton(view, "Create Sandbox").HasAttribute("disabled"));
        await ClickAsync(view, "Check command status");

        view.WaitForAssertion(() => Assert.Contains("Sandbox created.", view.Markup, StringComparison.Ordinal));
        Assert.Contains("The original revision is unavailable", view.Markup, StringComparison.Ordinal);
        Assert.Single(view.FindAll(detail));
        Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(CreateSandbox, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoweringRunLimitsUsesTheExactExplicitPolicyRevisionAndReportsAnActualConflict(bool conflict)
    {
        long policyRevision = 1;
        using var server = WorkFixture.Server((request, _) =>
        {
            if (request.Url.AbsolutePath.EndsWith(GetResourceBudget, StringComparison.Ordinal))
            {
                var read = WorkFixture.Read(request, GetResourceBudget, ResourceServiceGetBudgetRequest.Parser);
                var run = read.ScopeKind == "run";
                Assert.Equal(WorkFixture.Id(run ? WorkFixture.RunId : WorkFixture.SandboxId), read.ScopeId);
                return Task.FromResult(WorkFixture.Reply(new ResourceServiceGetBudgetResponse
                {
                    Meta = WorkFixture.Meta(read.Meta, policyRevision),
                    Value = new() { Snapshot = ResourceSample(run, policyRevision, "3000") },
                }));
            }

            var write = WorkFixture.Read(request, SetResourcePolicy, ResourceServiceSetPolicyRequest.Parser);
            Assert.Equal("run", write.ScopeKind);
            Assert.Equal(WorkFixture.Id(WorkFixture.RunId), write.ScopeId);
            Assert.Equal(WorkFixture.Id(WorkFixture.OwnerEpoch), write.OwnerEpoch);
            Assert.Equal(WorkFixture.Revision, write.Meta.ExpectedRev.Value);
            Assert.Equal(1500U, write.Policy.CpuMaxMillicores);
            Assert.Equal(1_073_741_824UL, write.Policy.MemoryMaxBytes);
            Assert.Equal(PressureAction.Throttle, write.Policy.PressureAction);
            var response = new ResourceServiceSetPolicyResponse { Meta = WorkFixture.Meta(write.Meta, policyRevision + 1) };
            if (conflict)
            {
                response.Error = new ArcError { Category = ErrorCategory.Conflict, Code = "revision_mismatch", Effect = EffectCertainty.DidNotHappen };
            }
            else
            {
                var operation = WorkFixture.Operation(write.Meta.CommandId, write.OwnerEpoch, OperationState.Succeeded, EffectCertainty.Happened);
                operation.Revision = new() { Value = policyRevision + 1 };
                operation.Method = "ResourceService.SetPolicy";
                response.Value = new() { Snapshot = ResourceSample(true, policyRevision + 1, "1500"), Operation = operation };
            }
            return Task.FromResult(WorkFixture.Reply(response));
        });
        using var context = CreateContext(server);
        var view = Render<ResourceOverview>(context, parameters => parameters
            .Add(component => component.Target, WorkFixture.Target)
            .Add(component => component.RunId, WorkFixture.RunId));
        view.WaitForAssertion(() => Assert.Contains("launch pressure policy", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Lower Run limits", view.Markup, StringComparison.Ordinal);
        Assert.Contains("Fresh at observation", view.Markup, StringComparison.Ordinal);

        policyRevision = WorkFixture.Revision;
        await ClickAsync(view, "Refresh");
        await ClickAsync(view, "Lower Run limits");
        await view.InvokeAsync(() =>
        {
            var form = "form[aria-describedby='dashboard-resource-policy-help']";
            view.Find(form + " input").Change("1500");
            view.Find(form).Submit();
        });

        view.WaitForAssertion(() => Assert.Contains(conflict ? "Refresh it before trying again" : "The device recorded command", view.Markup, StringComparison.Ordinal));
        var limits = view.Find("table[aria-label='Requested, applied and read-back limits']").TextContent;
        Assert.Contains(conflict ? "3000" : "1500", limits, StringComparison.Ordinal);
        Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(SetResourcePolicy, StringComparison.Ordinal));
    }

    private static ResourceSnapshot ResourceSample(bool run, long policyRevision, string cpu) => new()
    {
        ScopeKind = run ? "run" : "sandbox",
        ScopeId = WorkFixture.Id(run ? WorkFixture.RunId : WorkFixture.SandboxId),
        PolicyRevision = new() { Value = policyRevision },
        SampledAt = new() { UnixSeconds = 1_800_000_000 },
        SampleEpoch = "fixture-sampler",
        SampleSequence = (ulong)policyRevision,
        Stale = false,
        ManagedCpuMillicores = 500,
        ManagedMemoryBytes = 1000,
        Limits = { EffectiveLimit("cpu", "millicores", cpu, policyRevision), EffectiveLimit("memory", "bytes", "1073741824", policyRevision) },
    };

    private static LimitState EffectiveLimit(string dimension, string unit, string value, long revision) => new()
    {
        Dimension = dimension,
        Unit = unit,
        DesiredValue = value,
        AppliedValue = value,
        ReadbackValue = value,
        Status = LimitStatus.Effective,
        DesiredRevision = new() { Value = revision },
    };

    private static HttpResponseMessage InspectReply(CapturedRequest request, Run run)
    {
        var inspect = WorkFixture.Read(request, InspectRun, RunServiceInspectRequest.Parser);
        Assert.Equal(WorkFixture.Id(WorkFixture.RunId), inspect.RunId);
        return WorkFixture.Reply(new RunServiceInspectResponse
        {
            Meta = WorkFixture.Meta(inspect.Meta, run.Revision.Value),
            Value = new() { Run = run },
        });
    }

    private static BunitContext CreateContext(ScriptedServer server)
    {
        var center = WorkFixture.Center(server);
        var context = new BunitContext();
        context.Services.AddSingleton(center);
        context.Services.AddSingleton(Assert.IsAssignableFrom<IDashboardWorkspaces>(center));
        context.Services.AddSingleton(TimeProvider.System);
        return context;
    }

    private static IRenderedComponent<DashboardLayout> Render<T>(BunitContext context, Action<ComponentParameterCollectionBuilder<T>> configure)
        where T : IComponent => context.Render<DashboardLayout>(parameters => parameters.Add<T>(layout => layout.Body, configure));

    private static IRenderedComponent<DashboardLayout> RenderRun(BunitContext context, DashboardWorkTarget target) =>
        Render<RunDetailsView>(context, parameters => parameters
            .Add(component => component.Target, target)
            .Add(component => component.DeviceId, WorkFixture.DeviceId)
            .Add(component => component.RunId, WorkFixture.RunId));

    private static AngleSharp.Dom.IElement FindButton(IRenderedComponent<DashboardLayout> view, string text) =>
        Assert.Single(view.FindAll("button"), button => button.TextContent.Trim().Equals(text, StringComparison.Ordinal));

    private static Task ClickAsync(IRenderedComponent<DashboardLayout> view, string text) => FindButton(view, text).ClickAsync(new MouseEventArgs());
}
