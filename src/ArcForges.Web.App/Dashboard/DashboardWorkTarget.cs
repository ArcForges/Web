// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.App.Dashboard;

public sealed record DashboardWorkTarget(Guid NodeId, Guid EnrollmentId, Guid GrantId, long GrantRevision,
    Guid? SandboxId, Guid? OwnerEpoch);

internal sealed record DashboardWorkSelection(DashboardDevice Device, DashboardWorkTarget Target)
{
    internal bool Matches(DashboardDevice current) => Device.Id == current.Id &&
        Device.EnrollmentId == current.EnrollmentId && Device.Revision == current.Revision &&
        Device.NodeEpoch == current.NodeEpoch && current.Presence == DashboardDevicePresence.Online;
}
