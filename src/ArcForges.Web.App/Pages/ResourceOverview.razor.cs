// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using Microsoft.AspNetCore.Components;

namespace ArcForges.Web.App.Pages;

public partial class ResourceOverview : IDisposable
{
    private const int SampleLimit = 12;
    private const uint WatchIntervalMs = 1000;

    [Parameter, EditorRequired]
    public DashboardWorkTarget Target { get; set; } = default!;

    [Parameter]
    public Guid? RunId { get; set; }

    [CascadingParameter]
    private DashboardAccess Access { get; set; } = default!;

    [Inject]
    private IDashboardWorkspaces Workspaces { get; set; } = default!;

    private readonly List<ResourceSnapshot> _samples = new(SampleLimit);
    private CancellationTokenSource _lifetime = default!;
    private CancellationTokenSource? _budgetRead;
    private CancellationTokenSource? _watch;
    private ResourceSnapshot? _snapshot;
    private PolicyDraft? _draft;
    private DashboardApiException? _readFailure;
    private DashboardApiException? _watchFailure;
    private DashboardApiException? _policyFailure;
    private string? _policyMessage;
    private Guid? _observedPendingCommand;
    private string _cpu = "";
    private string _memory = "";
    private bool _reading;
    private bool _watching;
    private bool _watchFinished;
    private bool _budgetNeedsRefresh;
    private bool _checking;
    private bool _runScopeVerified;
    private bool _canUseRevision;
    private bool _disposed;

    private DashboardResourceScope Scope => new(Target.NodeId, Target.EnrollmentId, Target.SandboxId, RunId);
    private Guid? ScopeId => RunId ?? Target.SandboxId;
    private string ScopeName => RunId is null ? "Sandbox" : "Run";
    private DashboardPendingResourcePolicy? Pending => Access.PendingResourcePolicies.GetValueOrDefault(Scope);
    private DashboardPendingResourcePolicy? VisiblePending => Pending is { } pending && pending.OriginalTarget.GrantId == Target.GrantId
        ? pending : null;
    private long? PolicyRevision => _snapshot?.PolicyRevision is { HasValue: true, Value: > 0 } revision ? revision.Value : null;
    private bool CanRead => !_disposed && !_reading && !_watching && !_checking &&
        (_draft is null || _budgetNeedsRefresh) && Pending is not { Sending: true };
    private bool CanWatch => CanRead && _draft is null && Pending is null && (RunId is null || _runScopeVerified);
    private bool CanEdit => CanWatch && Target.OwnerEpoch is not null && EditableLimits is not null;

    private string WatchStatus => _watching ? "Watching for new device samples. Values are shown at their reported times." :
        _watchFailure is not null || _readFailure is not null ? "Live samples unavailable. Displayed samples are historical." :
        _watchFinished ? "Sampling ended. Displayed samples are historical." :
        _samples.Count > 0 ? "Showing the last reported samples." : "Sampling is off.";

    private PolicyDraft? EditableLimits
    {
        get
        {
            // The Node's first explicit Run overlay advances revision 1 and every persisted overlay uses throttle.
            if (RunId is null || !_canUseRevision || PolicyRevision is not long revision || revision is <= 1 or >= long.MaxValue ||
                _snapshot is not { HasStale: true, Stale: false, SampleSequence: > 0 } snapshot ||
                !TryRequestedLimit(snapshot, "cpu", "millicores", out ulong cpu) || cpu > uint.MaxValue ||
                !TryRequestedLimit(snapshot, "memory", "bytes", out ulong memory))
            {
                return null;
            }

            return new PolicyDraft(revision, (uint)cpu, memory);
        }
    }

    protected override Task OnInitializedAsync()
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(Access.Cancellation);
        Access.PendingResourcePoliciesChanged += PendingPolicyChanged;
        Access.ViewResumed += ViewResumed;
        _observedPendingCommand = Pending?.Command.CommandId;
        return ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (!CanRead)
            return;

        using var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _budgetRead = read;
        _reading = true;
        var ct = read.Token;
        try
        {
            if (RunId is not null && !_runScopeVerified)
            {
                // The Run RPC itself only carries the Run ID. Prove this grant covers the chosen Sandbox first.
                var sandbox = await Workspaces.GetResourceBudgetAsync(Target, null, ct);
                if (ct.IsCancellationRequested)
                    return;
                if (sandbox.ScopeKind != "sandbox" || RunPresentation.ReadId(sandbox.ScopeId) != Target.SandboxId)
                    throw new DashboardApiException(DashboardFailure.Incompatible);
                _runScopeVerified = true;
            }
            var result = await Workspaces.GetResourceBudgetAsync(Target, RunId, ct);
            if (ct.IsCancellationRequested)
                return;

            _canUseRevision = AcceptSnapshot(result);
            _budgetNeedsRefresh = !_canUseRevision;
            _readFailure = null;
            _watchFailure = null;
            _watchFinished = false;
        }
        catch (DashboardApiException failure)
        {
            if (!ct.IsCancellationRequested)
            {
                _readFailure = failure;
                await ReadRefusalAsync(failure);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_budgetRead, read))
            {
                _budgetRead = null;
                _reading = false;
            }
        }
    }

    private async Task ToggleWatchAsync()
    {
        if (_watching)
        {
            StopWatch();
            return;
        }
        if (!CanWatch)
            return;

        using var watch = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _watch = watch;
        _watching = true;
        _watchFailure = null;
        _watchFinished = false;
        try
        {
            await foreach (var frame in Workspaces.WatchResourceUsageAsync(Target, RunId, WatchIntervalMs, watch.Token))
            {
                if (watch.IsCancellationRequested)
                    break;

                AcceptSnapshot(frame.Snapshot ?? throw new DashboardApiException(DashboardFailure.Incompatible));
                _readFailure = null;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (DashboardApiException failure)
        {
            if (!watch.IsCancellationRequested)
            {
                _watchFailure = failure;
                await ReadRefusalAsync(failure);
            }
        }
        catch (OperationCanceledException) when (watch.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_watch, watch))
                StopWatch();
        }
    }

    private void StopWatch()
    {
        var watch = _watch;
        _watch = null;
        _watching = false;
        _watchFinished = true;
        _canUseRevision = false;
        watch?.Cancel();
    }

    private void ViewResumed() => _ = InvokeAsync(() =>
    {
        if (_disposed)
            return;

        var read = _budgetRead;
        _budgetRead = null;
        _reading = false;
        read?.Cancel();
        if (_watching)
            StopWatch();
        _runScopeVerified = false;
        _canUseRevision = false;
        _budgetNeedsRefresh = _snapshot is not null;
        _watchFinished = _samples.Count > 0;
        StateHasChanged();
    });

    private bool AcceptSnapshot(ResourceSnapshot snapshot)
    {
        if (snapshot.ScopeKind != (RunId is null ? "sandbox" : "run") || RunPresentation.ReadId(snapshot.ScopeId) != ScopeId)
            throw new DashboardApiException(DashboardFailure.Incompatible);

        bool observedSample = snapshot.SampleSequence > 0 && !string.IsNullOrEmpty(snapshot.SampleEpoch);
        bool sameEpoch = observedSample && _samples.Count > 0 && _samples[0].SampleEpoch == snapshot.SampleEpoch;
        if (sameEpoch && (_samples[0].SampleSequence > snapshot.SampleSequence ||
            (_samples[0].SampleSequence == snapshot.SampleSequence && _samples[0].Stale && !snapshot.Stale)))
        {
            return false;
        }

        // A new policy revision can accompany the same native sample without advancing its observation time.
        bool canUseRevision = PolicyRevision is not long current || snapshot.PolicyRevision?.Value >= current;
        if (canUseRevision)
            _snapshot = snapshot;

        if (!observedSample)
            return canUseRevision;

        if (!sameEpoch)
            _samples.Clear();

        if (sameEpoch && _samples[0].SampleSequence == snapshot.SampleSequence)
        {
            _samples[0] = snapshot;
            return canUseRevision;
        }

        if (_samples.Count == SampleLimit)
            _samples.RemoveAt(SampleLimit - 1);
        _samples.Insert(0, snapshot);
        return canUseRevision;
    }

    private async Task ReadRefusalAsync(DashboardApiException failure)
    {
        _canUseRevision = false;
        _budgetNeedsRefresh = _snapshot is not null;
        if (failure.Failure is DashboardFailure.Forbidden or DashboardFailure.NotFound or DashboardFailure.Unauthenticated)
        {
            _runScopeVerified = false;
            _samples.Clear();
            _snapshot = null;
            _draft = null;
            _budgetNeedsRefresh = false;
        }
        if (failure.Failure == DashboardFailure.Unauthenticated)
            await Access.ExpireAsync();
    }

    private void BeginPolicyEdit()
    {
        if (!CanEdit || EditableLimits is not { } draft)
            return;

        _draft = draft;
        _cpu = draft.Cpu.ToString(CultureInfo.InvariantCulture);
        _memory = draft.Memory.ToString(CultureInfo.InvariantCulture);
        _policyFailure = null;
        _policyMessage = null;
    }

    private void CancelPolicyEdit()
    {
        _draft = null;
        _policyMessage = null;
    }

    private async Task SavePolicyAsync()
    {
        if (_disposed || _reading || _budgetNeedsRefresh || Pending is not null || _draft is not { } draft ||
            Target.OwnerEpoch is not Guid epoch || RunId is not Guid run)
            return;

        if (EditableLimits != draft)
        {
            _draft = null;
            _policyMessage = "Refresh the resource budget before making a change.";
            return;
        }
        if (!uint.TryParse(_cpu, NumberStyles.None, CultureInfo.InvariantCulture, out uint cpu) ||
            !ulong.TryParse(_memory, NumberStyles.None, CultureInfo.InvariantCulture, out ulong memory) ||
            cpu == 0 || memory == 0 || cpu > draft.Cpu || memory > draft.Memory ||
            (cpu == draft.Cpu && memory == draft.Memory))
        {
            _policyMessage = "Enter positive whole numbers and lower at least one limit. Neither limit can exceed the requested value shown above.";
            return;
        }

        var access = Access;
        var scope = Scope;
        var pending = new DashboardPendingResourcePolicy(Target, new DashboardWorkCommand(Guid.NewGuid(), epoch, draft.Revision), Sending: true);
        if (!access.PendingResourcePolicies.TryAdd(scope, pending))
            return;

        _draft = null;
        _canUseRevision = false;
        _policyMessage = null;
        _policyFailure = null;
        access.NotifyPendingResourcePoliciesChanged();
        try
        {
            // Once submitted, navigation must not cancel observation of a possible write.
            var result = await Workspaces.SetResourcePolicyAsync(pending.OriginalTarget, pending.Command, run,
                new ResourcePolicy { CpuMaxMillicores = cpu, MemoryMaxBytes = memory, PressureAction = PressureAction.Throttle }, access.Cancellation);
            if (access.Cancellation.IsCancellationRequested)
                return;

            if (result.Operation is { State: OperationState.Succeeded } &&
                result.Snapshot.PolicyRevision?.Value != draft.Revision + 1)
                throw new DashboardApiException(DashboardFailure.Uncertain);
            if (!SettlePolicy(scope, pending, result.Operation))
                return;
            if (!_disposed)
            {
                if (result.Operation.State == OperationState.Succeeded)
                    AcceptSnapshot(result.Snapshot);
                ShowReceipt(result.Operation);
            }
        }
        catch (DashboardApiException failure)
        {
            if (access.PendingResourcePolicies.TryGetValue(scope, out var retained) && retained.Command == pending.Command)
            {
                if (failure.Failure == DashboardFailure.Uncertain)
                    access.PendingResourcePolicies[scope] = retained with { CorrelationId = failure.CorrelationId };
                else
                    access.PendingResourcePolicies.Remove(scope);
            }
            if (!_disposed && !access.Cancellation.IsCancellationRequested)
                _policyFailure = failure;

            if (failure.Failure == DashboardFailure.Unauthenticated)
                await access.ExpireAsync();
        }
        catch (OperationCanceledException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (access.PendingResourcePolicies.TryGetValue(scope, out var retained) && retained.Command == pending.Command)
                access.PendingResourcePolicies[scope] = retained with { Sending = false };

            access.NotifyPendingResourcePoliciesChanged();
        }
    }

    private async Task CheckPolicyAsync()
    {
        if (_disposed || _checking || _reading || VisiblePending is not { Sending: false } pending)
            return;

        var scope = Scope;
        var ct = _lifetime.Token;
        _checking = true;
        _policyFailure = null;
        _policyMessage = null;
        bool resolved = false;
        try
        {
            var operation = await Workspaces.GetOperationAsync(pending.OriginalTarget, pending.Command, ct);
            if (ct.IsCancellationRequested)
                return;

            resolved = SettlePolicy(scope, pending, operation);
            ShowReceipt(operation);
        }
        catch (DashboardApiException failure)
        {
            if (!ct.IsCancellationRequested)
            {
                _policyFailure = failure;
                if (failure.Failure == DashboardFailure.Unauthenticated)
                    await Access.ExpireAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            _checking = false;
            Access.NotifyPendingResourcePoliciesChanged();
        }
        if (resolved)
            await ReadAsync();
    }

    private bool SettlePolicy(DashboardResourceScope scope, DashboardPendingResourcePolicy pending, Operation? operation)
    {
        if (operation is null || operation.Method != "ResourceService.SetPolicy" || RunPresentation.ReadId(operation.AggregateId) != scope.RunId ||
            RunPresentation.ReadId(operation.CommandId) != pending.Command.CommandId ||
            RunPresentation.ReadId(operation.OwnerEpoch) != pending.Command.OwnerEpoch)
            throw new DashboardApiException(DashboardFailure.Uncertain);

        if ((operation.State, operation.Effect) is not ((OperationState.Succeeded, EffectCertainty.Happened) or
            (OperationState.Failed, EffectCertainty.DidNotHappen) or (OperationState.Failed, EffectCertainty.Happened)))
            return false;
        if (operation.State == OperationState.Succeeded && operation.Revision?.Value != pending.Command.ExpectedRevision + 1 &&
            !(operation.ResponseCompacted && operation.Revision is null))
            throw new DashboardApiException(DashboardFailure.Uncertain);

        Access.PendingResourcePolicies.Remove(scope);
        return true;
    }

    private void ShowReceipt(Operation operation)
    {
        _policyMessage = (operation.State, operation.Effect) switch
        {
            (OperationState.Succeeded, EffectCertainty.Happened) when operation.ResponseCompacted =>
                "The device confirmed the command took effect. The original result was compacted. Check the current read-back values.",
            (OperationState.Succeeded, EffectCertainty.Happened) =>
                $"The device recorded command {RunPresentation.ReadId(operation.CommandId):D} at policy revision {operation.Revision?.Value}. Check the applied and read-back values for enforcement.",
            (OperationState.Failed, EffectCertainty.DidNotHappen) => "The device confirmed the change did not take effect.",
            (OperationState.Failed, EffectCertainty.Happened) => "The device recorded a failed command with an effect. Review the read-back values.",
            _ => "The device has not confirmed the effect. Check the original receipt again.",
        };
    }

    private void PendingPolicyChanged() => _ = InvokeAsync(() =>
    {
        if (_disposed)
            return;
        var current = Pending?.Command.CommandId;
        if (current != _observedPendingCommand)
        {
            _draft = null;
            _canUseRevision = false;
            _observedPendingCommand = current;
        }
        StateHasChanged();
    });

    private static bool TryRequestedLimit(ResourceSnapshot snapshot, string dimension, string unit, out ulong limitValue)
    {
        limitValue = 0;
        var limit = snapshot.Limits.FirstOrDefault(item => item.Dimension == dimension && item.Unit == unit);
        return limit is { Status: LimitStatus.Effective, HasDesiredValue: true, HasAppliedValue: true, HasReadbackValue: true } &&
            limit.DesiredValue == limit.AppliedValue && limit.AppliedValue == limit.ReadbackValue &&
            ulong.TryParse(limit.DesiredValue, NumberStyles.None, CultureInfo.InvariantCulture, out limitValue) &&
            limitValue > 0;
    }

    private static string Reported(bool hasValue, string value) => hasValue && value.Length > 0 ? value : "Unavailable";

    private static string Count(bool hasValue, ulong value, string unit) => !hasValue ? "Unavailable" :
        unit.Length == 0 ? value.ToString("N0", CultureInfo.CurrentCulture) : $"{value:N0} {unit}";

    private static string Percent(bool hasValue, double value) => hasValue && value is >= 0 and <= 100 ? $"{value:G}%" : "Unavailable";

    private static string Freshness(ResourceSnapshot sample) => !sample.HasStale ? "Unavailable" : sample.Stale ? "Stale" : "Fresh at observation";

    private static string MemoryMetric(ResourceSnapshot sample) => sample.MemoryKind switch
    {
        "windows_commit" => "Windows commit",
        "linux_cgroup_current" => "Linux cgroup current",
        "resident_estimate" => "Resident estimate",
        _ => "Metric unavailable",
    };

    private static string Pressure(ResourceSnapshot sample) => sample.Pressure switch
    {
        "normal" => "Normal",
        "tight" => "Tight",
        "pressure" => "Pressure",
        "critical" => "Critical",
        _ => "Unknown",
    };

    private static string LimitCaption(LimitState limit) => limit.Status switch
    {
        LimitStatus.Pending => "Pending",
        LimitStatus.Effective => "Effective",
        LimitStatus.Degraded => "Degraded",
        LimitStatus.Failed => "Failed",
        LimitStatus.Unsupported => "Unsupported",
        _ => "Unavailable",
    };

    public void Dispose()
    {
        _disposed = true;
        Access.PendingResourcePoliciesChanged -= PendingPolicyChanged;
        Access.ViewResumed -= ViewResumed;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed record PolicyDraft(long Revision, uint Cpu, ulong Memory);
}
