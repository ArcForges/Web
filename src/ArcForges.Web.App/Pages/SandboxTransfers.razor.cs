// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using Google.Protobuf;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ArcForges.Web.App.Pages;

public sealed partial class SandboxTransfers : IDisposable
{
    private const int BrowserBytes = 16 * 1024 * 1024;
    private const int ChunkBytes = 256 * 1024;
    private const string PreviousTransferNotice =
        "A previous transfer is still retained or unresolved. Use its original grant to check its status or request release. " +
        "The Node releases expired transfers when it can do so safely. The local owner can check the original command receipt " +
        "using its command ID and owner epoch, and request a fresh deletion preview.";

    [Inject] private IDashboardWorkspaces Work { get; set; } = default!;
    [Inject] private IJSRuntime Browser { get; set; } = default!;
    [CascadingParameter] private DashboardAccess Access { get; set; } = default!;
    [Parameter, EditorRequired] public DashboardWorkTarget Target { get; set; } = default!;
    [Parameter] public long SandboxRevision { get; set; }
    [Parameter] public EventCallback<bool> LeaseChanged { get; set; }

    private DashboardAccess? _appliedAccess;
    private DashboardWorkTarget? _appliedTarget;
    private CancellationTokenSource? _scope;
    private Intent? _intent;
    private Pending? _pending;
    private DashboardTransferReceipt? _receipt;
    private DashboardApiException? _error;
    private string? _notice;
    private IBrowserFile? _upload;
    private string _importPath = "";
    private string _importVersion = "";
    private string _exportPath = "";
    private string _exportVersion = "";
    private bool _overwrite;
    private bool _busy;
    private bool _canDismiss;
    private bool _reportedLease;
    private bool _requiresObservation;
    private bool _readOnlyOriginal;
    private bool _previousGrantLease;
    private int _fileInputKey;
    private int? _browserBytes;
    private int _downloadLength;
    private long? _blockedSandboxRevision;
    private ElementReference _transferHeading;
    private bool _focusTransferHeading;

    private bool ObservationStale => _requiresObservation || _pending is not null;
    private bool CanBegin => !_busy && _scope is { IsCancellationRequested: false } &&
        _intent is null && !_previousGrantLease && SandboxRevision > 0 && SandboxRevision != _blockedSandboxRevision &&
        Target.OwnerEpoch is Guid owner && owner != Guid.Empty &&
        Target.SandboxId is Guid sandbox && sandbox != Guid.Empty;

    private bool CanClose => _receipt is { Transfer: { ClosedAt: null, State: not TransferState.RecoveryRequired } } &&
        _pending is null && !_requiresObservation && !_readOnlyOriginal;

    private bool CanDownload => _receipt is { Transfer: { Direction: "export", State: TransferState.Complete, ClosedAt: null } transfer } &&
        transfer.ByteLength <= BrowserBytes && ToTime(transfer.ExpiresAt) > DateTimeOffset.UtcNow &&
        _pending is null && !_requiresObservation && !_readOnlyOriginal;

    private string CloseLabel => _receipt?.Transfer is { State: TransferState.Complete, Direction: "export" }
        ? "Release snapshot" : _receipt?.Transfer.State == TransferState.Complete ? "Close transfer" : "Cancel transfer";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusTransferHeading && _scope is { IsCancellationRequested: false })
        {
            _focusTransferHeading = false;
            await _transferHeading.FocusAsync();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (ReferenceEquals(_appliedAccess, Access) && Equals(_appliedTarget, Target))
            return;

        CancelScope();
        _appliedAccess = Access;
        _appliedTarget = Target;
        _scope = CancellationTokenSource.CreateLinkedTokenSource(Access.Cancellation);
        ClearForm();
        ClearTransfer();
        _busy = false;
        _focusTransferHeading = false;
        _blockedSandboxRevision = null;
        if (Access.TransferJourneys.TryGetValue(Target, out var current))
        {
            RestoreJourney(current);
        }
        else
        {
            foreach (var (old, previous) in Access.TransferJourneys)
            {
                if (old.NodeId != Target.NodeId || old.EnrollmentId != Target.EnrollmentId || old.SandboxId != Target.SandboxId)
                    continue;
                if (old.GrantId != Target.GrantId)
                {
                    _previousGrantLease = true;
                    continue;
                }
                RestoreJourney(previous);
                _readOnlyOriginal = true;
                break;
            }
            if (_readOnlyOriginal || _previousGrantLease)
                _notice = PreviousTransferNotice;
        }
        await PublishLeaseAsync();
    }

    private void RestoreJourney(SandboxTransferJourney journey)
    {
        _intent = journey.Intent;
        _pending = journey.Pending;
        _receipt = journey.Receipt;
        _requiresObservation = true;
        _notice = _pending is not null ? "Check the original command before taking another action." :
            "Check the transfer for its current state.";
    }

    private void SelectUpload(InputFileChangeEventArgs change)
    {
        _upload = change.FileCount == 1 && change.File.Size is >= 0 and <= BrowserBytes ? change.File : null;
        _notice = _upload is null ? "Choose one local file up to 16 MiB." : null;
        _error = null;
    }

    private Task ImportAsync()
    {
        if (!CanBegin || _upload is not IBrowserFile file)
            return Task.CompletedTask;
        var path = _importPath;
        var replace = _overwrite;
        var version = replace ? _importVersion : null;
        if (!ValidPath(path) || replace && !ValidVersion(version))
        {
            _notice = "Enter a Sandbox-relative destination and the current file version when replacing a file.";
            return Task.CompletedTask;
        }
        var target = Target;
        var start = NewCommand(target.OwnerEpoch!.Value, SandboxRevision);
        return RunAsync(async token =>
        {
            var bytes = new byte[checked((int)file.Size)];
            try
            {
                _notice = "Reading the selected local file.";
                await using (var input = file.OpenReadStream(BrowserBytes, token))
                    await input.ReadExactlyAsync(bytes.AsMemory(), token);
                token.ThrowIfCancellationRequested();

                var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                _intent = new Intent(target, "import", path, (ulong)bytes.Length, digest);
                _focusTransferHeading = true;
                ClearForm();
                await ApplyMutationAsync(new Pending(TransferPhase.BeginImport, start), () =>
                    Work.BeginImportAsync(target, start, path, (ulong)bytes.Length, digest, replace, version, token), token);

                for (var offset = 0; offset < bytes.Length; offset += ChunkBytes)
                {
                    var count = Math.Min(ChunkBytes, bytes.Length - offset);
                    var receipt = _receipt!;
                    var id = ReadId(receipt.Transfer.TransferId)!.Value;
                    var write = NewCommand(start.OwnerEpoch, receipt.Revision);
                    _notice = "Uploading the selected file.";
                    var chunk = ByteString.CopyFrom(bytes, offset, count);
                    await ApplyMutationAsync(new Pending(TransferPhase.WriteImport, write, (ulong)(offset + count)), () =>
                        Work.WriteImportAsync(target, write, id, (ulong)offset, chunk, token), token);
                }

                var ready = _receipt!;
                var transferId = ReadId(ready.Transfer.TransferId)!.Value;
                var commit = NewCommand(start.OwnerEpoch, ready.Revision);
                var pending = new Pending(TransferPhase.CommitImport, commit);
                _notice = "Committing the verified upload.";
                await ApplyMutationAsync(pending, async () =>
                {
                    var result = await Work.CommitImportAsync(target, commit, transferId, token);
                    if (!OperationMatches(result.Operation, pending, transferId) ||
                        result.Operation.State != OperationState.Succeeded || result.Operation.Effect != EffectCertainty.Happened)
                        throw new DashboardApiException(DashboardFailure.Uncertain);
                    return new DashboardTransferReceipt(result.Transfer, result.Revision);
                }, token);
                _notice = "The Node verified and committed the imported file.";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        });
    }

    private Task ExportAsync()
    {
        if (!CanBegin)
            return Task.CompletedTask;
        var path = _exportPath;
        var version = _exportVersion.Length == 0 ? null : _exportVersion;
        if (!ValidPath(path) || version is not null && !ValidVersion(version))
        {
            _notice = "Enter a Sandbox-relative source path and a valid file version if supplied.";
            return Task.CompletedTask;
        }
        var target = Target;
        var start = NewCommand(target.OwnerEpoch!.Value, SandboxRevision);
        return RunAsync(async token =>
        {
            _intent = new Intent(target, "export", path);
            _focusTransferHeading = true;
            ClearForm();
            _notice = "Preparing an export snapshot.";
            await ApplyMutationAsync(new Pending(TransferPhase.BeginExport, start), () =>
                Work.BeginExportAsync(target, start, path, version, token), token);
            await PollExportAsync(token);
        });
    }

    private async Task PollExportAsync(CancellationToken token)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await ReadCurrentAsync(token);
            if (_receipt!.Transfer.State is not (TransferState.Open or TransferState.Streaming))
                return;
            if (attempt < 5)
                await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        _notice = "The snapshot is still preparing. Check the transfer for the latest progress.";
    }

    private Task InspectAsync() => _receipt is not null && _pending is null ? RunAsync(ReadCurrentAsync) : Task.CompletedTask;

    private async Task ReadCurrentAsync(CancellationToken token)
    {
        if (_receipt is not { } previous || _intent is not { } intent || ReadId(previous.Transfer.TransferId) is not Guid id)
            return;
        _requiresObservation = true;
        var current = await Work.InspectTransferAsync(intent.Target, id, token);
        token.ThrowIfCancellationRequested();
        if (!ReceiptMatches(current, previous, id))
            throw new DashboardApiException(DashboardFailure.Incompatible);
        await ObserveAsync(current, token);
        _notice = CurrentNotice(current.Transfer);
    }

    private Task DownloadAsync() => !CanDownload ? Task.CompletedTask : RunAsync(async token =>
    {
        await ReadCurrentAsync(token);
        if (!CanDownload)
            return;
        var intent = _intent!;
        var transfer = _receipt!.Transfer;
        var id = ReadId(transfer.TransferId)!.Value;
        var bytes = new byte[checked((int)transfer.ByteLength)];
        try
        {
            _notice = "Reading the verified snapshot.";
            var offset = 0;
            _browserBytes = offset;
            _downloadLength = bytes.Length;
            do
            {
                var count = Math.Min(ChunkBytes, bytes.Length - offset);
                var chunk = await Work.ReadExportAsync(intent.Target, id, (ulong)offset, (uint)Math.Max(1, count), token);
                token.ThrowIfCancellationRequested();
                if (chunk.Offset != (ulong)offset || chunk.Data.Length != count || chunk.Eof != (offset + count == bytes.Length))
                    throw new DashboardApiException(DashboardFailure.Incompatible);
                chunk.Data.Span.CopyTo(bytes.AsSpan(offset, count));
                offset += count;
                _browserBytes = offset;
                StateHasChanged();
            } while (offset < bytes.Length);

            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), transfer.Sha256, StringComparison.Ordinal))
            {
                _requiresObservation = true;
                _notice = "The downloaded bytes did not match the snapshot digest. No file was saved.";
                return;
            }
            token.ThrowIfCancellationRequested();
            var name = transfer.RelativePath.Split('/')[^1];
            using var content = new MemoryStream(bytes, writable: false);
            using var reference = new DotNetStreamReference(content, leaveOpen: true);
            try
            {
                await using var module = await Browser.InvokeAsync<IJSObjectReference>("import", token, DashboardActivity.ModulePath);
                token.ThrowIfCancellationRequested();
                await module.InvokeVoidAsync("saveVerifiedFile", token, name, reference);
            }
            catch (JSException) when (!token.IsCancellationRequested)
            {
                _requiresObservation = true;
                _notice = "The browser could not save the file. Check the transfer before downloading again.";
                return;
            }
            token.ThrowIfCancellationRequested();
            _notice = "Verified file sent to your browser. Release the snapshot when you no longer need it.";
        }
        catch (DashboardApiException)
        {
            if (!token.IsCancellationRequested)
                _requiresObservation = true;
            throw;
        }
        finally
        {
            if (!token.IsCancellationRequested)
                _browserBytes = null;
            CryptographicOperations.ZeroMemory(bytes);
        }
    });

    private Task CloseAsync()
    {
        if (!CanClose || _receipt is not { } receipt || _intent is not { } intent ||
            ReadId(receipt.Transfer.TransferId) is not Guid id)
            return Task.CompletedTask;
        var command = NewCommand(intent.Target.OwnerEpoch!.Value, receipt.Revision);
        return RunAsync(async token =>
        {
            _focusTransferHeading = true;
            await ApplyMutationAsync(new Pending(TransferPhase.CloseTransfer, command), () =>
                Work.CloseTransferAsync(intent.Target, command, id, token), token);
            _notice = "The Node confirmed that the transfer was released.";
        });
    }

    private async Task ApplyMutationAsync(Pending operation, Func<Task<DashboardTransferReceipt>> send, CancellationToken token)
    {
        var previous = _receipt;
        _pending = operation;
        await PublishLeaseAsync();
        token.ThrowIfCancellationRequested();
        try
        {
            var receipt = await send();
            token.ThrowIfCancellationRequested();
            if (!ReceiptMatches(receipt, previous, ReadId(previous?.Transfer.TransferId)) ||
                !MatchesMutation(operation, receipt.Transfer, reconciled: false))
                throw new DashboardApiException(DashboardFailure.Uncertain);

            _pending = null;
            await ObserveAsync(receipt, token);
        }
        catch (DashboardApiException error) when (error.Failure != DashboardFailure.Uncertain)
        {
            _pending = null;
            _canDismiss = previous is null;
            if (previous is null && error.Code == "revision_mismatch")
                _blockedSandboxRevision = operation.Command.ExpectedRevision;
            throw;
        }
    }

    private Task ReconcileAsync() => _pending is not { } pending || _intent is not { } intent
        ? Task.CompletedTask : RunAsync(async token =>
        {
            var operation = await Work.GetOperationAsync(intent.Target, pending.Command, token);
            token.ThrowIfCancellationRequested();
            var priorId = ReadId(_receipt?.Transfer.TransferId);
            if (!OperationMatches(operation, pending, priorId))
                throw new DashboardApiException(DashboardFailure.Incompatible);

            if (operation.State == OperationState.Failed && operation.Effect == EffectCertainty.DidNotHappen)
            {
                _pending = null;
                _canDismiss = _receipt is null;
                _notice = operation.ResponseCompacted
                    ? "The Node kept the original command outcome. It confirmed no change."
                    : "The Node confirmed that the original command made no change.";
                if (_receipt is not null)
                {
                    await ReadCurrentAsync(token);
                    _notice = "The original command made no change. The transfer status has been refreshed.";
                }
                return;
            }
            if (operation.State != OperationState.Succeeded || operation.Effect != EffectCertainty.Happened ||
                ReadId(operation.AggregateId) is not Guid id)
            {
                _notice = "The original command still needs reconciliation. Check again before taking another action.";
                return;
            }

            var current = await Work.InspectTransferAsync(intent.Target, id, token);
            token.ThrowIfCancellationRequested();
            if (!ReceiptMatches(current, _receipt, id) || !MatchesMutation(pending, current.Transfer, reconciled: true))
                throw new DashboardApiException(DashboardFailure.Uncertain);
            _pending = null;
            await ObserveAsync(current, token);
            _notice = pending.Phase is TransferPhase.BeginImport or TransferPhase.WriteImport &&
                current.Transfer.State != TransferState.Complete && current.Transfer.ClosedAt is null
                ? "Original command verified. This upload is paused. Cancel the staged transfer before starting again."
                : CurrentNotice(current.Transfer);
            if (pending.Phase == TransferPhase.BeginExport &&
                current.Transfer.State is TransferState.Open or TransferState.Streaming)
                await PollExportAsync(token);
        });

    private async Task ObserveAsync(DashboardTransferReceipt receipt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _receipt = receipt;
        _requiresObservation = false;
        await PublishLeaseAsync();
        token.ThrowIfCancellationRequested();
        StateHasChanged();
    }

    private bool ReceiptMatches(DashboardTransferReceipt receipt, DashboardTransferReceipt? previous, Guid? id)
    {
        var row = receipt.Transfer;
        if (_intent is not { } intent || receipt.Revision <= 0 || ReadId(row.TransferId) is not Guid actual ||
            id is not null && actual != id || ReadId(row.SandboxId) != intent.Target.SandboxId ||
            row.Direction != intent.Direction || row.RelativePath != intent.Path || !row.HasByteLength || !row.HasVerifiedOffset ||
            row.VerifiedOffset > row.ByteLength || ToTime(row.ExpiresAt) is null ||
            row.ClosedAt is not null && ToTime(row.ClosedAt) is null ||
            row.State is not (TransferState.Open or TransferState.Streaming or TransferState.Complete or TransferState.Failed or TransferState.RecoveryRequired))
            return false;

        if (row.State == TransferState.Complete && (!ValidHash(row.Sha256) || row.VerifiedOffset != row.ByteLength) ||
            intent.Length is ulong length && (row.ByteLength != length || row.Sha256 != intent.Digest))
            return false;
        if (previous is null)
            return true;

        var before = previous.Transfer;
        return receipt.Revision >= previous.Revision && row.VerifiedOffset >= before.VerifiedOffset &&
            (before.State != TransferState.Complete || row.State == TransferState.Complete &&
                row.ByteLength == before.ByteLength && row.Sha256 == before.Sha256) &&
            (before.ClosedAt is null || Equals(row.ClosedAt, before.ClosedAt));
    }

    private static bool MatchesMutation(Pending pending, Transfer row, bool reconciled) => pending.Phase switch
    {
        TransferPhase.BeginImport => row.State == TransferState.Open && row.ClosedAt is null && row.VerifiedOffset == 0,
        TransferPhase.BeginExport => row.State is TransferState.Open or TransferState.Streaming or TransferState.Complete,
        TransferPhase.WriteImport => (reconciled ? row.VerifiedOffset >= pending.ExpectedOffset : row.VerifiedOffset == pending.ExpectedOffset) &&
            row.State is TransferState.Streaming or TransferState.Complete,
        TransferPhase.CommitImport => row.State == TransferState.Complete,
        TransferPhase.CloseTransfer => row.ClosedAt is not null,
        _ => false,
    };

    private static bool OperationMatches(Operation operation, Pending pending, Guid? transferId) =>
        ReadId(operation.CommandId) == pending.Command.CommandId && ReadId(operation.OwnerEpoch) == pending.Command.OwnerEpoch &&
        operation.Method == "DataService." + pending.Phase && (transferId is null || ReadId(operation.AggregateId) == transferId);

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (_busy || _scope is not { IsCancellationRequested: false } scope)
            return;
        var token = scope.Token;
        var access = Access;
        _busy = true;
        _error = null;
        _notice = null;
        try
        {
            await work(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (DashboardApiException error) when (!token.IsCancellationRequested)
        {
            _error = error;
            if (error.Failure == DashboardFailure.Unauthenticated)
            {
                await access.ExpireAsync();
            }
            else
            {
                if (_receipt is not null && error.Failure is DashboardFailure.Conflict or DashboardFailure.Forbidden or
                    DashboardFailure.NotFound or DashboardFailure.Unavailable or DashboardFailure.Incompatible)
                {
                    _requiresObservation = true;
                }
                _notice = _readOnlyOriginal ? PreviousTransferNotice
                    : _pending is not null
                    ? "The original command still needs reconciliation. Check its result before taking another action."
                    : _requiresObservation ? "Check the transfer for its current state before continuing."
                    : _blockedSandboxRevision is not null ? "Refresh the Sandbox to review its current revision before beginning a transfer."
                    : error.Message;
            }
        }
        catch (IOException) when (!token.IsCancellationRequested)
        {
            _notice = "The selected local file could not be read. Select it again.";
        }
        catch (JSException) when (!token.IsCancellationRequested)
        {
            _notice = "The browser could not read the selected local file. Select it again.";
        }
        finally
        {
            if (ReferenceEquals(_scope, scope))
            {
                _busy = false;
                await PublishLeaseAsync();
            }
        }
    }

    private async Task PublishLeaseAsync()
    {
        var trackedLease = _pending is not null || _receipt?.Transfer is { ClosedAt: null } row &&
            (row.State is TransferState.Open or TransferState.Streaming or TransferState.RecoveryRequired ||
                row is { Direction: "export", State: TransferState.Complete });
        if (_appliedAccess is { } access && _intent is { } intent && !access.Cancellation.IsCancellationRequested)
        {
            if (trackedLease)
                access.TransferJourneys[intent.Target] = new SandboxTransferJourney(intent, _pending, _receipt);
            else
                access.TransferJourneys.Remove(intent.Target);
        }
        var hasLease = trackedLease || _previousGrantLease;
        if (_reportedLease == hasLease)
            return;
        _reportedLease = hasLease;
        await LeaseChanged.InvokeAsync(hasLease);
    }

    private async Task DismissAsync()
    {
        if (_busy || _pending is not null || _receipt?.Transfer.ClosedAt is null && !_canDismiss)
            return;
        ClearTransfer();
        _focusTransferHeading = true;
        _previousGrantLease = Access.TransferJourneys.Keys.Any(old => old.NodeId == Target.NodeId &&
            old.EnrollmentId == Target.EnrollmentId && old.SandboxId == Target.SandboxId);
        if (_previousGrantLease)
            _notice = PreviousTransferNotice;
        await PublishLeaseAsync();
    }

    private void ClearForm()
    {
        _upload = null;
        _importPath = _importVersion = _exportPath = _exportVersion = "";
        _overwrite = false;
        _fileInputKey++;
    }

    private void ClearTransfer()
    {
        _intent = null;
        _receipt = null;
        _pending = null;
        _notice = null;
        _error = null;
        _canDismiss = false;
        _requiresObservation = false;
        _readOnlyOriginal = false;
        _previousGrantLease = false;
        _browserBytes = null;
        _downloadLength = 0;
    }

    private void CancelScope()
    {
        _scope?.Cancel();
        _scope?.Dispose();
        _scope = null;
    }

    public void Dispose()
    {
        CancelScope();
        ClearForm();
        ClearTransfer();
    }

    private static DashboardWorkCommand NewCommand(Guid owner, long revision) => new(Guid.NewGuid(), owner, revision);

    private static Guid? ReadId(Id? value)
    {
        if (value?.Value.Length != 16)
            return null;
        var id = new Guid(value.Value.Span, bigEndian: true);
        return id == Guid.Empty ? null : id;
    }

    private static DateTimeOffset? ToTime(Instant? instant)
    {
        if (instant is null || !instant.HasUnixSeconds || instant.Nanos > 999_999_999)
            return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(instant.UnixSeconds).AddTicks(instant.Nanos / 100);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool ValidPath(string value) => value.Length != 0 && Encoding.UTF8.GetByteCount(value) <= 4096 &&
        !value.Any(character => char.IsControl(character) || character is '\\' or ':') &&
        value.Split('/').All(part => part.Length != 0 && part is not ("." or ".."));

    private static bool ValidVersion(string? value) => value is { Length: > 0 } && Encoding.UTF8.GetByteCount(value) <= 4096 &&
        !value.Any(char.IsControl);

    private static bool ValidHash(string hash) => hash.Length == 64 &&
        hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string TransferStateLabel(Transfer row) => row.State switch
    {
        TransferState.Open when row.Direction == "export" => "Preparing",
        TransferState.Open => "Staging",
        TransferState.Streaming when row.Direction == "export" => "Preparing",
        TransferState.Streaming => "Uploading",
        TransferState.Complete when row.Direction == "export" => "Materialized and verified",
        TransferState.Complete => "Committed and verified",
        TransferState.Failed => "Failed or cancelled",
        TransferState.RecoveryRequired => "Recovery required",
        _ => "State unavailable",
    };

    private string SnapshotLabel(Transfer row) => row.ClosedAt is not null ? "Released by the Node" :
        ToTime(row.ExpiresAt) <= DateTimeOffset.UtcNow ? "Retention deadline passed. Release has not been confirmed." :
        ObservationStale ? "Check the snapshot for its current availability" :
        row.State == TransferState.Failed ? "Snapshot unavailable" :
        row.State == TransferState.Complete ? "Retained for download" : "Awaiting materialization";

    private static string CurrentNotice(Transfer row) => row.ClosedAt is not null ? "The Node confirmed release." : row.State switch
    {
        TransferState.Complete when row.Direction == "export" && ToTime(row.ExpiresAt) <= DateTimeOffset.UtcNow =>
            "The retention deadline passed. Release has not been confirmed.",
        TransferState.Complete when row.Direction == "export" && row.ByteLength > BrowserBytes =>
            "The verified snapshot exceeds the browser limit. Release it with the original grant. " +
            "The local owner can export the source separately using the CLI.",
        TransferState.Complete when row.Direction == "export" => "Snapshot verified. Download it or release it.",
        TransferState.Complete => "The imported file is verified and committed.",
        TransferState.Failed => "The Node reported that the transfer failed.",
        TransferState.RecoveryRequired => "The Node still needs to reconcile this transfer.",
        _ => row.Direction == "export" ? "Preparing the export snapshot." : "The staged upload is incomplete.",
    };

    internal enum TransferPhase { BeginImport, WriteImport, CommitImport, BeginExport, CloseTransfer }

    internal sealed record Intent(DashboardWorkTarget Target, string Direction, string Path, ulong? Length = null, string? Digest = null);

    internal sealed record Pending(TransferPhase Phase, DashboardWorkCommand Command, ulong ExpectedOffset = 0);
}

internal sealed record SandboxTransferJourney(SandboxTransfers.Intent Intent, SandboxTransfers.Pending? Pending, DashboardTransferReceipt? Receipt);
