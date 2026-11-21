// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;

namespace ArcForges.Web.App.Pages;

internal sealed class PendingSecuritySessionRevocation(Guid commandId)
{
    internal Guid CommandId { get; } = commandId;
    internal bool Sending { get; set; } = true;
    internal long? ConfirmedRevision { get; set; }
    internal DashboardApiException? Failure { get; set; }
}

internal sealed class PendingSecurityGrantRevocation(DashboardWorkTarget target, DashboardWorkCommand command)
{
    internal DashboardWorkTarget Target { get; } = target;
    internal DashboardWorkCommand Command { get; } = command;
    internal bool Sending { get; set; } = true;
    internal long? ConfirmedRevision { get; set; }
    internal DashboardApiException? Failure { get; set; }
    internal bool CanVerify => !Sending && ConfirmedRevision is null;

    internal bool Matches(DashboardWorkTarget selection) =>
        Target.NodeId == selection.NodeId && Target.EnrollmentId == selection.EnrollmentId &&
        Target.GrantId == selection.GrantId;
}
