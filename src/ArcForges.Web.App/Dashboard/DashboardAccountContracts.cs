// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Contracts.Sandbox.V2;

namespace ArcForges.Web.App.Dashboard;

public interface IDashboardAccount
{
    Task<AccountProfile> ReadProfileAsync(CancellationToken ct);
    Task<AccountProfile> UpdateProfileAsync(Guid commandId, long expectedRevision, ProfileUpdate patch, CancellationToken ct);
    Task<DeletionStatus> ReadAccountDeletionAsync(CancellationToken ct);
}

public interface IDashboardSecurity
{
    Task<IdentityServiceListSessionsValue> ListSessionsAsync(string? cursor, CancellationToken ct);
    Task<IdentityServiceListAuthIdentitiesValue> ListCredentialsAsync(string? cursor, CancellationToken ct);
    Task<Receipt> RevokeSessionAsync(Guid sessionId, Guid commandId, CancellationToken ct);
    Task<RemoteGrant> RevokeGrantAsync(DashboardWorkTarget target, DashboardWorkCommand command, CancellationToken ct);
}

public interface IDashboardSubscription
{
    Task<EntitlementSnapshot> ReadEntitlementAsync(CancellationToken ct);
    Task<CommerceServiceGetCatalogueValue> ReadCatalogueAsync(string region, CancellationToken ct);
    Task<PurchaseView> CreatePurchaseAsync(Guid commandId, Guid purchaseId, Guid offerId, string priceVersion, string region, CancellationToken ct);
    Task<CheckoutView> CreateCheckoutAsync(Guid commandId, Guid purchaseId, Guid checkoutId, CancellationToken ct);
    Task<PurchaseView> ReadPurchaseAsync(Guid purchaseId, CancellationToken ct);
    Task<SubscriptionView> ReadSubscriptionAsync(CancellationToken ct);
    Task<SubscriptionView> SetRenewalAsync(Guid commandId, Guid subscriptionId, long expectedRevision, bool autoRenew, CancellationToken ct);
    Task<CommerceServiceListBillingHistoryValue> ReadBillingAsync(DateTimeOffset from, DateTimeOffset until, string? cursor, CancellationToken ct);
}
