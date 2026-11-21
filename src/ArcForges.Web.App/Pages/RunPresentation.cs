// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;

namespace ArcForges.Web.App.Pages;

internal static class RunPresentation
{
    internal static Guid? ReadId(Id? id)
    {
        if (id?.Value.Length != 16)
            return null;
        var value = new Guid(id.Value.Span, bigEndian: true);
        return value == Guid.Empty ? null : value;
    }

    internal static DateTimeOffset? ReadTime(Instant? value)
    {
        if (value is null || !value.HasUnixSeconds || value.Nanos > 999_999_999 ||
            value.UnixSeconds is < -62_135_596_800 or > 253_402_300_799)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(value.UnixSeconds).AddTicks(value.Nanos / 100);
    }

    internal static string Caption(RunState state) => state switch
    {
        RunState.Starting => "Starting",
        RunState.Running => "Running",
        RunState.Stopping => "Stopping",
        RunState.Draining => "Draining",
        RunState.Exited => "Exited",
        RunState.Failed => "Failed",
        RunState.RecoveryRequired => "Recovery required",
        _ => "State unavailable",
    };

    internal static string Caption(Run run) => run.HasSafeCommandDisplay && !string.IsNullOrWhiteSpace(run.SafeCommandDisplay)
        ? run.SafeCommandDisplay
        : "Run";

    internal static bool NeedsCheck(Operation? operation) =>
        operation is null || operation.State is not (OperationState.Succeeded or OperationState.Failed) ||
        operation.Effect is not (EffectCertainty.Happened or EffectCertainty.DidNotHappen);
}
