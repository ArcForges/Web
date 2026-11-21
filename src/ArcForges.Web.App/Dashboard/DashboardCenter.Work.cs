// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using DataClient = ArcForges.Contracts.Sandbox.V2.DataService.DataServiceClient;
using NodeClient = ArcForges.Contracts.Sandbox.V2.NodeService.NodeServiceClient;
using ResourceClient = ArcForges.Contracts.Sandbox.V2.ResourceService.ResourceServiceClient;
using RunClient = ArcForges.Contracts.Sandbox.V2.RunService.RunServiceClient;
using SandboxClient = ArcForges.Contracts.Sandbox.V2.SandboxService.SandboxServiceClient;

namespace ArcForges.Web.App.Dashboard;

internal sealed partial class DashboardCenter
{
    // The Center limits framed requests. The gRPC client limit counts protobuf bytes.
    private const int FrameHeaderBytes = 5;
    private const int WorkRequestBytes = 253_952 - FrameHeaderBytes;
    private const int WorkReplyBytes = 1_048_576 - FrameHeaderBytes;
    private const int WatchRequestBytes = 4_096 - FrameHeaderBytes;
    private const int OutputReplyBytes = 65_536;
    private const int TransferWireBytes = 2_097_152 - FrameHeaderBytes;
    private const int TransferChunkBytes = 1_048_576;
    private const uint WorkPageSize = 30;
    private static readonly TimeSpan DataDeadline = TimeSpan.FromSeconds(30);

    public Task<SandboxServiceListValue> ListSandboxesAsync(DashboardWorkTarget target, string? cursor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.SandboxId is null || !string.IsNullOrEmpty(cursor))
            throw WorkValidation();

        return WorkUnaryAsync(target, ct,
            (channel, session, options) => new SandboxClient(channel).ListAsync(
                new SandboxServiceListRequest { Meta = Meta(session), Limit = 1 }, options),
            static reply => (reply.Value, reply.Error, reply.Meta),
            (page, _) => page.Page is not null && page.Page.NextCursor.Length == 0 && page.Sandboxes.Count <= 1 &&
                page.Sandboxes.All(item => ValidSandbox(item, target.SandboxId)));
    }

    public Task<Sandbox> InspectSandboxAsync(DashboardWorkTarget target, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new SandboxClient(channel).InspectAsync(
                new SandboxServiceInspectRequest { Meta = Meta(session), SandboxId = SandboxIdentity(target) }, options),
            static reply => (reply.Value?.Sandbox, reply.Error, reply.Meta),
            (sandbox, meta) => ValidSandbox(sandbox, target.SandboxId) && meta?.ResultRev?.Value == sandbox.Revision.Value);

    public Task<Sandbox> CreateSandboxAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        string name, SandboxConfig config, long templateRevision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (target.SandboxId is not null || templateRevision < 1 || string.IsNullOrWhiteSpace(name))
            throw WorkValidation();

        return WorkUnaryAsync(target, ct,
            (channel, session, options) => new SandboxClient(channel).CreateAsync(BoundRequest(new SandboxServiceCreateRequest
            {
                Meta = WorkMutationMeta(session, target, command, existing: false),
                OwnerEpoch = ExactId(command.OwnerEpoch),
                ProvisionGrantId = ExactId(target.GrantId),
                TemplateRevision = new Revision { Value = templateRevision },
                DisplayName = name,
                Config = config.Clone(),
            }), options),
            static reply => (reply.Value?.Sandbox, reply.Error, reply.Meta),
            static (sandbox, meta) => ValidSandbox(sandbox) && meta?.ResultRev?.Value == sandbox.Revision.Value,
            mutation: true);
    }

    public Task<RunServiceListValue> ListRunsAsync(DashboardWorkTarget target, string? cursor, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) =>
            {
                var request = new RunServiceListRequest { Meta = Meta(session), SandboxId = SandboxIdentity(target), Limit = WorkPageSize };
                if (PageCursor(cursor) is { } next)
                    request.Cursor = next;
                return new RunClient(channel).ListAsync(request, options);
            },
            static reply => (reply.Value, reply.Error, reply.Meta),
            (page, _) => ValidPage(page.Page) && page.Runs.Count <= WorkPageSize &&
                page.Runs.All(run => ValidRun(run, target)));

    public Task<Run> InspectRunAsync(DashboardWorkTarget target, Guid runId, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new RunClient(channel).InspectAsync(
                new RunServiceInspectRequest { Meta = Meta(session), RunId = RunIdentity(target, runId) }, options),
            static reply => (reply.Value?.Run, reply.Error, reply.Meta),
            (run, meta) => ValidRun(run, target, runId) && meta?.ResultRev?.Value == run.Revision.Value);

    public Task<RunServiceStartValue> StartRunAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        IReadOnlyList<string> argv, string? workingDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(argv);
        if (argv.Count is < 1 or > 256 || string.IsNullOrWhiteSpace(argv[0]) ||
            argv.Any(argument => argument is null || Encoding.UTF8.GetByteCount(argument) > 32_768))
        {
            throw WorkValidation();
        }

        return WorkUnaryAsync(target, ct,
            (channel, session, options) =>
            {
                var request = new RunServiceStartRequest
                {
                    Meta = WorkMutationMeta(session, target, command),
                    SandboxId = SandboxIdentity(target),
                    OwnerEpoch = ExactId(command.OwnerEpoch),
                    TerminalMode = TerminalMode.Pipes,
                };
                request.Argv.AddRange(argv);
                if (!string.IsNullOrEmpty(workingDirectory))
                    request.WorkingDirectory = workingDirectory;
                return new RunClient(channel).StartAsync(BoundRequest(request), options);
            },
            static reply => (reply.Value, reply.Error, reply.Meta),
            (value, _) => ValidRun(value.Run, target) && ValidOperation(value.Operation, command, ReadId(value.Run.RunId)),
            mutation: true);
    }

    public Task<RunServiceStopValue> StopRunAsync(DashboardWorkTarget target, DashboardWorkCommand command, Guid runId,
        uint? graceMs, CancellationToken ct)
    {
        if (graceMs is > 60_000)
            throw WorkValidation();
        return WorkUnaryAsync(target, ct,
            (channel, session, options) =>
            {
                var request = new RunServiceStopRequest
                {
                    Meta = WorkMutationMeta(session, target, command),
                    RunId = RunIdentity(target, runId),
                    OwnerEpoch = ExactId(command.OwnerEpoch),
                };
                if (graceMs is uint grace)
                    request.GraceMs = grace;
                return new RunClient(channel).StopAsync(request, options);
            },
            static reply => (reply.Value, reply.Error, reply.Meta),
            (value, _) => ValidRun(value.Run, target, runId) && ValidOperation(value.Operation, command, runId),
            mutation: true);
    }

    public async IAsyncEnumerable<RunServiceWatchOutputFrame> WatchRunOutputAsync(DashboardWorkTarget target, Guid runId,
        StreamKind stream, string? epoch, ulong? offset, [EnumeratorCancellation] CancellationToken ct)
    {
        if (stream is not (StreamKind.Stdout or StreamKind.Stderr or StreamKind.Terminal))
            throw WorkValidation();
        var session = WorkSession(target, ct);
        var request = new RunServiceWatchOutputRequest { Meta = StreamDataMeta(session), RunId = RunIdentity(target, runId), Stream = stream };
        var next = offset.GetValueOrDefault();
        if (epoch is not null)
            request.Epoch = epoch;
        if (offset is ulong cursor)
            request.Offset = cursor;
        using var channel = OpenWorkChannel(session, WatchRequestBytes, OutputReplyBytes);
        using var call = new RunClient(channel).WatchOutput(BoundRequest(request, WatchRequestBytes), Options(ct, WorkHeaders(target)));
        while (await NextWorkFrameAsync(call.ResponseStream, session, ct))
        {
            var frame = call.ResponseStream.Current;
            Accept(frame, frame.Error, frame.Meta, session);
            if (frame.Chunk is { HasStream: true, HasOffset: true } chunk && chunk.Stream == stream && chunk.Offset == next &&
                chunk.Data.Length is > 0 and <= 65_536 &&
                chunk.NextOffset > next && chunk.NextOffset - next == (ulong)chunk.Data.Length &&
                !string.IsNullOrEmpty(chunk.Epoch) && (epoch is null || chunk.Epoch == epoch))
            {
                next = chunk.NextOffset;
                epoch = chunk.Epoch;
            }
            else if (frame.Gap is { HasRequestedOffset: true, HasEarliestOffset: true } gap &&
                !string.IsNullOrEmpty(gap.Epoch) && gap.RequestedOffset == next)
            {
                if (gap.Reason == "epoch_changed" && epoch is not null && gap.Epoch != epoch)
                {
                    yield return frame;
                    yield break;
                }
                if (gap.Reason != "retention" || gap.EarliestOffset <= next || (epoch is not null && gap.Epoch != epoch))
                    throw InvalidResult(frame.Meta);
                next = gap.EarliestOffset;
                epoch = gap.Epoch;
            }
            else if (ValidRun(frame.Run, target, runId) && (epoch is null || frame.Run.Epoch == epoch))
            {
                yield return frame;
                yield break;
            }
            else
            {
                throw InvalidResult(frame.Meta);
            }
            yield return frame;
        }
    }

    public Task<ResourceSnapshot> GetResourceBudgetAsync(DashboardWorkTarget target, Guid? runId, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new ResourceClient(channel).GetBudgetAsync(new ResourceServiceGetBudgetRequest
            {
                Meta = Meta(session),
                ScopeKind = ResourceKind(runId),
                ScopeId = ResourceIdentity(target, runId),
            }, options),
            static reply => (reply.Value?.Snapshot, reply.Error, reply.Meta),
            (snapshot, _) => ValidSnapshot(snapshot, target, runId));

    public Task<ResourceServiceSetPolicyValue> SetResourcePolicyAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        Guid? runId, ResourcePolicy policy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return WorkUnaryAsync(target, ct,
            (channel, session, options) => new ResourceClient(channel).SetPolicyAsync(BoundRequest(new ResourceServiceSetPolicyRequest
            {
                Meta = WorkMutationMeta(session, target, command),
                ScopeKind = ResourceKind(runId),
                ScopeId = ResourceIdentity(target, runId),
                OwnerEpoch = ExactId(command.OwnerEpoch),
                Policy = policy.Clone(),
            }), options),
            static reply => (reply.Value, reply.Error, reply.Meta),
            (value, _) => ValidSnapshot(value.Snapshot, target, runId) && ValidOperation(value.Operation, command),
            mutation: true);
    }

    public async IAsyncEnumerable<ResourceServiceWatchUsageFrame> WatchResourceUsageAsync(DashboardWorkTarget target, Guid? runId,
        uint intervalMs, [EnumeratorCancellation] CancellationToken ct)
    {
        if (intervalMs is < 1_000 or > 60_000)
            throw WorkValidation();
        var session = WorkSession(target, ct);
        var request = new ResourceServiceWatchUsageRequest
        {
            Meta = StreamDataMeta(session),
            ScopeKind = ResourceKind(runId),
            ScopeId = ResourceIdentity(target, runId),
            IntervalMs = intervalMs,
        };
        using var channel = OpenWorkChannel(session, WatchRequestBytes, WorkReplyBytes);
        using var call = new ResourceClient(channel).WatchUsage(request, Options(ct, WorkHeaders(target)));
        while (await NextWorkFrameAsync(call.ResponseStream, session, ct))
        {
            var frame = call.ResponseStream.Current;
            var snapshot = Accept(frame.Snapshot, frame.Error, frame.Meta, session);
            if (!ValidSnapshot(snapshot, target, runId) || snapshot.SampleSequence == 0 || string.IsNullOrEmpty(snapshot.SampleEpoch))
                throw InvalidResult(frame.Meta);
            yield return frame;
        }
    }

    public Task<Operation> GetOperationAsync(DashboardWorkTarget target, DashboardWorkCommand command, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new NodeClient(channel).GetOperationAsync(new NodeServiceGetOperationRequest
            {
                Meta = Meta(session),
                CommandId = ExactId(command.CommandId),
                OwnerEpoch = ExactId(command.OwnerEpoch),
            }, options),
            static reply => (reply.Value?.Operation, reply.Error, reply.Meta),
            (operation, _) => ValidOperation(operation, command));

    public Task<NodeServiceListRemoteGrantsValue> ListRemoteGrantsAsync(DashboardWorkTarget target, string? cursor, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(cursor))
            throw WorkValidation();

        return WorkUnaryAsync(target, ct,
            (channel, session, options) =>
            {
                var request = new NodeServiceListRemoteGrantsRequest { Meta = Meta(session), Limit = 1 };
                if (target.SandboxId is Guid sandbox)
                    request.SandboxId = ExactId(sandbox);
                return new NodeClient(channel).ListRemoteGrantsAsync(request, options);
            },
            static reply => (reply.Value, reply.Error, reply.Meta),
            (page, _) => page.Page is not null && page.Page.NextCursor.Length == 0 && page.Grants.Count == 1 &&
                page.Grants.All(grant => ReadId(grant.GrantId) == target.GrantId && ReadId(grant.EnrollmentId) == target.EnrollmentId &&
                    grant.Revision?.Value == target.GrantRevision && (target.SandboxId is null || ReadId(grant.SandboxId) == target.SandboxId)));
    }

    public Task<DashboardTransferReceipt> BeginImportAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        string relativePath, ulong length, string sha256, bool overwrite, string? expectedFileVersion, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(relativePath) || !ValidDigest(sha256) ||
            (overwrite && string.IsNullOrEmpty(expectedFileVersion)))
        {
            throw WorkValidation();
        }

        return WorkUnaryAsync(target, ct,
            (channel, session, options) =>
            {
                var request = new DataServiceBeginImportRequest
                {
                    Meta = WorkMutationMeta(session, target, command, streamData: true),
                    SandboxId = SandboxIdentity(target),
                    OwnerEpoch = ExactId(command.OwnerEpoch),
                    RelativePath = relativePath,
                    ByteLength = length,
                    Sha256 = sha256,
                    Overwrite = overwrite,
                };
                if (expectedFileVersion is not null)
                    request.ExpectedFileVersion = expectedFileVersion;
                return new DataClient(channel).BeginImportAsync(BoundRequest(request), options);
            },
            static reply => (TransferReceipt(reply.Value?.Transfer, reply.Meta), reply.Error, reply.Meta),
            (receipt, _) => ValidTransfer(receipt.Transfer, target, direction: "import"),
            mutation: true, deadline: DataDeadline);
    }

    public Task<DashboardTransferReceipt> WriteImportAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        Guid transferId, ulong offset, ByteString chunk, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length is < 1 or > TransferChunkBytes || offset > ulong.MaxValue - (ulong)chunk.Length)
            throw WorkValidation();
        return WorkUnaryAsync(target, ct,
            (channel, session, options) => new DataClient(channel).WriteImportAsync(BoundRequest(new DataServiceWriteImportRequest
            {
                Meta = WorkMutationMeta(session, target, command, streamData: true),
                OwnerEpoch = ExactId(command.OwnerEpoch),
                TransferId = TransferIdentity(target, transferId),
                Offset = offset,
                Chunk = chunk,
            }, TransferWireBytes), options),
            static reply => (TransferReceipt(reply.Value?.Transfer, reply.Meta), reply.Error, reply.Meta),
            (receipt, _) => ValidTransfer(receipt.Transfer, target, transferId, "import") &&
                receipt.Transfer.HasVerifiedOffset && receipt.Transfer.VerifiedOffset >= offset + (ulong)chunk.Length,
            mutation: true, deadline: DataDeadline, requestBytes: TransferWireBytes);
    }

    public Task<DashboardTransferCommitReceipt> CommitImportAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        Guid transferId, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new DataClient(channel).CommitImportAsync(new DataServiceCommitImportRequest
            {
                Meta = WorkMutationMeta(session, target, command, streamData: true),
                OwnerEpoch = ExactId(command.OwnerEpoch),
                TransferId = TransferIdentity(target, transferId),
            }, options),
            static reply => (CommitReceipt(reply.Value, reply.Meta), reply.Error, reply.Meta),
            (receipt, _) => ValidTransfer(receipt.Transfer, target, transferId, "import") &&
                ValidOperation(receipt.Operation, command, transferId),
            mutation: true, deadline: DataDeadline);

    public Task<DashboardTransferReceipt> BeginExportAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        string relativePath, string? expectedFileVersion, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(relativePath))
            throw WorkValidation();
        return WorkUnaryAsync(target, ct,
            (channel, session, options) =>
            {
                var request = new DataServiceBeginExportRequest
                {
                    Meta = WorkMutationMeta(session, target, command, streamData: true),
                    SandboxId = SandboxIdentity(target),
                    OwnerEpoch = ExactId(command.OwnerEpoch),
                    RelativePath = relativePath,
                };
                if (expectedFileVersion is not null)
                    request.ExpectedFileVersion = expectedFileVersion;
                return new DataClient(channel).BeginExportAsync(BoundRequest(request), options);
            },
            static reply => (TransferReceipt(reply.Value?.Transfer, reply.Meta), reply.Error, reply.Meta),
            (receipt, _) => ValidTransfer(receipt.Transfer, target, direction: "export"),
            mutation: true, deadline: DataDeadline);
    }

    public Task<DataChunk> ReadExportAsync(DashboardWorkTarget target, Guid transferId, ulong offset, uint maxBytes, CancellationToken ct)
    {
        if (maxBytes is < 1 or > TransferChunkBytes)
            throw WorkValidation();
        return WorkUnaryAsync(target, ct,
            (channel, session, options) => new DataClient(channel).ReadExportAsync(new DataServiceReadExportRequest
            {
                Meta = StreamDataMeta(session),
                TransferId = TransferIdentity(target, transferId),
                Offset = offset,
                MaxBytes = maxBytes,
            }, options),
            static reply => (reply.Value?.Chunk, reply.Error, reply.Meta),
            (chunk, _) => chunk.HasOffset && chunk.Offset == offset && chunk.HasEof &&
                chunk.Data.Length <= maxBytes && (chunk.Eof || !chunk.Data.IsEmpty),
            deadline: DataDeadline, responseBytes: TransferWireBytes);
    }

    public Task<DashboardTransferReceipt> InspectTransferAsync(DashboardWorkTarget target, Guid transferId, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new DataClient(channel).InspectTransferAsync(new DataServiceInspectTransferRequest
            {
                Meta = StreamDataMeta(session),
                TransferId = TransferIdentity(target, transferId),
            }, options),
            static reply => (TransferReceipt(reply.Value?.Transfer, reply.Meta), reply.Error, reply.Meta),
            (receipt, _) => ValidTransfer(receipt.Transfer, target, transferId));

    public Task<DashboardTransferReceipt> CloseTransferAsync(DashboardWorkTarget target, DashboardWorkCommand command,
        Guid transferId, CancellationToken ct) =>
        WorkUnaryAsync(target, ct,
            (channel, session, options) => new DataClient(channel).CloseTransferAsync(new DataServiceCloseTransferRequest
            {
                Meta = WorkMutationMeta(session, target, command, streamData: true),
                OwnerEpoch = ExactId(command.OwnerEpoch),
                TransferId = TransferIdentity(target, transferId),
            }, options),
            static reply => (TransferReceipt(reply.Value?.Transfer, reply.Meta), reply.Error, reply.Meta),
            (receipt, _) => ValidTransfer(receipt.Transfer, target, transferId),
            mutation: true, deadline: DataDeadline);

    private static T BoundRequest<T>(T request, int payloadBytes = WorkRequestBytes) where T : IMessage =>
        request.CalculateSize() <= payloadBytes
            ? request
            : throw new DashboardApiException(DashboardFailure.Validation, code: "request_too_large");

    private async Task<T> WorkUnaryAsync<TReply, T>(DashboardWorkTarget target, CancellationToken ct,
        Func<GrpcChannel, SessionState, CallOptions, AsyncUnaryCall<TReply>> invoke,
        Func<TReply, (T? Value, ArcError? Error, ResponseMeta? Meta)> outcome,
        Func<T, ResponseMeta?, bool> validate, bool mutation = false, TimeSpan? deadline = null,
        int requestBytes = WorkRequestBytes, int responseBytes = WorkReplyBytes) where T : class
    {
        var session = WorkSession(target, ct);
        using var channel = OpenWorkChannel(session, requestBytes, responseBytes);
        var reply = await CallAsync(invoke(channel, session, Options(ct, WorkHeaders(target), deadline)), session, ct, mutation);
        var (value, error, meta) = outcome(reply);
        var result = Accept(value, error, meta, session, mutation);
        if (!validate(result, meta))
            throw new DashboardApiException(mutation ? DashboardFailure.Uncertain : DashboardFailure.Incompatible, ReadId(meta?.CorrelationId));
        return result;
    }

    private SessionState WorkSession(DashboardWorkTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ct.ThrowIfCancellationRequested();
        if (target.NodeId == Guid.Empty || target.EnrollmentId == Guid.Empty || target.GrantId == Guid.Empty ||
            target.GrantRevision <= 0 || target.SandboxId == Guid.Empty || target.OwnerEpoch == Guid.Empty)
        {
            throw WorkValidation();
        }
        return RequireSession();
    }

    private static Metadata WorkHeaders(DashboardWorkTarget target) =>
        WorkHeaders(target.NodeId, target.EnrollmentId, target.GrantId, target.GrantRevision);

    private static Metadata WorkHeaders(Guid node, Guid enrollment, Guid grant, long revision) => new()
    {
        { "x-arcforges-node-id", node.ToString("D") },
        { "x-arcforges-enrollment-id", enrollment.ToString("D") },
        { "x-arcforges-grant-id", grant.ToString("D") },
        { "x-arcforges-grant-revision", revision.ToString(CultureInfo.InvariantCulture) },
    };

    private static RequestMeta WorkMutationMeta(SessionState session, DashboardWorkTarget target, DashboardWorkCommand command,
        bool existing = true, bool streamData = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (target.OwnerEpoch is null || target.OwnerEpoch != command.OwnerEpoch ||
            (existing ? command.ExpectedRevision is not > 0 : command.ExpectedRevision is not null))
        {
            throw WorkValidation();
        }

        var meta = streamData ? StreamDataMeta(session) : Meta(session);
        meta.CommandId = ExactId(command.CommandId);
        if (command.ExpectedRevision is long revision)
            meta.ExpectedRev = new Revision { Value = revision };
        return meta;
    }

    private static RequestMeta StreamDataMeta(SessionState session)
    {
        var meta = Meta(session);
        // The published V2 Node Watch/Data admission accepts correlation and the operation's CAS fields.
        meta.ClearRecoveryGeneration();
        return meta;
    }

    private async Task<bool> NextWorkFrameAsync<T>(IAsyncStreamReader<T> reader, SessionState session, CancellationToken ct)
    {
        bool hasFrame;
        try
        {
            hasFrame = await reader.MoveNext(ct);
        }
        catch (RpcException error)
        {
            ct.ThrowIfCancellationRequested();
            if (error.StatusCode == StatusCode.DeadlineExceeded)
                return false;
            throw RpcFailure(error, session);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DashboardApiException(DashboardFailure.Unavailable);
        }
        EnsureCurrent(session, ct);
        return hasFrame;
    }

    private static string? PageCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        return Encoding.UTF8.GetByteCount(cursor) <= 4_096 ? cursor : throw WorkValidation();
    }

    private static bool ValidPage(Page? page) => page is not null && Encoding.UTF8.GetByteCount(page.NextCursor) <= 4_096;

    private static Id SandboxIdentity(DashboardWorkTarget target) => ExactId(target.SandboxId ?? Guid.Empty);

    private static Id RunIdentity(DashboardWorkTarget target, Guid runId)
    {
        if (target.SandboxId is null)
            throw WorkValidation();
        return ExactId(runId);
    }

    private static Id TransferIdentity(DashboardWorkTarget target, Guid transferId)
    {
        if (target.SandboxId is null)
            throw WorkValidation();
        return ExactId(transferId);
    }

    private static string ResourceKind(Guid? runId) => runId is null ? "sandbox" : "run";

    private static Id ResourceIdentity(DashboardWorkTarget target, Guid? runId) =>
        runId is Guid run ? RunIdentity(target, run) : SandboxIdentity(target);

    private static bool ValidSandbox(Sandbox? sandbox, Guid? expected = null) =>
        ReadId(sandbox?.SandboxId) is Guid found && (expected is null || expected == found) && sandbox!.Revision is { Value: > 0 };

    private static bool ValidRun(Run? run, DashboardWorkTarget target, Guid? runId = null) =>
        ReadId(run?.RunId) is Guid found && (runId is null || runId == found) &&
        ReadId(run!.SandboxId) is Guid sandbox && sandbox == target.SandboxId && run.Revision is { Value: > 0 };

    private static bool ValidOperation(Operation? operation, DashboardWorkCommand command, Guid? aggregateId = null) =>
        operation is not null && ReadId(operation.CommandId) == command.CommandId && ReadId(operation.OwnerEpoch) == command.OwnerEpoch &&
        operation.HasState && operation.HasEffect && (aggregateId is null || ReadId(operation.AggregateId) == aggregateId);

    private static bool ValidSnapshot(ResourceSnapshot? snapshot, DashboardWorkTarget target, Guid? runId) =>
        snapshot is not null && ReadId(snapshot.ScopeId) == (runId ?? target.SandboxId) &&
        snapshot.ScopeKind == ResourceKind(runId) && snapshot.SampledAt is not null && snapshot.HasStale;

    private static DashboardTransferReceipt? TransferReceipt(Transfer? transfer, ResponseMeta? meta) =>
        transfer is not null && meta?.ResultRev is { Value: > 0 } revision ? new(transfer, revision.Value) : null;

    private static DashboardTransferCommitReceipt? CommitReceipt(DataServiceCommitImportValue? value, ResponseMeta? meta) =>
        value is { Transfer: { } transfer, Operation: { } operation } && meta?.ResultRev is { Value: > 0 } revision
            ? new(transfer, revision.Value, operation)
            : null;

    private static bool ValidTransfer(Transfer transfer, DashboardWorkTarget target, Guid? transferId = null, string? direction = null) =>
        ReadId(transfer.TransferId) is Guid found && (transferId is null || found == transferId) &&
        ReadId(transfer.SandboxId) is Guid sandbox && sandbox == target.SandboxId && transfer.HasState &&
        (direction is null ? transfer.Direction is "import" or "export" : transfer.Direction == direction);

    private static bool ValidDigest(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static DashboardApiException WorkValidation() => new(DashboardFailure.Validation);
}
