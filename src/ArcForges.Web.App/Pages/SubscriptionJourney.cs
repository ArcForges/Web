// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Web.App.Dashboard;

namespace ArcForges.Web.App.Pages;

internal sealed class SubscriptionJourney
{
    internal SubscriptionPurchase? Purchase { get; set; }
    internal SubscriptionRenewal? Renewal { get; set; }
    internal event Action? Changed;
    internal void NotifyChanged() => Changed?.Invoke();
}

internal sealed class SubscriptionPurchase(Guid id, Offer? originalOffer = null, Guid? commandId = null)
{
    internal Guid Id { get; } = id;
    internal Offer? OriginalOffer { get; } = originalOffer;
    internal Guid? CommandId { get; } = commandId;
    internal PurchaseView? Current { get; set; }
    internal SubscriptionCheckout? Checkout { get; set; }
    internal DashboardApiException? Failure { get; set; }
    internal bool Sending { get; set; }
    internal bool VerifiedMissing { get; set; }
}

internal sealed class SubscriptionCheckout(Guid attemptId, Guid commandId)
{
    internal Guid AttemptId { get; } = attemptId;
    internal Guid CommandId { get; } = commandId;
    internal Uri? Link { get; set; }
    internal DateTimeOffset? ExpiresAt { get; set; }
    internal bool VerifiedPending { get; set; }
}

internal sealed class SubscriptionRenewal(Guid commandId, Guid subscriptionId, long expectedRevision,
    bool autoRenew, DateTimeOffset periodEndsAt)
{
    internal Guid CommandId { get; } = commandId;
    internal Guid SubscriptionId { get; } = subscriptionId;
    internal long ExpectedRevision { get; } = expectedRevision;
    internal bool AutoRenew { get; } = autoRenew;
    internal DateTimeOffset PeriodEndsAt { get; } = periodEndsAt;
    internal SubscriptionView? Receipt { get; set; }
    internal DashboardApiException? Failure { get; set; }
    internal bool Sending { get; set; }
    internal bool Checked { get; set; }
}
