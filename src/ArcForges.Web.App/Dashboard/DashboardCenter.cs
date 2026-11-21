// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowser;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Probe;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using DeviceApi = ArcForges.Contracts.Management.V2;
using DeviceClient = ArcForges.Contracts.Management.V2.DeviceService.DeviceServiceClient;
using ManagementDevice = ArcForges.Contracts.Management.V2.Device;

namespace ArcForges.Web.App.Dashboard;

internal sealed partial class DashboardCenter(
    DashboardCenterConfiguration configuration,
    SessionProbe sessions,
    DashboardGrpcTransport transport,
    DashboardPasskeys passkeys,
    TimeProvider time) : IDashboardCenter, IDashboardWorkspaces, IDashboardAccount, IDashboardSecurity, IDashboardSubscription
{
    private const string DeviceRevoke = "management.deviceRevoke";
    private static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(10);
    private readonly object _sessionGate = new();
    private SessionState? _session;
    private long _sessionRead;

    public DashboardCenterConfiguration Configuration { get; } = configuration;

    public async Task<DashboardSession> ReadSessionAsync(CancellationToken cancellationToken)
    {
        RequireOrigin();
        long attempt;
        lock (_sessionGate)
            attempt = ++_sessionRead;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallDeadline);
        SessionState? next;
        try
        {
            var (snapshot, account, session) = await sessions.ReadWebBoundSessionAsync(deadline.Token);
            next = snapshot is AuthenticatedSession authenticated ? Bind(authenticated, account, session) : null;
        }
        catch (ProbeFailureException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exception.Kind != FailureKind.Unauthenticated)
            {
                DiscardBootstrap(attempt);
                throw MapProbeFailure(exception.Kind);
            }
            next = null;
        }
        catch (DashboardApiException)
        {
            DiscardBootstrap(attempt);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_sessionGate)
        {
            if (attempt != _sessionRead)
                throw new OperationCanceledException("The session read was superseded.");
            _session = next is not null && IsCurrent(next.View) ? next : null;
            return _session?.View ?? new DashboardSession(false, null, null, null);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var session = RequireSession();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallDeadline);
        try
        {
            await sessions.EndWebSessionAsync(session.Csrf, deadline.Token);
        }
        catch (ProbeFailureException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exception.Kind != FailureKind.Unauthenticated)
                throw MapProbeFailure(exception.Kind, mutation: true);
        }

        Invalidate(session);
    }

    public async Task<DashboardDevicePage> ListDevicesAsync(string? cursor, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var request = new DeviceApi.DeviceServiceListRequest { Meta = Meta(session), Limit = 30 };
        if (!string.IsNullOrEmpty(cursor))
            request.Cursor = cursor;
        using var channel = transport.Open(RequireOrigin(), session.Csrf, 4096, 131072);
        var client = new DeviceClient(channel);
        var reply = await CallAsync(client.ListAsync(request, Options(cancellationToken)), session, cancellationToken);
        var value = Accept(reply.Value, reply.Error, reply.Meta, session);
        var page = value.Page ?? throw InvalidResult();
        return new DashboardDevicePage(page.Devices.Select(DeviceView).ToArray(),
            string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor);
    }

    public async Task<DashboardDevice> InspectDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var request = new DeviceApi.DeviceServiceInspectRequest { Meta = Meta(session), DeviceId = ExactId(deviceId) };
        using var channel = transport.Open(RequireOrigin(), session.Csrf, 4096, 65536);
        var client = new DeviceClient(channel);
        var reply = await CallAsync(client.InspectAsync(request, Options(cancellationToken)), session, cancellationToken);
        var value = Accept(reply.Value, reply.Error, reply.Meta, session);
        var device = DeviceView(value.Device);
        if (device.Id != deviceId || reply.Meta?.ResultRev?.Value != device.Revision)
            throw InvalidResult(reply.Meta);
        return device;
    }

    public async Task<DashboardDevice> RevokeDeviceAsync(Guid deviceId, long expectedRevision, Guid commandId, CancellationToken cancellationToken)
    {
        if (expectedRevision <= 0)
            throw new DashboardApiException(DashboardFailure.Validation);

        var session = RequireSession();
        var meta = Meta(session);
        meta.ExpectedRev = new Revision { Value = expectedRevision };
        meta.CommandId = ExactId(commandId);
        var request = new DeviceApi.DeviceServiceRevokeRequest { Meta = meta, DeviceId = ExactId(deviceId) };
        using var channel = transport.Open(RequireOrigin(), session.Csrf, 4096, 65536);
        var client = new DeviceClient(channel);
        var reply = await SendRevokeAsync();
        if (reply.Error is { Code: "step_up_required", Effect: EffectCertainty.DidNotHappen })
        {
            await ConfirmOperationAsync(DeviceRevoke, deviceId, session, cancellationToken);
            EnsureCurrent(session, cancellationToken);
            reply = await SendRevokeAsync();
        }

        var value = Accept(reply.Value, reply.Error, reply.Meta, session, mutation: true);
        try
        {
            var device = DeviceView(value.Device);
            if (device.Id != deviceId || device.Presence != DashboardDevicePresence.Revoked ||
                device.Revision <= expectedRevision || reply.Meta?.ResultRev?.Value != device.Revision)
            {
                throw InvalidResult(reply.Meta);
            }
            return device;
        }
        catch (DashboardApiException)
        {
            throw new DashboardApiException(DashboardFailure.Uncertain, ReadId(reply.Meta?.CorrelationId));
        }

        Task<DeviceApi.DeviceServiceRevokeResponse> SendRevokeAsync() =>
            CallAsync(client.RevokeAsync(request, Options(cancellationToken)), session, cancellationToken, mutation: true);
    }

    public async Task<DashboardRunSummary> ReadRunSummaryAsync(DashboardDevice device, CancellationToken cancellationToken, DashboardRunTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        if (device.Presence == DashboardDevicePresence.Offline)
            return new DashboardRunSummary(DashboardRunState.Offline);
        if (device.Presence != DashboardDevicePresence.Online || target is null)
            return new DashboardRunSummary(DashboardRunState.Unknown);
        if (target.NodeId == Guid.Empty || target.GrantId == Guid.Empty || target.GrantRevision <= 0 ||
            target.EnrollmentId == Guid.Empty || target.EnrollmentId != device.EnrollmentId)
        {
            throw new DashboardApiException(DashboardFailure.Validation);
        }

        var session = RequireSession();
        var headers = WorkHeaders(target.NodeId, target.EnrollmentId, target.GrantId, target.GrantRevision);
        var request = new RunServiceListRequest { Meta = Meta(session), SandboxId = ExactId(target.SandboxId), Limit = 20 };
        using var channel = transport.Open(RequireOrigin(), session.Csrf, 4096, 1048576);
        var client = new RunService.RunServiceClient(channel);
        var reply = await CallAsync(client.ListAsync(request, Options(cancellationToken, headers)), session, cancellationToken);
        if (reply.Error?.Code == "node_offline")
            return new DashboardRunSummary(DashboardRunState.Offline);
        var value = Accept(reply.Value, reply.Error, reply.Meta, session);
        var page = value.Page ?? throw InvalidResult(reply.Meta);
        var count = value.Runs.Count;
        var state = !string.IsNullOrEmpty(page.NextCursor) ? DashboardRunState.Partial :
            count == 0 ? DashboardRunState.Empty : DashboardRunState.Ready;
        return new DashboardRunSummary(state, count, time.GetUtcNow(), target.SandboxId);
    }

    private async Task ConfirmOperationAsync(string operationClass, Guid target, SessionState session, CancellationToken cancellationToken)
    {
        var origin = RequireOrigin();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{operationClass}\n{session.View.AccountId:D}\n{target:D}"));
        var request = new WebBrowserBeginStepUpRequest
        {
            OperationClass = operationClass,
            Method = "passkey",
            TargetHash = Convert.ToHexStringLower(hash),
        };
        try
        {
            var challenge = await sessions.BeginStepUpAsync(request, session.Csrf, cancellationToken);
            EnsureCurrent(session, cancellationToken);
            if (!Guid.TryParseExact(challenge.FlowId, "D", out var flow) || flow == Guid.Empty || flow.ToString("D") != challenge.FlowId ||
                !TryUtc(challenge.ExpiresAt, out var expires) || expires <= time.GetUtcNow())
            {
                throw InvalidResult();
            }

            var assertion = await passkeys.ProveAsync(challenge, origin, cancellationToken);
            EnsureCurrent(session, cancellationToken);
            if (expires <= time.GetUtcNow())
                throw new DashboardApiException(DashboardFailure.ProofCancelled);
            var proof = new WebBrowserCompleteStepUpRequest
            {
                FlowId = challenge.FlowId,
                Proof = new WebBrowserAuthProofWebBrowserAuthProofPasskey(new WebBrowserAuthProofPasskey { Passkey = assertion }),
            };
            var evidence = await sessions.CompleteStepUpAsync(proof, session.Csrf, cancellationToken);
            EnsureCurrent(session, cancellationToken);
            if (evidence.OperationClass != operationClass || !TryUtc(evidence.ExpiresAt, out var evidenceExpiry) ||
                evidenceExpiry <= time.GetUtcNow())
            {
                throw InvalidResult();
            }
        }
        catch (ProbeFailureException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exception.Kind == FailureKind.Unauthenticated)
                Invalidate(session);
            throw MapProbeFailure(exception.Kind, correlationId: exception.CorrelationId);
        }
    }

    private CallOptions Options(CancellationToken cancellationToken, Metadata? headers = null, TimeSpan? deadline = null) =>
        new(headers, time.GetUtcNow().Add(deadline ?? CallDeadline).UtcDateTime, cancellationToken);

    private GrpcChannel OpenWorkChannel(SessionState session, int requestBytes, int responseBytes) =>
        transport.Open(RequireOrigin(), session.Csrf, requestBytes, responseBytes);

    private async Task<T> CallAsync<T>(AsyncUnaryCall<T> call, SessionState session, CancellationToken cancellationToken, bool mutation = false)
    {
        using (call)
        {
            try
            {
                var response = await call.ResponseAsync;
                EnsureCurrent(session, cancellationToken);
                return response;
            }
            catch (RpcException error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw RpcFailure(error, session, mutation);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DashboardApiException(mutation ? DashboardFailure.Uncertain : DashboardFailure.Unavailable);
            }
        }
    }

    private DashboardApiException RpcFailure(RpcException error, SessionState session, bool mutation = false)
    {
        var failure = error.StatusCode switch
        {
            StatusCode.Unauthenticated => DashboardFailure.Unauthenticated,
            StatusCode.PermissionDenied => DashboardFailure.Forbidden,
            StatusCode.NotFound => DashboardFailure.NotFound,
            StatusCode.Unimplemented => DashboardFailure.Incompatible,
            _ => mutation ? DashboardFailure.Uncertain : DashboardFailure.Unavailable,
        };
        if (failure == DashboardFailure.Unauthenticated)
            Invalidate(session);
        return new DashboardApiException(failure);
    }

    private T Accept<T>(T? value, ArcError? error, ResponseMeta? meta, SessionState session, bool mutation = false) where T : class
    {
        var correlation = ReadId(meta?.CorrelationId);
        if (error is null)
            return value ?? throw new DashboardApiException(mutation ? DashboardFailure.Uncertain : DashboardFailure.Incompatible, correlation);
        var failure = mutation && error.Effect != EffectCertainty.DidNotHappen ? DashboardFailure.Uncertain :
            error.Code switch
            {
                "step_up_required" => DashboardFailure.StepUpRequired,
                "not_found" => DashboardFailure.NotFound,
                "upgrade_required" => DashboardFailure.Incompatible,
                "node_offline" or "node_unreachable" or "overloaded" => DashboardFailure.Unavailable,
                _ => error.Category switch
                {
                    ErrorCategory.Authentication => DashboardFailure.Unauthenticated,
                    ErrorCategory.Authorization or ErrorCategory.Entitlement => DashboardFailure.Forbidden,
                    ErrorCategory.Conflict => DashboardFailure.Conflict,
                    ErrorCategory.Validation => DashboardFailure.Validation,
                    _ => DashboardFailure.Unavailable,
                },
            };
        if (failure == DashboardFailure.Unauthenticated)
            Invalidate(session);
        throw new DashboardApiException(failure, correlation ?? ReadId(error.CorrelationId),
            WireToken(error.Code), WireToken(error.Details?.State?.Reason));
    }

    private static string? WireToken(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '-' or '.')
            ? value
            : null;

    private static DashboardDevice DeviceView(ManagementDevice? raw)
    {
        if (raw is null || ReadId(raw.DeviceId) is not Guid id || ReadId(raw.EnrollmentId) is not Guid enrollment ||
            raw.Revision is not { Value: > 0 })
        {
            throw InvalidResult();
        }

        var presence = raw.Presence switch
        {
            "online" when !string.IsNullOrEmpty(raw.NodeEpoch) => DashboardDevicePresence.Online,
            "offline" => DashboardDevicePresence.Offline,
            "stale" => DashboardDevicePresence.Stale,
            "revoked" => DashboardDevicePresence.Revoked,
            _ => DashboardDevicePresence.Unknown,
        };
        return new DashboardDevice(id, enrollment, raw.DisplayName, raw.Revision.Value, presence, raw.Platform,
            InstantValue(raw.LastSeenAt), raw.Capabilities.ToArray(), string.IsNullOrEmpty(raw.NodeEpoch) ? null : raw.NodeEpoch);
    }

    private static SessionState Bind(AuthenticatedSession value, string? accountId, string? sessionId)
    {
        if (!Guid.TryParseExact(accountId, "D", out var account) || account == Guid.Empty ||
            !Guid.TryParseExact(sessionId, "D", out var session) || session == Guid.Empty ||
            !Exact.TryUnsigned(value.RecoveryGeneration, out var generation) ||
            !TryUtc(value.ExpiresAt, out var expires))
        {
            throw InvalidResult();
        }
        DateTimeOffset? idle = null;
        if (value.IdleExpiresAt is not null)
        {
            if (!TryUtc(value.IdleExpiresAt, out var parsed))
                throw InvalidResult();
            idle = parsed;
        }
        return new SessionState(value.CsrfToken, generation.Value, new DashboardSession(true, value.DisplayName, expires, idle, account, session, generation.Value));
    }

    private static bool TryUtc(string input, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant) &&
        input.EndsWith('Z') && instant.Offset == TimeSpan.Zero;

    private bool IsCurrent(DashboardSession session)
    {
        var now = time.GetUtcNow();
        return session.ExpiresAt > now && (session.IdleExpiresAt is null || session.IdleExpiresAt > now);
    }

    private SessionState RequireSession()
    {
        RequireOrigin();
        lock (_sessionGate)
        {
            if (_session is null || !IsCurrent(_session.View))
                throw new DashboardApiException(DashboardFailure.Unauthenticated);
            return _session;
        }
    }

    private void EnsureCurrent(SessionState expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameSession(RequireSession(), expected))
            throw new OperationCanceledException("The dashboard session changed.");
    }

    private void Invalidate(SessionState expected)
    {
        lock (_sessionGate)
        {
            if (_session is not null && SameSession(_session, expected))
            {
                _session = null;
                ++_sessionRead;
            }
        }
    }

    private void DiscardBootstrap(long attempt)
    {
        lock (_sessionGate)
        {
            if (attempt == _sessionRead)
                _session = null;
        }
    }

    private static bool SameSession(SessionState first, SessionState second) =>
        first.View.AccountId == second.View.AccountId && first.View.SessionId == second.View.SessionId &&
        first.RecoveryGeneration == second.RecoveryGeneration && first.Csrf == second.Csrf;

    private Uri RequireOrigin() => Configuration.Origin ?? throw new DashboardApiException(DashboardFailure.NotConfigured);

    private static RequestMeta Meta(SessionState session) => new()
    {
        CorrelationId = ExactId(Guid.NewGuid()),
        RecoveryGeneration = session.RecoveryGeneration,
    };

    private static Id ExactId(Guid id) => id == Guid.Empty ? throw new DashboardApiException(DashboardFailure.Validation) :
        new Id { Value = ByteString.CopyFrom(id.ToByteArray(bigEndian: true)) };

    private static Guid? ReadId(Id? id)
    {
        if (id?.Value.Length != 16)
            return null;
        var value = new Guid(id.Value.Span, bigEndian: true);
        return value == Guid.Empty ? null : value;
    }

    private static DateTimeOffset? InstantValue(Instant? instant)
    {
        if (instant is null || !instant.HasUnixSeconds || instant.Nanos > 999_999_999)
            return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(instant.UnixSeconds).AddTicks(instant.Nanos / 100);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidResult();
        }
    }

    private static DashboardApiException InvalidResult(ResponseMeta? meta = null) =>
        new(DashboardFailure.Incompatible, ReadId(meta?.CorrelationId));

    private static DashboardApiException MapProbeFailure(FailureKind kind, bool mutation = false, Guid? correlationId = null) => new(kind switch
    {
        FailureKind.Unauthenticated => DashboardFailure.Unauthenticated,
        FailureKind.Forbidden => DashboardFailure.Forbidden,
        FailureKind.Rejected => DashboardFailure.Validation,
        _ when mutation => DashboardFailure.Uncertain,
        FailureKind.Malformed => DashboardFailure.Incompatible,
        _ => DashboardFailure.Unavailable,
    }, correlationId);

    private sealed record SessionState(string Csrf, ulong RecoveryGeneration, DashboardSession View);
}
