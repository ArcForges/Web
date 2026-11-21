// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Pages;

namespace ArcForges.Web.App.Layout;

internal sealed class DashboardAccess(DashboardSession session, CancellationToken cancellation, Func<DashboardAccess, Task> expire)
{
    internal DashboardSession Session { get; set; } = session;
    internal CancellationToken Cancellation { get; } = cancellation;
    internal Dictionary<Guid, PendingDeviceRevocation> Revocations { get; } = new();
    internal DashboardWorkSelection? WorkSelection { get; set; }
    internal Dictionary<DashboardWorkTarget, SandboxMutationReceipt> SandboxMutations { get; } = new();
    internal Dictionary<(DashboardWorkTarget Target, Guid? RunId), DashboardPendingRunCommand> RunCommands { get; } = new();
    internal Dictionary<DashboardResourceScope, DashboardPendingResourcePolicy> PendingResourcePolicies { get; } = new();
    internal Dictionary<DashboardWorkTarget, SandboxTransferJourney> TransferJourneys { get; } = new();

    internal event Action<DashboardDevice?>? RevocationsChanged;
    internal event Action? RunCommandsChanged;
    internal event Action? PendingResourcePoliciesChanged;

    internal Task ExpireAsync() => expire(this);
    internal void NotifyRevocationsChanged(DashboardDevice? confirmed = null) => RevocationsChanged?.Invoke(confirmed);
    internal void NotifyRunCommandsChanged() => RunCommandsChanged?.Invoke();
    internal void NotifyPendingResourcePoliciesChanged() => PendingResourcePoliciesChanged?.Invoke();
}

internal sealed record PendingDeviceRevocation(Guid DeviceId, Guid EnrollmentId, long Revision, Guid CommandId)
{
    internal bool Sending { get; set; } = true;
    internal Guid? CorrelationId { get; set; }
}
