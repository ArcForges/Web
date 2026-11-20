// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;

namespace ArcForges.Web.App.Layout;

internal sealed class DashboardAccess(DashboardSession session, CancellationToken cancellation, Func<DashboardAccess, Task> expire)
{
    internal DashboardSession Session { get; set; } = session;
    internal CancellationToken Cancellation { get; } = cancellation;
    internal Dictionary<Guid, PendingDeviceRevocation> Revocations { get; } = new();

    internal event Action<DashboardDevice?>? RevocationsChanged;

    internal Task ExpireAsync() => expire(this);
    internal void NotifyRevocationsChanged(DashboardDevice? confirmed = null) => RevocationsChanged?.Invoke(confirmed);
}

internal sealed record PendingDeviceRevocation(Guid DeviceId, Guid EnrollmentId, long Revision, Guid CommandId)
{
    internal bool Sending { get; set; } = true;
    internal Guid? CorrelationId { get; set; }
}
