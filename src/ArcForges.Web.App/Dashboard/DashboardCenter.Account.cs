// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Contracts.Sandbox.V2;
using Google.Protobuf;
using Grpc.Core;
using NodeClient = ArcForges.Contracts.Sandbox.V2.NodeService.NodeServiceClient;

namespace ArcForges.Web.App.Dashboard;

internal sealed partial class DashboardCenter
{
    private const int AccountRequestBytes = 4_096 - FrameHeaderBytes;
    private const int AccountReplyBytes = 65_536 - FrameHeaderBytes;
    private const int BillingCursorBytes = 128;
    private const string RevokeSession = "identity.revokeSession";
    private const string CancelSubscription = "commerce.cancelSubscription";
    private const string BillingReturn = "/account/subscription";
    private static readonly TimeSpan AccountDeadline = TimeSpan.FromSeconds(15);

    public Task<AccountProfile> ReadProfileAsync(CancellationToken ct) =>
        AccountUnaryAsync(
            static meta => new IdentityServiceGetProfileRequest { Meta = meta },
            AccountRpc.GetProfile,
            static reply => (reply.Value?.Profile, reply.Error, reply.Meta),
            static profile => profile.Revision is { Value: > 0 }, ct);

    public Task<AccountProfile> UpdateProfileAsync(Guid commandId, long expectedRevision, ProfileUpdate patch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (expectedRevision < 1 || patch.ChangeCase != ProfileUpdate.ChangeOneofCase.None)
            throw WorkValidation();

        return AccountUnaryAsync(
            meta => new IdentityServiceUpdateProfileRequest
            {
                Meta = AccountCommandMeta(meta, commandId, expectedRevision),
                Patch = patch.Clone(),
            },
            AccountRpc.UpdateProfile,
            static reply => (reply.Value?.Profile, reply.Error, reply.Meta),
            profile => profile.Revision?.Value >= expectedRevision, ct, mutation: true);
    }

    public Task<DeletionStatus> ReadAccountDeletionAsync(CancellationToken ct) =>
        AccountUnaryAsync(
            static meta => new IdentityServiceGetAccountDeletionRequest { Meta = meta },
            AccountRpc.GetAccountDeletion,
            static reply => (reply.Value?.Deletion, reply.Error, reply.Meta),
            static deletion => ReadId(deletion.DeletionId) is not null && !string.IsNullOrEmpty(deletion.State), ct);

    public Task<IdentityServiceListSessionsValue> ListSessionsAsync(string? cursor, CancellationToken ct) =>
        AccountUnaryAsync(
            meta => new IdentityServiceListSessionsRequest { Meta = meta, Page = AccountPage(cursor) },
            AccountRpc.ListSessions,
            static reply => (reply.Value, reply.Error, reply.Meta),
            static page => ValidAccountPage(page.Page) && page.Sessions.Count <= WorkPageSize &&
                page.Sessions.All(row => ReadId(row.SessionId) is not null && ReadId(row.DeviceId) is not null &&
                    row.Revision is { Value: > 0 }), ct);

    public Task<IdentityServiceListAuthIdentitiesValue> ListCredentialsAsync(string? cursor, CancellationToken ct) =>
        AccountUnaryAsync(
            meta => new IdentityServiceListAuthIdentitiesRequest { Meta = meta, Page = AccountPage(cursor) },
            AccountRpc.ListAuthIdentities,
            static reply => (reply.Value, reply.Error, reply.Meta),
            static page => ValidAccountPage(page.Page) && page.Items.Count <= WorkPageSize &&
                page.Items.All(row => ReadId(row.CredentialId) is not null && !string.IsNullOrEmpty(row.Kind)), ct);

    public Task<Receipt> RevokeSessionAsync(Guid sessionId, Guid commandId, CancellationToken ct) =>
        AccountUnaryAsync(
            meta => new IdentityServiceRevokeSessionRequest { Meta = AccountCommandMeta(meta, commandId), SessionId = ExactId(sessionId) },
            AccountRpc.RevokeSession,
            static reply => (reply.Value?.Receipt, reply.Error, reply.Meta),
            receipt => ValidAccountReceipt(receipt, commandId), ct,
            mutation: true, proofClass: RevokeSession, proofTarget: sessionId, revokeSession: sessionId);

    public Task<RemoteGrant> RevokeGrantAsync(DashboardWorkTarget target, DashboardWorkCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExpectedRevision != target.GrantRevision)
            throw WorkValidation();

        return WorkUnaryAsync(target, ct,
            (channel, session, options) => new NodeClient(channel).RevokeRemoteGrantAsync(new NodeServiceRevokeRemoteGrantRequest
            {
                Meta = WorkMutationMeta(session, target, command),
                OwnerEpoch = ExactId(command.OwnerEpoch),
                GrantId = ExactId(target.GrantId),
            }, options),
            static reply => (reply.Value?.Grant, reply.Error, reply.Meta),
            (grant, meta) => ReadId(grant.GrantId) == target.GrantId && ReadId(grant.EnrollmentId) == target.EnrollmentId &&
                ReadId(grant.SandboxId) == target.SandboxId && grant.State == GrantState.Revoked &&
                grant.Revision?.Value > target.GrantRevision && meta?.ResultRev?.Value == grant.Revision.Value,
            mutation: true);
    }

    public Task<EntitlementSnapshot> ReadEntitlementAsync(CancellationToken ct) =>
        AccountUnaryAsync(
            static meta => new EntitlementServiceGetSnapshotRequest { Meta = meta },
            AccountRpc.GetSnapshot,
            static reply => (reply.Value?.Snapshot, reply.Error, reply.Meta),
            snapshot => ValidEntitlement(snapshot, Configuration.Mode), ct);

    public Task<CommerceServiceGetCatalogueValue> ReadCatalogueAsync(string region, CancellationToken ct) =>
        AccountUnaryAsync(
            meta => new CommerceServiceGetCatalogueRequest { Meta = meta, Region = Region(region) },
            AccountRpc.GetCatalogue,
            static reply => (reply.Value, reply.Error, reply.Meta),
            catalogue => !string.IsNullOrEmpty(catalogue.CatalogueVersion) && catalogue.Items.All(item =>
                ReadId(item.OfferId) is not null && item.Kind == "subscription" && item.Region == region &&
                ValidPriceVersion(item.PriceVersion) && item.Amount is not null && !string.IsNullOrEmpty(item.Currency) &&
                !string.IsNullOrEmpty(item.Interval)), ct, hosted: true);

    public Task<PurchaseView> CreatePurchaseAsync(Guid commandId, Guid purchaseId, Guid offerId, string priceVersion,
        string region, CancellationToken ct)
    {
        if (!ValidPriceVersion(priceVersion))
            throw WorkValidation();
        return AccountUnaryAsync(
            meta => new CommerceServiceCreatePurchaseIntentRequest
            {
                Meta = AccountCommandMeta(meta, commandId),
                PurchaseIntentId = ExactId(purchaseId),
                OfferId = ExactId(offerId),
                PriceVersion = priceVersion,
                Region = Region(region),
            },
            AccountRpc.CreatePurchaseIntent,
            static reply => (reply.Value?.Purchase, reply.Error, reply.Meta),
            purchase => ValidPurchase(purchase, purchaseId) && ReadId(purchase.OfferId) == offerId,
            ct, mutation: true, hosted: true);
    }

    public Task<CheckoutView> CreateCheckoutAsync(Guid commandId, Guid purchaseId, Guid checkoutId, CancellationToken ct) =>
        AccountUnaryAsync(
            meta => new CommerceServiceCreateCheckoutAttemptRequest
            {
                Meta = AccountCommandMeta(meta, commandId),
                PurchaseIntentId = ExactId(purchaseId),
                CheckoutAttemptId = ExactId(checkoutId),
                ReturnRoute = BillingReturn,
            },
            AccountRpc.CreateCheckoutAttempt,
            static reply => (reply.Value?.Checkout, reply.Error, reply.Meta),
            checkout => ReadId(checkout.PurchaseIntentId) == purchaseId && ReadId(checkout.CheckoutAttemptId) == checkoutId &&
                ValidCheckoutLink(checkout.Url) && IsValidInstant(checkout.ExpiresAt) && InstantValue(checkout.ExpiresAt) > time.GetUtcNow(),
            ct, mutation: true, hosted: true);

    public Task<PurchaseView> ReadPurchaseAsync(Guid purchaseId, CancellationToken ct) =>
        AccountUnaryAsync(
            meta => new CommerceServiceGetPurchaseStateRequest { Meta = meta, PurchaseIntentId = ExactId(purchaseId) },
            AccountRpc.GetPurchaseState,
            static reply => (reply.Value?.Purchase, reply.Error, reply.Meta),
            purchase => ValidPurchase(purchase, purchaseId), ct, hosted: true);

    public Task<SubscriptionView> ReadSubscriptionAsync(CancellationToken ct) =>
        AccountUnaryAsync(
            static meta => new CommerceServiceGetSubscriptionRequest { Meta = meta },
            AccountRpc.GetSubscription,
            static reply => (reply.Value?.Subscription, reply.Error, reply.Meta),
            ValidSubscription, ct, hosted: true);

    public Task<SubscriptionView> SetRenewalAsync(Guid commandId, Guid subscriptionId, long expectedRevision, bool autoRenew,
        CancellationToken ct)
    {
        if (expectedRevision < 1)
            throw WorkValidation();
        if (autoRenew)
        {
            return AccountUnaryAsync(
                meta => new CommerceServiceReactivateSubscriptionRequest
                {
                    Meta = AccountCommandMeta(meta, commandId, expectedRevision),
                    SubscriptionId = ExactId(subscriptionId),
                },
                AccountRpc.ReactivateSubscription,
                static reply => (reply.Value?.Subscription, reply.Error, reply.Meta),
                ValidRenewal, ct, mutation: true, hosted: true);
        }

        return AccountUnaryAsync(
            meta => new CommerceServiceCancelSubscriptionRequest
            {
                Meta = AccountCommandMeta(meta, commandId, expectedRevision),
                SubscriptionId = ExactId(subscriptionId),
            },
            AccountRpc.CancelSubscription,
            static reply => (reply.Value?.Subscription, reply.Error, reply.Meta),
            ValidRenewal, ct, mutation: true, hosted: true, proofClass: CancelSubscription, proofTarget: subscriptionId);

        bool ValidRenewal(SubscriptionView subscription) => ValidSubscription(subscription) &&
            ReadId(subscription.SubscriptionId) == subscriptionId && subscription.Revision.Value >= expectedRevision;
    }

    public Task<CommerceServiceListBillingHistoryValue> ReadBillingAsync(DateTimeOffset from, DateTimeOffset until, string? cursor,
        CancellationToken ct)
    {
        if (from >= until)
            throw WorkValidation();
        return AccountUnaryAsync(
            meta => new CommerceServiceListBillingHistoryRequest
            {
                Meta = meta,
                Period = new TimeRangeUtc { From = ExactInstant(from), Until = ExactInstant(until) },
                Page = AccountPage(cursor, BillingCursorBytes),
            },
            AccountRpc.ListBillingHistory,
            static reply => (reply.Value, reply.Error, reply.Meta),
            static page => ValidAccountPage(page.Page, BillingCursorBytes) && page.Items.Count <= WorkPageSize &&
                page.Items.All(item => ReadId(item.ItemId) is not null && item.Amount is not null && IsValidInstant(item.OccurredAt)),
            ct, hosted: true);
    }

    private async Task<T> AccountUnaryAsync<TRequest, TReply, T>(
        Func<RequestMeta, TRequest> build,
        Method<TRequest, TReply> method,
        Func<TReply, (T? Value, ArcError? Error, ResponseMeta? Meta)> outcome,
        Func<T, bool> validate,
        CancellationToken ct,
        bool mutation = false, bool hosted = false, string? proofClass = null, Guid? proofTarget = null, Guid? revokeSession = null)
        where TRequest : class, IMessage
        where TReply : class
        where T : class
    {
        ct.ThrowIfCancellationRequested();
        if (hosted && Configuration.Mode != DashboardCenterMode.OfficialHosted)
            throw new DashboardApiException(DashboardFailure.NotConfigured);

        var session = RequireSession();
        var requestMeta = Meta(session);
        var request = BoundRequest(build(requestMeta), AccountRequestBytes);
        using var channel = OpenWorkChannel(session, AccountRequestBytes, AccountReplyBytes);
        var invoker = channel.CreateCallInvoker();
        var (value, error, meta) = await SendAsync();
        if (proofClass is not null && proofTarget is Guid target &&
            error is { Code: "step_up_required", Effect: EffectCertainty.DidNotHappen })
        {
            await ConfirmOperationAsync(proofClass, target, session, ct);
            EnsureCurrent(session, ct);
            (value, error, meta) = await SendAsync();
        }

        var result = Accept(value, error, meta, session, mutation);
        if (!validate(result))
            throw new DashboardApiException(mutation ? DashboardFailure.Uncertain : DashboardFailure.Incompatible, ReadId(meta?.CorrelationId));
        if (revokeSession is not null && revokeSession == session.View.SessionId)
            Invalidate(session);
        return result;

        async Task<(T? Value, ArcError? Error, ResponseMeta? Meta)> SendAsync()
        {
            var call = invoker.AsyncUnaryCall(method, null, Options(ct, deadline: AccountDeadline), request);
            var reply = outcome(await CallAsync(call, session, ct, mutation));
            // Worker-side denials can precede reading the request. A server success or step-up challenge echoes our correlation.
            if ((reply.Error is null || reply.Error.Code == "step_up_required") &&
                !requestMeta.CorrelationId.Equals(reply.Meta?.CorrelationId))
            {
                throw new DashboardApiException(mutation ? DashboardFailure.Uncertain : DashboardFailure.Incompatible);
            }
            return reply;
        }
    }

    private static RequestMeta AccountCommandMeta(RequestMeta meta, Guid commandId, long? expectedRevision = null)
    {
        meta.CommandId = ExactId(commandId);
        if (expectedRevision is long revision)
            meta.ExpectedRev = new Revision { Value = revision };
        return meta;
    }

    private static PageRequest AccountPage(string? cursor, int maximumCursorBytes = 4_096)
    {
        var page = new PageRequest { Limit = (int)WorkPageSize };
        if (!string.IsNullOrEmpty(cursor))
        {
            if (Encoding.UTF8.GetByteCount(cursor) > maximumCursorBytes)
                throw WorkValidation();
            page.Cursor = cursor;
        }
        return page;
    }

    private static bool ValidAccountPage(PageState? page, int maximumCursorBytes = 4_096) =>
        page is not null && Encoding.UTF8.GetByteCount(page.NextCursor) <= maximumCursorBytes &&
        page.HasMore == !string.IsNullOrEmpty(page.NextCursor);

    private static bool ValidAccountReceipt(Receipt receipt, Guid commandId) =>
        ReadId(receipt.CommandId) == commandId && receipt.Effect == EffectCertainty.Happened;

    private static bool ValidEntitlement(EntitlementSnapshot snapshot, DashboardCenterMode mode)
    {
        // Static self-hosting and unconfigured official service do not read the entitlement store.
        // A real hosted snapshot can have version zero before any effective entitlement changes.
        if (snapshot.Capabilities.Count != 3 ||
            (snapshot.HasVersion && (mode != DashboardCenterMode.OfficialHosted || snapshot.Version < 0)))
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in snapshot.Capabilities)
        {
            if (capability.Capability is not ("center.nodes" or "remote.view" or "remote.control") ||
                !keys.Add(capability.Capability) || !capability.HasAllowed || capability.Reasons.Count != 1 ||
                !ValidEntitlementDecision(mode, snapshot.HasVersion, capability.Allowed, capability.Reasons[0]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValidEntitlementDecision(DashboardCenterMode mode, bool versioned, bool allowed, string reason) => mode switch
    {
        DashboardCenterMode.SelfHosted => allowed ? reason == "self_hosted_default" : reason == "operator_limit",
        DashboardCenterMode.OfficialHosted when !versioned =>
            !allowed && reason is "plan_configuration_missing" or "plan_configuration_invalid",
        DashboardCenterMode.OfficialHosted => allowed ? reason == "entitled" :
            reason is "no_entitlement" or "subscription_expired" or "payment_grace" or "temporarily_restricted" or
                "account_suspended" or "feature_unavailable",
        _ => false,
    };

    private static string Region(string region) =>
        region is { Length: 2 } && region.All(char.IsAsciiLetterUpper) ? region : throw WorkValidation();

    private static bool ValidPriceVersion(string? version) =>
        !string.IsNullOrEmpty(version) && Encoding.UTF8.GetByteCount(version) <= 128;

    private static bool ValidPurchase(PurchaseView purchase, Guid purchaseId) =>
        ReadId(purchase.PurchaseIntentId) == purchaseId && ReadId(purchase.OfferId) is not null && !string.IsNullOrEmpty(purchase.State);

    private static bool ValidSubscription(SubscriptionView subscription) =>
        ReadId(subscription.SubscriptionId) is not null && subscription.Revision is { Value: > 0 } &&
        subscription.HasState && subscription.HasCancelAtPeriodEnd &&
        subscription.CurrentPeriod is { From: { } from, Until: { } until } &&
        IsValidInstant(from) && IsValidInstant(until) && InstantValue(from) < InstantValue(until);

    private static bool ValidCheckoutLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var target) && target.IsWellFormedOriginalString() &&
        target.Scheme == Uri.UriSchemeHttps && target.Host.Length != 0 && target.UserInfo.Length == 0 &&
        target.Fragment.Length == 0;

    private static Instant ExactInstant(DateTimeOffset instant) => new()
    {
        UnixSeconds = instant.ToUnixTimeSeconds(),
        Nanos = (uint)((instant.UtcTicks % TimeSpan.TicksPerSecond) * 100),
    };

    private static bool IsValidInstant(Instant? instant) =>
        instant is { HasUnixSeconds: true, UnixSeconds: >= -62_135_596_800 and <= 253_402_300_799, Nanos: < 1_000_000_000 };

    private static class AccountRpc
    {
        private const string Identity = "arcforges.publicapi.v1.IdentityService";
        private const string Entitlement = "arcforges.publicapi.v1.EntitlementService";
        private const string Commerce = "arcforges.publicapi.v1.CommerceService";

        // Use the published message codecs without rooting every stub in the generated service registries.
        public static readonly Method<IdentityServiceGetProfileRequest, IdentityServiceGetProfileResponse> GetProfile =
            Define<IdentityServiceGetProfileRequest, IdentityServiceGetProfileResponse>(Identity, "GetProfile", IdentityServiceGetProfileResponse.Parser);

        public static readonly Method<IdentityServiceUpdateProfileRequest, IdentityServiceUpdateProfileResponse> UpdateProfile =
            Define<IdentityServiceUpdateProfileRequest, IdentityServiceUpdateProfileResponse>(Identity, "UpdateProfile", IdentityServiceUpdateProfileResponse.Parser);

        public static readonly Method<IdentityServiceGetAccountDeletionRequest, IdentityServiceGetAccountDeletionResponse> GetAccountDeletion =
            Define<IdentityServiceGetAccountDeletionRequest, IdentityServiceGetAccountDeletionResponse>(Identity, "GetAccountDeletion", IdentityServiceGetAccountDeletionResponse.Parser);

        public static readonly Method<IdentityServiceListSessionsRequest, IdentityServiceListSessionsResponse> ListSessions =
            Define<IdentityServiceListSessionsRequest, IdentityServiceListSessionsResponse>(Identity, "ListSessions", IdentityServiceListSessionsResponse.Parser);

        public static readonly Method<IdentityServiceListAuthIdentitiesRequest, IdentityServiceListAuthIdentitiesResponse> ListAuthIdentities =
            Define<IdentityServiceListAuthIdentitiesRequest, IdentityServiceListAuthIdentitiesResponse>(Identity, "ListAuthIdentities", IdentityServiceListAuthIdentitiesResponse.Parser);

        public static readonly Method<IdentityServiceRevokeSessionRequest, IdentityServiceRevokeSessionResponse> RevokeSession =
            Define<IdentityServiceRevokeSessionRequest, IdentityServiceRevokeSessionResponse>(Identity, "RevokeSession", IdentityServiceRevokeSessionResponse.Parser);

        public static readonly Method<EntitlementServiceGetSnapshotRequest, EntitlementServiceGetSnapshotResponse> GetSnapshot =
            Define<EntitlementServiceGetSnapshotRequest, EntitlementServiceGetSnapshotResponse>(Entitlement, "GetSnapshot", EntitlementServiceGetSnapshotResponse.Parser);

        public static readonly Method<CommerceServiceGetCatalogueRequest, CommerceServiceGetCatalogueResponse> GetCatalogue =
            Define<CommerceServiceGetCatalogueRequest, CommerceServiceGetCatalogueResponse>(Commerce, "GetCatalogue", CommerceServiceGetCatalogueResponse.Parser);

        public static readonly Method<CommerceServiceCreatePurchaseIntentRequest, CommerceServiceCreatePurchaseIntentResponse> CreatePurchaseIntent =
            Define<CommerceServiceCreatePurchaseIntentRequest, CommerceServiceCreatePurchaseIntentResponse>(Commerce, "CreatePurchaseIntent", CommerceServiceCreatePurchaseIntentResponse.Parser);

        public static readonly Method<CommerceServiceCreateCheckoutAttemptRequest, CommerceServiceCreateCheckoutAttemptResponse> CreateCheckoutAttempt =
            Define<CommerceServiceCreateCheckoutAttemptRequest, CommerceServiceCreateCheckoutAttemptResponse>(Commerce, "CreateCheckoutAttempt", CommerceServiceCreateCheckoutAttemptResponse.Parser);

        public static readonly Method<CommerceServiceGetPurchaseStateRequest, CommerceServiceGetPurchaseStateResponse> GetPurchaseState =
            Define<CommerceServiceGetPurchaseStateRequest, CommerceServiceGetPurchaseStateResponse>(Commerce, "GetPurchaseState", CommerceServiceGetPurchaseStateResponse.Parser);

        public static readonly Method<CommerceServiceGetSubscriptionRequest, CommerceServiceGetSubscriptionResponse> GetSubscription =
            Define<CommerceServiceGetSubscriptionRequest, CommerceServiceGetSubscriptionResponse>(Commerce, "GetSubscription", CommerceServiceGetSubscriptionResponse.Parser);

        public static readonly Method<CommerceServiceReactivateSubscriptionRequest, CommerceServiceReactivateSubscriptionResponse> ReactivateSubscription =
            Define<CommerceServiceReactivateSubscriptionRequest, CommerceServiceReactivateSubscriptionResponse>(Commerce, "ReactivateSubscription", CommerceServiceReactivateSubscriptionResponse.Parser);

        public static readonly Method<CommerceServiceCancelSubscriptionRequest, CommerceServiceCancelSubscriptionResponse> CancelSubscription =
            Define<CommerceServiceCancelSubscriptionRequest, CommerceServiceCancelSubscriptionResponse>(Commerce, "CancelSubscription", CommerceServiceCancelSubscriptionResponse.Parser);

        public static readonly Method<CommerceServiceListBillingHistoryRequest, CommerceServiceListBillingHistoryResponse> ListBillingHistory =
            Define<CommerceServiceListBillingHistoryRequest, CommerceServiceListBillingHistoryResponse>(Commerce, "ListBillingHistory", CommerceServiceListBillingHistoryResponse.Parser);

        private static Method<TRequest, TReply> Define<TRequest, TReply>(string service, string method, MessageParser<TReply> parser)
            where TRequest : class, IMessage<TRequest>, IBufferMessage
            where TReply : class, IMessage<TReply>, IBufferMessage
        {
            var request = Marshallers.Create<TRequest>(Write, static _ => throw new NotSupportedException());
            var reply = Marshallers.Create<TReply>(static (_, _) => throw new NotSupportedException(), context =>
            {
                var payload = context.PayloadAsReadOnlySequence();
                if (payload.Length > AccountReplyBytes)
                    throw new RpcException(new Status(StatusCode.ResourceExhausted, "The account response exceeds the published limit."));
                return parser.ParseFrom(payload);
            });
            return new Method<TRequest, TReply>(MethodType.Unary, service, method, request, reply);
        }

        private static void Write<T>(T message, SerializationContext context) where T : class, IMessage<T>, IBufferMessage
        {
            var size = message.CalculateSize();
            if (size > AccountRequestBytes)
                throw new RpcException(new Status(StatusCode.ResourceExhausted, "The account request exceeds the published limit."));
            context.SetPayloadLength(size);
            MessageExtensions.WriteTo(message, context.GetBufferWriter());
            context.Complete();
        }
    }
}
