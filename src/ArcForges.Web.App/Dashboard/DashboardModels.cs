// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ArcForges.Web.App.Dashboard;

public interface IDashboardCenter
{
    DashboardCenterConfiguration Configuration { get; }
    Task<DashboardSession> ReadSessionAsync(CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
    Task<DashboardDevicePage> ListDevicesAsync(string? cursor, CancellationToken cancellationToken);
    Task<DashboardDevice> InspectDeviceAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<DashboardDevice> RevokeDeviceAsync(Guid deviceId, long expectedRevision, Guid commandId, CancellationToken cancellationToken);
    Task<DashboardRunSummary> ReadRunSummaryAsync(DashboardDevice device, CancellationToken cancellationToken, DashboardRunTarget? target = null);
}

public sealed record DashboardSession(
    bool Authenticated,
    string? DisplayName,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? IdleExpiresAt,
    Guid? AccountId = null,
    Guid? SessionId = null,
    ulong? RecoveryGeneration = null);

public enum DashboardDevicePresence { Unknown, Online, Stale, Offline, Revoked }

public sealed record DashboardDevice(
    Guid Id,
    Guid EnrollmentId,
    string Name,
    long Revision,
    DashboardDevicePresence Presence,
    string Platform,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<string> Capabilities,
    string? NodeEpoch);

public sealed record DashboardDevicePage(IReadOnlyList<DashboardDevice> Devices, string? NextCursor);

public enum DashboardRunState { Unknown, Offline, Empty, Ready, Partial }

public sealed record DashboardRunSummary(
    DashboardRunState State,
    int? VisibleRunCount = null,
    DateTimeOffset? ObservedAt = null,
    Guid? SandboxId = null);

/// <summary>Exact local grant and sandbox selection required for a routed Run read.</summary>
public sealed record DashboardRunTarget(Guid NodeId, Guid EnrollmentId, Guid GrantId, long GrantRevision, Guid SandboxId)
{
    private const string CliAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static bool TryCreate(string? nodeId, string? grantId, string? grantRevision, string? sandboxId,
        Guid enrollmentId, [NotNullWhen(true)] out DashboardRunTarget? target)
    {
        target = null;
        if (enrollmentId == Guid.Empty || !TryIdentifier(nodeId, out var node) || !TryIdentifier(grantId, out var grant) ||
            !TryIdentifier(sandboxId, out var sandbox) || !TryRevision(grantRevision, out var revision))
        {
            return false;
        }

        target = new DashboardRunTarget(node, enrollmentId, grant, revision, sandbox);
        return true;
    }

    internal static bool TryRevision(string? text, out long revision) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out revision) &&
        revision > 0 && string.Equals(revision.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);

    internal static bool TryIdentifier(string? text, out Guid id)
    {
        id = Guid.Empty;
        if (text is { Length: 36 } && Guid.TryParseExact(text, "D", out id))
            return id != Guid.Empty;
        if (text is not { Length: 26 })
            return false;

        UInt128 value = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsAsciiLetterOrDigit(text[index]))
                return false;
            var digit = char.ToLowerInvariant(text[index]) switch
            {
                'i' or 'l' => 1,
                'o' => 0,
                var character => CliAlphabet.IndexOf(character),
            };
            if (digit < 0 || (index == 0 && digit > 7))
                return false;
            value = (value << 5) | (uint)digit;
        }

        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)(value >> 64));
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)value);
        id = new Guid(bytes, bigEndian: true);
        return id != Guid.Empty;
    }
}

public enum DashboardFailure
{
    NotConfigured,
    Unavailable,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict,
    Validation,
    StepUpRequired,
    ProofCancelled,
    Incompatible,
    Uncertain,
}

public sealed class DashboardApiException(DashboardFailure failure, Guid? correlationId = null,
    string? code = null, string? reason = null)
    : Exception(MessageFor(failure))
{
    public DashboardFailure Failure { get; } = failure;
    public Guid? CorrelationId { get; } = correlationId;
    public string? Code { get; } = code;
    public string? Reason { get; } = reason;

    private static string MessageFor(DashboardFailure failure) => failure switch
    {
        DashboardFailure.NotConfigured => "This dashboard has no configured Center.",
        DashboardFailure.Unavailable => "The Center or device is unavailable. Try again later.",
        DashboardFailure.Unauthenticated => "Your session has ended. Sign in to continue.",
        DashboardFailure.Forbidden => "Your account or local device grant does not allow this action.",
        DashboardFailure.NotFound => "The selected device is no longer available to this account.",
        DashboardFailure.Conflict => "The device has changed. Refresh it before trying again.",
        DashboardFailure.Validation => "The Center refused this request.",
        DashboardFailure.StepUpRequired => "Confirm your identity with a passkey to continue.",
        DashboardFailure.ProofCancelled => "Passkey confirmation did not complete.",
        DashboardFailure.Incompatible => "The Center returned a result this dashboard cannot use.",
        DashboardFailure.Uncertain => "The Center did not confirm the result. Check the device before trying again.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
