// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;

namespace ArcForges.Web.App.Pages;

internal sealed class DashboardPendingRunCommand(DashboardWorkCommand command)
{
    internal DashboardWorkCommand Command { get; } = command;
    internal Operation? Operation { get; set; }
    internal Guid? ResultRunId { get; set; }
    internal bool Sending { get; set; }
    internal DashboardApiException? Failure { get; set; }

    internal bool IsUnresolved => (Failure is null or { Failure: DashboardFailure.Uncertain }) &&
        RunPresentation.NeedsCheck(Operation);

    internal static (DashboardWorkTarget Target, DashboardPendingRunCommand Pending)? Find(
        DashboardAccess access, DashboardWorkTarget target, Guid? runId)
    {
        if (access.RunCommands.TryGetValue((target, runId), out var pending))
            return (target, pending);

        foreach (var (key, command) in access.RunCommands)
        {
            if (key.RunId == runId && SameSandbox(key.Target, target))
                return (key.Target, command);
        }
        return null;
    }

    internal void Settle(DashboardAccess access, DashboardWorkTarget target, Guid? runId)
    {
        var key = (target, runId);
        if (access.RunCommands.TryGetValue(key, out var registered) && ReferenceEquals(registered, this))
        {
            access.RunCommands.Remove(key);
            access.NotifyRunCommandsChanged();
        }
    }

    internal static bool SameSandbox(DashboardWorkTarget left, DashboardWorkTarget right) =>
        left.NodeId == right.NodeId && left.EnrollmentId == right.EnrollmentId && left.SandboxId == right.SandboxId;
}
