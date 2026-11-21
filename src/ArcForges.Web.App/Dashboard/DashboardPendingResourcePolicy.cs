// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.App.Dashboard;

internal sealed record DashboardResourceScope(Guid NodeId, Guid EnrollmentId, Guid? SandboxId, Guid? RunId);

internal sealed record DashboardPendingResourcePolicy(DashboardWorkTarget OriginalTarget, DashboardWorkCommand Command,
    bool Sending, Guid? CorrelationId = null);
