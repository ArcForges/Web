// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.App.Dashboard;

namespace ArcForges.Web.App.Pages;

internal sealed class SandboxMutationReceipt(DashboardWorkCommand command)
{
    internal DashboardWorkCommand Command { get; } = command;
    internal SandboxMutationState State { get; set; } = SandboxMutationState.Sending;
    internal bool Checking { get; set; }
    internal Guid? SandboxId { get; set; }
    internal long? Revision { get; set; }
    internal Guid? CorrelationId { get; set; }
    internal bool NeedsReconciliation => State is SandboxMutationState.Accepted or SandboxMutationState.Uncertain;
}

internal enum SandboxMutationState
{
    Sending,
    Accepted,
    Uncertain,
    Succeeded,
    Refused,
}
