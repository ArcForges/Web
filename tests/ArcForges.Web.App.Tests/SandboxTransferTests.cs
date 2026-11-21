// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using ArcForges.Web.App.Pages;
using Bunit;
using Google.Protobuf;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class SandboxTransferTests
{
    private const string Data = "/arcforges.sandbox.v2.DataService/";
    private const string GetOperation = "/arcforges.sandbox.v2.NodeService/GetOperation";
    private static readonly Guid TransferId = Guid.Parse("a91a9009-37e8-429a-a6b4-cdeafe90c139");

    [Fact]
    public async Task LostBeginReconcilesTheOriginalExportAndDownloadingDoesNotReleaseIt()
    {
        var bytes = "verified snapshot"u8.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var transfer = new Transfer
        {
            TransferId = WorkFixture.Id(TransferId),
            SandboxId = WorkFixture.Id(WorkFixture.SandboxId),
            State = TransferState.Complete,
            RelativePath = "work/output.log",
            Direction = "export",
            ByteLength = (ulong)bytes.Length,
            VerifiedOffset = (ulong)bytes.Length,
            ExpiresAt = Instant(DateTimeOffset.UtcNow.AddMinutes(15)),
            Sha256 = digest,
        };
        DataServiceBeginExportRequest? original = null;
        var inspections = 0;
        var ranges = 0;
        var closes = 0;
        var leases = new List<bool>();

        using var server = WorkFixture.Server((request, _) =>
        {
            switch (request.Url.AbsolutePath)
            {
                case "/api" + Data + "BeginExport":
                    Assert.Null(original);
                    original = WorkFixture.Read(request, Data + "BeginExport", DataServiceBeginExportRequest.Parser);
                    Assert.Equal(WorkFixture.Revision, original.Meta.ExpectedRev.Value);
                    Assert.Equal(WorkFixture.Id(WorkFixture.OwnerEpoch), original.OwnerEpoch);
                    Assert.Equal(transfer.SandboxId, original.SandboxId);
                    Assert.Equal(transfer.RelativePath, original.RelativePath);
                    throw new HttpRequestException("Fixture lost the reply to BeginExport.");
                case "/api" + GetOperation:
                    var lookup = WorkFixture.Read(request, GetOperation, NodeServiceGetOperationRequest.Parser);
                    Assert.NotNull(original);
                    Assert.Equal(original.Meta.CommandId, lookup.CommandId);
                    Assert.Equal(original.OwnerEpoch, lookup.OwnerEpoch);
                    Assert.Null(lookup.Meta.CommandId);
                    Assert.Null(lookup.Meta.ExpectedRev);
                    return Task.FromResult(WorkFixture.Reply(new NodeServiceGetOperationResponse
                    {
                        Meta = WorkFixture.Meta(lookup.Meta),
                        Value = new()
                        {
                            Operation = new Operation
                            {
                                CommandId = lookup.CommandId,
                                OwnerEpoch = lookup.OwnerEpoch,
                                Method = "DataService.BeginExport",
                                AggregateId = transfer.TransferId,
                                State = OperationState.Succeeded,
                                Effect = EffectCertainty.Happened,
                            },
                        },
                    }));
                case "/api" + Data + "InspectTransfer":
                    var inspect = WorkFixture.Read(request, Data + "InspectTransfer", DataServiceInspectTransferRequest.Parser);
                    Assert.Equal(transfer.TransferId, inspect.TransferId);
                    inspections++;
                    return Task.FromResult(WorkFixture.Reply(new DataServiceInspectTransferResponse
                    {
                        Meta = WorkFixture.Meta(inspect.Meta, 17),
                        Value = new() { Transfer = transfer },
                    }));
                case "/api" + Data + "ReadExport":
                    var range = WorkFixture.Read(request, Data + "ReadExport", DataServiceReadExportRequest.Parser);
                    Assert.Equal(transfer.TransferId, range.TransferId);
                    Assert.Equal(0UL, range.Offset);
                    Assert.Equal((uint)bytes.Length, range.MaxBytes);
                    ranges++;
                    return Task.FromResult(WorkFixture.Reply(new DataServiceReadExportResponse
                    {
                        Meta = WorkFixture.Meta(range.Meta, 17),
                        Value = new() { Chunk = new() { Offset = 0, Eof = true, Data = ByteString.CopyFrom(bytes) } },
                    }));
                case "/api" + Data + "CloseTransfer":
                    var close = WorkFixture.Read(request, Data + "CloseTransfer", DataServiceCloseTransferRequest.Parser);
                    Assert.NotNull(original);
                    Assert.Equal(original.OwnerEpoch, close.OwnerEpoch);
                    Assert.NotEqual(original.Meta.CommandId, close.Meta.CommandId);
                    Assert.Equal(17L, close.Meta.ExpectedRev.Value);
                    Assert.Equal(transfer.TransferId, close.TransferId);
                    closes++;
                    var released = transfer.Clone();
                    released.ClosedAt = Instant(DateTimeOffset.UtcNow);
                    return Task.FromResult(WorkFixture.Reply(new DataServiceCloseTransferResponse
                    {
                        Meta = WorkFixture.Meta(close.Meta, 18),
                        Value = new() { Transfer = released },
                    }));
                default:
                    throw new InvalidOperationException("Unexpected transfer fixture endpoint.");
            }
        });

        using var context = new BunitContext();
        var center = WorkFixture.Center(server);
        context.Services.AddSingleton(center);
        context.Services.AddSingleton(Assert.IsAssignableFrom<IDashboardWorkspaces>(center));
        context.Services.AddSingleton(TimeProvider.System);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var download = context.JSInterop.SetupModule("./" + WasmProfilePolicyTests.ActivityModulePath);
        download.Mode = JSRuntimeMode.Loose;
        var view = context.Render<DashboardLayout>(parameters => parameters.Add<SandboxTransfers>(layout => layout.Body, transferParameters =>
            transferParameters.Add(component => component.Target, WorkFixture.Target)
                .Add(component => component.SandboxRevision, WorkFixture.Revision)
                .Add(component => component.LeaseChanged, value => leases.Add(value))));
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("section[aria-labelledby='sandbox-transfers'] form")));

        await view.InvokeAsync(() =>
        {
            var form = view.FindAll("section[aria-labelledby='sandbox-transfers'] form")[1];
            var input = form.QuerySelector("input");
            Assert.NotNull(input);
            input.Input(transfer.RelativePath);
            form.Submit();
        });
        view.WaitForAssertion(() => Assert.NotNull(Button(view, "Check original command")));
        Assert.Empty(view.FindAll("section[aria-labelledby='sandbox-transfers'] form"));
        Assert.Equal([true], leases);

        await Button(view, "Check original command").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Contains("Retained for download", view.Markup, StringComparison.Ordinal));
        Assert.Contains(digest, view.Markup, StringComparison.Ordinal);
        await Button(view, "Download verified file").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Contains("Verified file sent to your browser", view.Markup, StringComparison.Ordinal));
        Assert.Single(download.Invocations["saveVerifiedFile"]);
        Assert.Equal(2, inspections);
        Assert.Equal(1, ranges);
        Assert.Equal(0, closes);
        Assert.Equal([true], leases);
        Assert.Contains("Retained for download", view.Markup, StringComparison.Ordinal);

        await Button(view, "Release snapshot").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Contains("Released by the Node", view.Markup, StringComparison.Ordinal));
        Assert.Contains("Materialized and verified", view.Markup, StringComparison.Ordinal);
        Assert.Equal(1, closes);
        Assert.Equal([true, false], leases);
    }

    private static Instant Instant(DateTimeOffset value) => new() { UnixSeconds = value.ToUnixTimeSeconds() };

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<DashboardLayout> view, string label) =>
        Assert.Single(view.FindAll("button"), button => button.TextContent.Trim().Equals(label, StringComparison.Ordinal));
}
