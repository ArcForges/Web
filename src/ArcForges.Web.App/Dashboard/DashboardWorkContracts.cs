// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.Sandbox.V2;
using Google.Protobuf;

namespace ArcForges.Web.App.Dashboard;

/// <summary>Typed routes for an exact, locally authorized Node target.</summary>
public interface IDashboardWorkspaces
{
    Task<SandboxServiceListValue> ListSandboxesAsync(DashboardWorkTarget target, string? cursor, CancellationToken ct);
    Task<Sandbox> InspectSandboxAsync(DashboardWorkTarget target, CancellationToken ct);
    Task<Sandbox> CreateSandboxAsync(DashboardWorkTarget target, DashboardWorkCommand command, string name, SandboxConfig config, long templateRevision, CancellationToken ct);

    Task<RunServiceListValue> ListRunsAsync(DashboardWorkTarget target, string? cursor, CancellationToken ct);
    Task<Run> InspectRunAsync(DashboardWorkTarget target, Guid runId, CancellationToken ct);
    Task<RunServiceStartValue> StartRunAsync(DashboardWorkTarget target, DashboardWorkCommand command, IReadOnlyList<string> argv, string? workingDirectory, CancellationToken ct);
    Task<RunServiceStopValue> StopRunAsync(DashboardWorkTarget target, DashboardWorkCommand command, Guid runId, uint? graceMs, CancellationToken ct);
    IAsyncEnumerable<RunServiceWatchOutputFrame> WatchRunOutputAsync(DashboardWorkTarget target, Guid runId, StreamKind stream, string? epoch, ulong? offset, CancellationToken ct);

    Task<ResourceSnapshot> GetResourceBudgetAsync(DashboardWorkTarget target, Guid? runId, CancellationToken ct);
    Task<ResourceServiceSetPolicyValue> SetResourcePolicyAsync(DashboardWorkTarget target, DashboardWorkCommand command, Guid? runId, ResourcePolicy policy, CancellationToken ct);
    IAsyncEnumerable<ResourceServiceWatchUsageFrame> WatchResourceUsageAsync(DashboardWorkTarget target, Guid? runId, uint intervalMs, CancellationToken ct);

    Task<Operation> GetOperationAsync(DashboardWorkTarget target, DashboardWorkCommand command, CancellationToken ct);
    Task<NodeServiceListRemoteGrantsValue> ListRemoteGrantsAsync(DashboardWorkTarget target, string? cursor, CancellationToken ct);

    Task<DashboardTransferReceipt> BeginImportAsync(DashboardWorkTarget target, DashboardWorkCommand command, string relativePath, ulong length, string sha256, bool overwrite, string? expectedFileVersion, CancellationToken ct);
    Task<DashboardTransferReceipt> WriteImportAsync(DashboardWorkTarget target, DashboardWorkCommand command, Guid transferId, ulong offset, ByteString chunk, CancellationToken ct);
    Task<DashboardTransferCommitReceipt> CommitImportAsync(DashboardWorkTarget target, DashboardWorkCommand command, Guid transferId, CancellationToken ct);
    Task<DashboardTransferReceipt> BeginExportAsync(DashboardWorkTarget target, DashboardWorkCommand command, string relativePath, string? expectedFileVersion, CancellationToken ct);
    Task<DataChunk> ReadExportAsync(DashboardWorkTarget target, Guid transferId, ulong offset, uint maxBytes, CancellationToken ct);
    Task<DashboardTransferReceipt> InspectTransferAsync(DashboardWorkTarget target, Guid transferId, CancellationToken ct);
    Task<DashboardTransferReceipt> CloseTransferAsync(DashboardWorkTarget target, DashboardWorkCommand command, Guid transferId, CancellationToken ct);
}

/// <summary>The original command identity and aggregate precondition used for an effect.</summary>
public sealed record DashboardWorkCommand(Guid CommandId, Guid OwnerEpoch, long? ExpectedRevision = null);

public sealed record DashboardTransferReceipt(Transfer Transfer, long Revision);

public sealed record DashboardTransferCommitReceipt(Transfer Transfer, long Revision, Operation Operation);
