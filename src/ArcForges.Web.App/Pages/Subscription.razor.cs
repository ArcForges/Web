// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ArcForges.Web.App.Pages;

public partial class Subscription : IDisposable
{
    [Inject] private IDashboardCenter Center { get; set; } = default!;
    [Inject] private IDashboardSubscription Billing { get; set; } = default!;
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [CascadingParameter] private DashboardAccess Access { get; set; } = default!;

    private CancellationTokenSource _lifetime = default!;
    private CancellationTokenSource? _linkExpiry;
    private SubscriptionView? _subscription;
    private EntitlementSnapshot? _entitlement;
    private CommerceServiceGetCatalogueValue? _catalogue;
    private CommerceServiceListBillingHistoryValue? _history;
    private DashboardApiException? _subscriptionFailure;
    private DashboardApiException? _entitlementFailure;
    private DashboardApiException? _catalogueFailure;
    private DashboardApiException? _historyFailure;
    private DashboardApiException? _startFailure;
    private DashboardApiException? _renewalFailure;
    private Confirmation? _confirmation;
    private readonly Dictionary<Guid, ElementReference> _offerReviews = new();
    private ElementReference _renewalHeading;
    private ElementReference _checkoutHeading;
    private ElementReference _confirmationHeading;
    private ElementReference _renewalReview;
    private ElementReference _renewalRetryReview;
    private ElementReference _checkoutReview;
    private ElementReference _purchaseRetryReview;
    private ElementReference _confirmationTrigger;
    private ElementReference? _restoreFocus;
    private string _region = "";
    private string? _catalogueRegion;
    private string _previousPurchase = "";
    private string? _historyCursor;
    private DateTimeOffset _historyUntil;
    private bool _disposed;
    private bool _refreshing;
    private bool _subscriptionMissing;
    private bool _loadingCatalogue;
    private bool _checkingPurchase;
    private bool _purchaseCheckPending;
    private bool _purchaseReadFailed;
    private bool _loadingHistory;
    private bool _regionInvalid;
    private bool _purchaseIdInvalid;
    private bool _focusConfirmation;

    private SubscriptionJourney Journey => Access.Subscription;
    private bool Official => Center.Configuration.Mode == DashboardCenterMode.OfficialHosted;
    private bool Configured => Center.Configuration.Mode != DashboardCenterMode.Unconfigured;
    private bool CatalogueCurrent => _catalogue is not null && _catalogueRegion == RegionCode(_region);
    private bool PurchaseChecking => _checkingPurchase || _purchaseCheckPending;
    private bool CanStartPurchase => Official && !_refreshing && !_loadingCatalogue && !PurchaseChecking && CatalogueCurrent &&
        Journey.Purchase is null && _subscriptionFailure is null && (_subscriptionMissing || _subscription?.State == SubscriptionState.Ended);
    private bool CanChangeRenewal => Official && !_refreshing && _subscriptionFailure is null && Journey.Renewal is not { Receipt: null } &&
        _subscription is { State: SubscriptionState.Active or SubscriptionState.CancelScheduled, HasCancelAtPeriodEnd: true, Revision.Value: > 0 and < long.MaxValue } value &&
        RunPresentation.ReadId(value.SubscriptionId) is not null && RunPresentation.ReadTime(value.CurrentPeriod?.Until) > Clock.GetUtcNow() &&
        (Journey.Renewal?.Receipt is not { } receipt || !NewerThan(receipt, value));

    protected override Task OnInitializedAsync()
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(Access.Cancellation);
        Journey.Changed += JourneyChanged;
        Access.ViewResumed += ViewResumed;
        ScheduleCheckoutExpiry();
        return Official
            ? Task.WhenAll(RefreshAsync(), ReadLatestBillingAsync(), ReadActivePurchaseAsync())
            : RefreshAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _lifetime.IsCancellationRequested)
            return;
        if (_focusConfirmation && _confirmation is not null)
        {
            _focusConfirmation = false;
            await _confirmationHeading.FocusAsync();
        }
        else if (_restoreFocus is { } target)
        {
            _restoreFocus = null;
            await target.FocusAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _refreshing || !Configured)
            return;

        _refreshing = true;
        var ct = _lifetime.Token;
        try
        {
            await Task.WhenAll(ReadEntitlementAsync(ct), Official ? ReadSubscriptionAsync(ct) : Task.CompletedTask);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task ReadEntitlementAsync(CancellationToken ct)
    {
        try
        {
            var snapshot = await Billing.ReadEntitlementAsync(ct);
            if (!ct.IsCancellationRequested)
            {
                _entitlement = snapshot;
                _entitlementFailure = null;
            }
        }
        catch (DashboardApiException error) when (!ct.IsCancellationRequested)
        {
            _entitlement = null;
            _entitlementFailure = error;
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task ReadSubscriptionAsync(CancellationToken ct)
    {
        if (Journey.Renewal is { Receipt: null, Sending: false } pending)
            pending.Checked = false;
        try
        {
            var value = await Billing.ReadSubscriptionAsync(ct);
            if (ct.IsCancellationRequested)
                return;

            if (_subscription is null || !NewerThan(_subscription, value))
                _subscription = value;
            _subscriptionMissing = false;
            _subscriptionFailure = null;
            if (Journey.Renewal is { Receipt: null, Sending: false } renewal && RunPresentation.ReadId(value.SubscriptionId) == renewal.SubscriptionId)
                renewal.Checked = true;
        }
        catch (DashboardApiException error) when (!ct.IsCancellationRequested)
        {
            _subscription = null;
            _subscriptionMissing = error.Failure == DashboardFailure.NotFound && error.Code == "not_found";
            _subscriptionFailure = _subscriptionMissing ? null : error;
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static bool NewerThan(SubscriptionView current, SubscriptionView value) =>
        RunPresentation.ReadId(current.SubscriptionId) == RunPresentation.ReadId(value.SubscriptionId) && current.Revision?.Value > value.Revision?.Value;

    private async Task ReadCatalogueAsync()
    {
        if (_disposed || _loadingCatalogue || !Official || Journey.Purchase is not null)
            return;

        var region = RegionCode(_region);
        _regionInvalid = region is null;
        if (region is null)
            return;

        _loadingCatalogue = true;
        _confirmation = null;
        _focusConfirmation = false;
        _offerReviews.Clear();
        _catalogue = null;
        _catalogueRegion = region;
        _catalogueFailure = null;
        _startFailure = null;
        var ct = _lifetime.Token;
        try
        {
            var value = await Billing.ReadCatalogueAsync(region, ct);
            if (ct.IsCancellationRequested || RegionCode(_region) != region)
                return;

            if (value.Items.Any(offer => !ValidOffer(offer, region)))
                throw new DashboardApiException(DashboardFailure.Incompatible);
            _catalogue = value;
        }
        catch (DashboardApiException error) when (!ct.IsCancellationRequested)
        {
            if (RegionCode(_region) == region)
                _catalogueFailure = error;
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            _loadingCatalogue = false;
        }
    }

    private void ReviewPurchase(Offer offer, Guid offerId)
    {
        if (CanStartPurchase && _catalogue!.Items.Contains(offer))
            OpenConfirmation(new(BillingAction.StartPurchase, Offer: offer), _offerReviews.GetValueOrDefault(offerId));
    }

    private bool CanResumePurchase(SubscriptionPurchase purchase) => !PurchaseChecking && !purchase.Sending && purchase.VerifiedMissing &&
        purchase.Current is null && purchase.OriginalOffer is not null && purchase.CommandId is not null;

    private void ReviewOriginalPurchase()
    {
        if (Journey.Purchase is { } purchase && CanResumePurchase(purchase))
            OpenConfirmation(new(BillingAction.ResumePurchase, Offer: purchase.OriginalOffer, Purchase: purchase), _purchaseRetryReview);
    }

    private bool CanPrepareCheckout(SubscriptionPurchase purchase) => !PurchaseChecking && !purchase.Sending &&
        purchase.Failure is null && purchase.OriginalOffer is not null &&
        purchase.Current?.State == "pending" && purchase.Checkout is null;

    private bool CanResumeCheckout(SubscriptionPurchase purchase) => !PurchaseChecking && !purchase.Sending &&
        purchase.Failure is null && purchase.Current?.State == "pending" &&
        purchase.Checkout is { VerifiedPending: true } && CheckoutLink(purchase) is null;

    private void ReviewCheckout()
    {
        if (Journey.Purchase is not { } purchase)
            return;
        if (CanPrepareCheckout(purchase))
            OpenConfirmation(new(BillingAction.PrepareCheckout, Offer: purchase.OriginalOffer, Purchase: purchase), _checkoutReview);
        else if (CanResumeCheckout(purchase))
            OpenConfirmation(new(BillingAction.ResumeCheckout, Offer: purchase.OriginalOffer, Purchase: purchase), _checkoutReview);
    }

    private void ReviewRenewal()
    {
        if (CanChangeRenewal && _subscription is { } value && RunPresentation.ReadId(value.SubscriptionId) is Guid id &&
            RunPresentation.ReadTime(value.CurrentPeriod?.Until) is DateTimeOffset end)
        {
            OpenConfirmation(new(value.CancelAtPeriodEnd ? BillingAction.Reactivate : BillingAction.CancelRenewal,
                Renewal: new(Guid.NewGuid(), id, value.Revision.Value, value.CancelAtPeriodEnd, end)), _renewalReview);
        }
    }

    private void ReviewOriginalRenewal()
    {
        if (Journey.Renewal is { } original && CanResumeRenewal(original))
            OpenConfirmation(new(BillingAction.ResumeRenewal, Renewal: original), _renewalRetryReview);
    }

    private bool CanResumeRenewal(SubscriptionRenewal renewal) => !_refreshing && _subscriptionFailure is null &&
        renewal is { Checked: true, Sending: false, Receipt: null } && _subscription is { } current &&
        RunPresentation.ReadId(current.SubscriptionId) == renewal.SubscriptionId && current.Revision?.Value >= renewal.ExpectedRevision;

    private void OpenConfirmation(Confirmation confirmation, ElementReference trigger)
    {
        _confirmation = confirmation;
        _confirmationTrigger = trigger;
        _restoreFocus = null;
        _focusConfirmation = true;
    }

    private void ConfirmationKeyDown(KeyboardEventArgs key)
    {
        if (key.Key == "Escape")
            CloseConfirmation();
    }

    private void CloseConfirmation() => DismissConfirmation(restoreTrigger: true);

    private void DismissConfirmation(bool restoreTrigger)
    {
        if (_confirmation is not { } confirmation)
            return;
        var section = confirmation.Renewal is null ? _checkoutHeading : _renewalHeading;
        _restoreFocus = restoreTrigger && CanConfirm(confirmation) && !string.IsNullOrEmpty(_confirmationTrigger.Id)
            ? _confirmationTrigger
            : section;
        _confirmation = null;
        _focusConfirmation = false;
    }

    private bool CanConfirm(Confirmation request) => !_disposed && Official && (request switch
    {
        { Action: BillingAction.StartPurchase, Offer: { } offer } => CanStartPurchase && _catalogue!.Items.Contains(offer),
        { Action: BillingAction.ResumePurchase, Purchase: { } purchase } => Journey.Purchase == purchase && CanResumePurchase(purchase),
        { Action: BillingAction.PrepareCheckout, Purchase: { } purchase } => Journey.Purchase == purchase && CanPrepareCheckout(purchase),
        { Action: BillingAction.ResumeCheckout, Purchase: { Checkout: not null } purchase } =>
            Journey.Purchase == purchase && CanResumeCheckout(purchase),
        { Action: BillingAction.CancelRenewal or BillingAction.Reactivate, Renewal: { } renewal } =>
            CanChangeRenewal && MatchesRenewalBase(renewal),
        { Action: BillingAction.ResumeRenewal, Renewal: { } renewal } => Journey.Renewal == renewal && CanResumeRenewal(renewal),
        _ => false,
    });

    private Task ConfirmAsync()
    {
        if (_confirmation is not { } confirmation || !CanConfirm(confirmation))
            return Task.CompletedTask;

        DismissConfirmation(restoreTrigger: false);
        switch (confirmation)
        {
            case { Action: BillingAction.StartPurchase, Offer: { } offer }:
                return ReservePurchaseAsync(new(Guid.NewGuid(), offer.Clone(), Guid.NewGuid()), retry: false);
            case { Action: BillingAction.ResumePurchase, Purchase: { } purchase }:
                return ReservePurchaseAsync(purchase, retry: true);
            case { Action: BillingAction.PrepareCheckout, Purchase: { } purchase }:
                return PrepareCheckoutAsync(purchase, new(Guid.NewGuid(), Guid.NewGuid()), retry: false);
            case { Action: BillingAction.ResumeCheckout, Purchase: { Checkout: { } checkout } purchase }:
                return PrepareCheckoutAsync(purchase, checkout, retry: true);
            case { Action: BillingAction.CancelRenewal or BillingAction.Reactivate, Renewal: { } renewal }:
                return SubmitRenewalAsync(renewal, retry: false);
            case { Action: BillingAction.ResumeRenewal, Renewal: { } renewal }:
                return SubmitRenewalAsync(renewal, retry: true);
            default:
                return Task.CompletedTask;
        }
    }

    private async Task ReservePurchaseAsync(SubscriptionPurchase purchase, bool retry)
    {
        if (purchase.OriginalOffer is not { } offer || RunPresentation.ReadId(offer.OfferId) is not Guid offerId || purchase.CommandId is not Guid command)
            return;

        var journey = Journey;
        var access = Access;
        journey.Purchase = purchase;
        purchase.Sending = true;
        _purchaseReadFailed = false;
        purchase.VerifiedMissing = false;
        purchase.Failure = null;
        _startFailure = null;
        journey.NotifyChanged();
        try
        {
            var result = await Billing.CreatePurchaseAsync(command, purchase.Id, offerId, offer.PriceVersion, offer.Region, access.Cancellation);
            access.Cancellation.ThrowIfCancellationRequested();
            AcceptPurchase(purchase, result, mutation: true);
        }
        catch (DashboardApiException error) when (!access.Cancellation.IsCancellationRequested)
        {
            if (retry || error.Failure == DashboardFailure.Uncertain)
                purchase.Failure = error;
            else
            {
                journey.Purchase = null;
                if (!_disposed)
                    _startFailure = error;
            }
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            purchase.Sending = false;
            journey.NotifyChanged();
        }
    }

    private async Task PrepareCheckoutAsync(SubscriptionPurchase purchase, SubscriptionCheckout checkout, bool retry)
    {
        var journey = Journey;
        var access = Access;
        purchase.Checkout = checkout;
        purchase.Sending = true;
        purchase.Failure = null;
        checkout.VerifiedPending = false;
        ClearLink(checkout);
        journey.NotifyChanged();
        try
        {
            var result = await Billing.CreateCheckoutAsync(checkout.CommandId, purchase.Id, checkout.AttemptId, access.Cancellation);
            access.Cancellation.ThrowIfCancellationRequested();
            if (RunPresentation.ReadId(result.PurchaseIntentId) != purchase.Id || RunPresentation.ReadId(result.CheckoutAttemptId) != checkout.AttemptId ||
                RunPresentation.ReadTime(result.ExpiresAt) is not DateTimeOffset expires || expires <= Clock.GetUtcNow() ||
                !TryExternalHttps(result.Url, out var link))
                throw new DashboardApiException(DashboardFailure.Uncertain);

            checkout.Link = link;
            checkout.ExpiresAt = expires;
        }
        catch (DashboardApiException error) when (!access.Cancellation.IsCancellationRequested)
        {
            purchase.Failure = error;
            if (!retry && error.Failure != DashboardFailure.Uncertain)
                purchase.Checkout = null;
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            purchase.Sending = false;
            journey.NotifyChanged();
        }
    }

    private Task FindPurchaseAsync()
    {
        _purchaseIdInvalid = !Guid.TryParseExact(_previousPurchase, "D", out var id) || id == Guid.Empty;
        if (_disposed || !Official || PurchaseChecking || _purchaseIdInvalid || Journey.Purchase is not null)
            return Task.CompletedTask;

        Journey.Purchase = new SubscriptionPurchase(id);
        _confirmation = null;
        _focusConfirmation = false;
        _previousPurchase = "";
        return ReadActivePurchaseAsync();
    }

    private async Task ReadActivePurchaseAsync()
    {
        if (_disposed || !Official || _checkingPurchase || Journey.Purchase is not { Sending: false } purchase)
            return;

        var journey = Journey;
        _checkingPurchase = true;
        var ct = _lifetime.Token;
        try
        {
            do
            {
                _purchaseCheckPending = false;
                await ReadPurchaseSnapshotAsync(purchase, ct);
            }
            while (_purchaseCheckPending && !ct.IsCancellationRequested &&
                ReferenceEquals(Journey.Purchase, purchase) && !purchase.Sending);
        }
        finally
        {
            _checkingPurchase = false;
            journey.NotifyChanged();
        }
    }

    private async Task ReadPurchaseSnapshotAsync(SubscriptionPurchase purchase, CancellationToken ct)
    {
        _purchaseReadFailed = false;
        purchase.Failure = null;
        purchase.VerifiedMissing = false;
        if (purchase.Checkout is { } checkout)
            checkout.VerifiedPending = false;
        try
        {
            var result = await Billing.ReadPurchaseAsync(purchase.Id, ct);
            if (!IsCurrentPurchaseRead(purchase, ct))
                return;
            AcceptPurchase(purchase, result, mutation: false);
            if (purchase.Checkout is { } checkedCheckout)
                checkedCheckout.VerifiedPending = result.State == "pending";
            if (result.State == "completed")
                await RefreshAsync();
        }
        catch (DashboardApiException error) when (IsCurrentPurchaseRead(purchase, ct))
        {
            purchase.Failure = error;
            _purchaseReadFailed = true;
            purchase.VerifiedMissing = error.Failure == DashboardFailure.NotFound && error.Code == "not_found";
            ClearLink(purchase.Checkout);
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException)
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private bool IsCurrentPurchaseRead(SubscriptionPurchase purchase, CancellationToken ct) =>
        !ct.IsCancellationRequested && !_purchaseCheckPending && ReferenceEquals(Journey.Purchase, purchase);

    private static void AcceptPurchase(SubscriptionPurchase purchase, PurchaseView result, bool mutation)
    {
        var originalOfferId = RunPresentation.ReadId(purchase.OriginalOffer?.OfferId);
        var returnedOfferId = RunPresentation.ReadId(result.OfferId);
        if (RunPresentation.ReadId(result.PurchaseIntentId) != purchase.Id || returnedOfferId is null ||
            originalOfferId is not null && returnedOfferId != originalOfferId || result.State is not ("pending" or "unknown" or "completed" or "expired"))
        {
            throw new DashboardApiException(mutation ? DashboardFailure.Uncertain : DashboardFailure.Incompatible);
        }

        purchase.Current = result;
        if (result.State != "pending")
            ClearLink(purchase.Checkout);
    }

    private bool MatchesRenewalBase(SubscriptionRenewal renewal) => _subscription is { } value &&
        RunPresentation.ReadId(value.SubscriptionId) == renewal.SubscriptionId && value.Revision?.Value == renewal.ExpectedRevision &&
        value.CancelAtPeriodEnd == renewal.AutoRenew && RunPresentation.ReadTime(value.CurrentPeriod?.Until) == renewal.PeriodEndsAt;

    private bool CurrentMatches(SubscriptionRenewal renewal) => !_refreshing && _subscriptionFailure is null && _subscription is { } value &&
        RunPresentation.ReadId(value.SubscriptionId) == renewal.SubscriptionId && value.Revision?.Value > renewal.ExpectedRevision &&
        value.HasCancelAtPeriodEnd && value.CancelAtPeriodEnd != renewal.AutoRenew &&
        value.State == (renewal.AutoRenew ? SubscriptionState.Active : SubscriptionState.CancelScheduled);

    private async Task SubmitRenewalAsync(SubscriptionRenewal renewal, bool retry)
    {
        var journey = Journey;
        var access = Access;
        journey.Renewal = renewal;
        renewal.Sending = true;
        renewal.Checked = false;
        renewal.Failure = null;
        _renewalFailure = null;
        journey.NotifyChanged();
        try
        {
            var result = await Billing.SetRenewalAsync(renewal.CommandId, renewal.SubscriptionId, renewal.ExpectedRevision, renewal.AutoRenew, access.Cancellation);
            access.Cancellation.ThrowIfCancellationRequested();
            if (RunPresentation.ReadId(result.SubscriptionId) != renewal.SubscriptionId || result.Revision?.Value < renewal.ExpectedRevision ||
                result.Revision is not { HasValue: true } || !result.HasCancelAtPeriodEnd || result.CancelAtPeriodEnd == renewal.AutoRenew ||
                result.State != (renewal.AutoRenew ? SubscriptionState.Active : SubscriptionState.CancelScheduled) ||
                RunPresentation.ReadTime(result.CurrentPeriod?.Until) is null)
            {
                throw new DashboardApiException(DashboardFailure.Uncertain);
            }

            renewal.Receipt = result;
            if (!_disposed)
                await RefreshAsync();
        }
        catch (DashboardApiException error) when (!access.Cancellation.IsCancellationRequested)
        {
            if (retry || error.Failure == DashboardFailure.Uncertain)
                renewal.Failure = error;
            else
            {
                journey.Renewal = null;
                if (!_disposed)
                {
                    _renewalFailure = error;
                    if (error.Failure == DashboardFailure.Conflict)
                    {
                        _subscription = null;
                        _subscriptionFailure = error;
                    }
                }
            }
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (access.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            renewal.Sending = false;
            journey.NotifyChanged();
        }
    }

    private Task ReadLatestBillingAsync()
    {
        if (_historyUntil == default)
            _historyUntil = Clock.GetUtcNow();
        return ReadBillingAsync(null);
    }

    private Task RefreshBillingAsync()
    {
        _historyUntil = Clock.GetUtcNow();
        return ReadBillingAsync(null);
    }

    private Task ReadNextBillingAsync() => _history?.Page is { HasMore: true, NextCursor.Length: > 0 } page
        ? ReadBillingAsync(page.NextCursor) : Task.CompletedTask;

    private async Task ReadBillingAsync(string? cursor)
    {
        if (_disposed || !Official || _loadingHistory)
            return;
        _loadingHistory = true;
        _historyFailure = null;
        var ct = _lifetime.Token;
        try
        {
            var value = await Billing.ReadBillingAsync(_historyUntil.AddDays(-90), _historyUntil, cursor, ct);
            if (!ct.IsCancellationRequested)
            {
                _history = value;
                _historyCursor = cursor;
            }
        }
        catch (DashboardApiException error) when (!ct.IsCancellationRequested)
        {
            _historyFailure = error;
            await ExpireIfNeededAsync(error);
        }
        catch (DashboardApiException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            _loadingHistory = false;
        }
    }

    private Task ExpireIfNeededAsync(DashboardApiException error) => error.Failure == DashboardFailure.Unauthenticated
        ? Access.ExpireAsync() : Task.CompletedTask;

    private void ClosePurchase()
    {
        if (!PurchaseChecking && Journey.Purchase is { Sending: false } purchase &&
            (IsPurchaseTerminal(purchase) || (purchase.CommandId is null && purchase.VerifiedMissing)))
        {
            Journey.Purchase = null;
            _confirmation = null;
            Journey.NotifyChanged();
        }
    }

    private Uri? CheckoutLink(SubscriptionPurchase purchase) => !PurchaseChecking && !purchase.Sending &&
        !_purchaseReadFailed && purchase.Current?.State == "pending" &&
        purchase.Checkout is { Link: { } uri, ExpiresAt: { } expires } && expires > Clock.GetUtcNow() ? uri : null;

    private static void ClearLink(SubscriptionCheckout? checkout)
    {
        if (checkout is not null)
        {
            checkout.Link = null;
            checkout.ExpiresAt = null;
        }
    }

    // Center verifies the provider transaction and deployment-owned origin allowlist before returning a URL.
    // The browser admits only an explicit HTTPS navigation from that typed reply.
    private static bool TryExternalHttps(string value, out Uri? link)
    {
        link = null;
        if (value.Length is 0 or > 2048 || !value.StartsWith("https://", StringComparison.Ordinal) ||
            value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character is '\\' or '#') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 ||
            uri.UserInfo.Length != 0 || uri.Host.EndsWith('.') || uri.Fragment.Length != 0)
        {
            return false;
        }

        link = uri;
        return true;
    }

    private void ScheduleCheckoutExpiry()
    {
        _linkExpiry?.Cancel();
        if (Journey.Purchase?.Checkout is not { Link: not null, ExpiresAt: { } expires } checkout)
            return;

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _linkExpiry = lifetime;
        _ = ExpireLinkAsync(checkout, expires, lifetime);
    }

    private async Task ExpireLinkAsync(SubscriptionCheckout checkout, DateTimeOffset expires, CancellationTokenSource lifetime)
    {
        try
        {
            while (expires > Clock.GetUtcNow())
            {
                var delay = expires - Clock.GetUtcNow();
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay < TimeSpan.FromDays(1) ? delay : TimeSpan.FromDays(1), Clock, lifetime.Token);
            }
            if (!lifetime.IsCancellationRequested && checkout.ExpiresAt == expires)
            {
                ClearLink(checkout);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_linkExpiry, lifetime))
                _linkExpiry = null;
            lifetime.Dispose();
        }
    }

    private void ViewResumed() => _ = InvokeAsync(() =>
    {
        if (_disposed || !Official || Journey.Purchase is null)
            return Task.CompletedTask;

        _purchaseCheckPending = true;
        StateHasChanged();
        return ReadActivePurchaseAsync();
    });

    private void JourneyChanged() => _ = InvokeAsync(() =>
    {
        if (_disposed)
            return Task.CompletedTask;
        if (Journey.Purchase is null)
            _purchaseCheckPending = false;
        ScheduleCheckoutExpiry();
        StateHasChanged();
        return _purchaseCheckPending && !_checkingPurchase ? ReadActivePurchaseAsync() : Task.CompletedTask;
    });

    public void Dispose()
    {
        _disposed = true;
        Access.ViewResumed -= ViewResumed;
        Journey.Changed -= JourneyChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private static bool IsPurchaseTerminal(SubscriptionPurchase purchase) => purchase.Current?.State is "completed" or "expired";

    private string PurchaseStatus(SubscriptionPurchase purchase)
    {
        if (purchase.Sending || PurchaseChecking)
            return "Checking the purchase request…";
        if (_purchaseReadFailed)
            return "The latest purchase status is unavailable. Check the original purchase for updates.";

        return purchase.Current?.State switch
        {
            "completed" => "The Center verified the purchase.",
            "expired" => "The purchase expired.",
            "unknown" => "Purchase outcome unconfirmed. Check the original purchase for updates.",
            "pending" when purchase.Checkout is { Link: null, VerifiedPending: false } => "Checkout result unconfirmed. Check the original purchase for updates.",
            "pending" when purchase.Checkout is not null => "Payment confirmation pending.",
            "pending" => "Purchase prepared. Review checkout to continue.",
            _ => "Purchase state unavailable. Check the original purchase.",
        };
    }

    private static string SubscriptionStatus(SubscriptionState state) => state switch
    {
        SubscriptionState.Pending => "Pending",
        SubscriptionState.Active => "Active",
        SubscriptionState.Grace => "Grace period",
        SubscriptionState.CancelScheduled => "Cancellation scheduled",
        SubscriptionState.Ended => "Ended",
        SubscriptionState.Suspended => "Suspended",
        _ => "Unavailable",
    };

    private static string SubscriptionFailureText(DashboardApiException failure) => failure.Reason switch
    {
        "provider_unknown" => "The provider has not confirmed the subscription state.",
        "period_unverified" => "The Center has not verified a subscription period.",
        _ => "Hosted subscription status is unavailable.",
    };

    private static string CatalogueFailureText(DashboardApiException failure) => failure.Reason == "sales_closed" ||
        failure.Failure is DashboardFailure.NotConfigured or DashboardFailure.NotFound or DashboardFailure.Incompatible
        ? "Hosted sales are unavailable on this Center." : "The Center could not load offers.";

    private static RenderFragment Failure(DashboardApiException error, string? message = null) => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "role", "status");
        builder.AddContent(2, message ?? BillingErrorText(error));
        if (error.CorrelationId is Guid reference)
        {
            builder.AddContent(3, " Reference ");
            builder.OpenElement(4, "code");
            builder.AddContent(5, reference.ToString("D"));
            builder.CloseElement();
        }
        builder.CloseElement();
    };

    private static string BillingErrorText(DashboardApiException error) => error.Failure switch
    {
        DashboardFailure.NotFound => "The requested billing record is unavailable.",
        DashboardFailure.Conflict => "Billing details changed. Refresh the status before reviewing another change.",
        DashboardFailure.Validation => "The Center refused this billing request.",
        DashboardFailure.Uncertain => "The Center has not confirmed the result. Check the original status.",
        _ => error.Message,
    };

    private static string? RegionCode(string input) => input.Length == 2 && input.All(char.IsAsciiLetter) ? input.ToUpperInvariant() : null;

    private static bool ValidOffer(Offer offer, string region) => RunPresentation.ReadId(offer.OfferId) is not null &&
        offer.Kind == "subscription" && offer.Region == region && offer.PriceVersion.Length is > 0 and <= 128 &&
        offer.Interval is "month" or "year" && offer.Currency.Length == 3 && offer.Currency.All(char.IsAsciiLetterUpper) &&
        offer.Amount is { HasValue: true } amount && decimal.TryParse(amount.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static string OfferPrice(Offer offer) => $"{offer.Amount.Value} {offer.Currency} per {offer.Interval}";

    private static string CapabilityName(string key) => key switch
    {
        "center.nodes" => "Connected Nodes",
        "remote.view" => "Remote view",
        "remote.control" => "Remote control",
        _ => key,
    };

    private static string CapabilityStatus(Availability capability) => capability.Reasons.FirstOrDefault() switch
    {
        "operator_limit" => "Restricted by this Center's operator",
        "plan_configuration_missing" or "plan_configuration_invalid" => "Hosted service is unconfigured",
        "subscription_expired" => "Subscription period ended",
        "payment_grace" => capability.Allowed ? "Available during payment grace" : "Unavailable during payment grace",
        "account_suspended" => "Account suspended",
        "entitlement_unavailable" => "Service access could not be checked",
        _ => !capability.HasAllowed ? "Unavailable" : capability.Allowed ? "Available" : "Unavailable for this account",
    };

    private static string QuotaValue(bool available, ulong value) => available ? value.ToString("N0", CultureInfo.CurrentCulture) : "Unavailable";

    private static string ConfirmationTitle(Confirmation confirmation) => confirmation.Action switch
    {
        BillingAction.StartPurchase => "Confirm hosted offer",
        BillingAction.ResumePurchase => "Resume original purchase",
        BillingAction.PrepareCheckout => "Prepare hosted checkout",
        BillingAction.ResumeCheckout => "Resume original checkout",
        BillingAction.CancelRenewal => "Cancel renewal at period end",
        BillingAction.Reactivate => "Reactivate renewal",
        BillingAction.ResumeRenewal => confirmation.Renewal?.AutoRenew == true ? "Resume original reactivation" : "Resume original cancellation",
        _ => throw new ArgumentOutOfRangeException(nameof(confirmation)),
    };

    private enum BillingAction { StartPurchase, ResumePurchase, PrepareCheckout, ResumeCheckout, CancelRenewal, Reactivate, ResumeRenewal }

    private sealed record Confirmation(BillingAction Action, Offer? Offer = null, SubscriptionPurchase? Purchase = null, SubscriptionRenewal? Renewal = null);
}
