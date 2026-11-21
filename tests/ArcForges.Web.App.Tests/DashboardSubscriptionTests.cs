// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.PublicApi.V1;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Pages;
using ArcForges.Web.App.Tests.TestDoubles;
using Bunit;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardSubscriptionTests
{
    private const string Entitlement = "/arcforges.publicapi.v1.EntitlementService/GetSnapshot";
    private const string SubscriptionRead = "/arcforges.publicapi.v1.CommerceService/GetSubscription";
    private const string BillingHistory = "/arcforges.publicapi.v1.CommerceService/ListBillingHistory";
    private const string Catalogue = "/arcforges.publicapi.v1.CommerceService/GetCatalogue";
    private const string Cancel = "/arcforges.publicapi.v1.CommerceService/CancelSubscription";
    private const string Purchase = "/arcforges.publicapi.v1.CommerceService/CreatePurchaseIntent";
    private const string PurchaseRead = "/arcforges.publicapi.v1.CommerceService/GetPurchaseState";
    private const string Checkout = "/arcforges.publicapi.v1.CommerceService/CreateCheckoutAttempt";
    private static readonly Guid SubscriptionId = Guid.Parse("786d16f1-9dfa-4988-8939-8dcb88c24bab");

    [Fact]
    public void SelfHostedShowsTheReportedOperatorQuotaWithoutCallingCommerce()
    {
        using var server = DashboardLifecycleFixture.Server((captured, _) => Task.FromResult(EntitlementReply(captured, versioned: false)));
        using var context = DashboardLifecycleFixture.Context(server, services: [typeof(IDashboardSubscription)]);
        var view = DashboardLifecycleFixture.Render<Subscription>(context);

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("table[aria-label='Reported service quotas']")));
        Assert.Contains("This Center's operator manages service access and quotas", view.Markup, StringComparison.Ordinal);
        Assert.Contains(((ulong)WorkFixture.Revision).ToString("N0", CultureInfo.CurrentCulture),
            view.Find("table[aria-label='Reported service quotas']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Restricted by this Center's operator", view.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Hosted checkout", view.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Billing history", view.Markup, StringComparison.Ordinal);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task DarkSalesDoNotExposeCheckoutOrInventAnOfferForAnEndedSubscription()
    {
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            if (!captured.Url.AbsolutePath.EndsWith(Catalogue, StringComparison.Ordinal))
                return Task.FromResult(OfficialRead(captured, SubscriptionState.Ended));

            var request = WorkFixture.Read(captured, Catalogue, CommerceServiceGetCatalogueRequest.Parser);
            Assert.Equal("GB", request.Region);
            Assert.Null(request.Meta.CommandId);
            return Task.FromResult(WorkFixture.Reply(new CommerceServiceGetCatalogueResponse
            {
                Meta = WorkFixture.Meta(request.Meta),
                Error = new ArcError
                {
                    Category = ErrorCategory.State,
                    Code = "unavailable",
                    Effect = EffectCertainty.DidNotHappen,
                    Details = new() { State = new() { Reason = "sales_closed" } },
                },
            }));
        });
        using var context = DashboardLifecycleFixture.Context(server, "official-hosted", typeof(IDashboardSubscription));
        var view = DashboardLifecycleFixture.Render<Subscription>(context);
        const string form = "section[aria-labelledby='subscription-catalogue'] form";
        view.WaitForAssertion(() => Assert.Contains("Ended", view.Find("section[aria-labelledby='subscription-current']").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Review cancellation", view.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Requests, request => request.Url.AbsolutePath.EndsWith(Catalogue, StringComparison.Ordinal));

        await view.InvokeAsync(() =>
        {
            view.Find(form + " input").Input("gb");
            view.Find(form).Submit();
        });

        view.WaitForAssertion(() => Assert.Contains("Hosted sales are unavailable on this Center", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Review purchase", view.Markup, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("a[target='_blank']"));
        Assert.DoesNotContain(server.Requests, request => request.Url.AbsolutePath.Contains("CreateCheckoutAttempt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckoutRequiresTheExactQuoteAndBothConfirmationsAndRefusesAForeignAttempt(bool foreignAttempt)
    {
        const string offerVersion = "fixture-price-v7";
        const string checkoutUrl = "https://checkout.example.test/start?attempt=fixture";
        var offerId = WorkFixture.Id(WorkFixture.GrantId);
        CommerceServiceCreatePurchaseIntentRequest? purchase = null;
        CommerceServiceCreateCheckoutAttemptRequest? checkout = null;
        int sends = 0;
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            if (captured.Url.AbsolutePath.EndsWith(Catalogue, StringComparison.Ordinal))
            {
                var request = WorkFixture.Read(captured, Catalogue, CommerceServiceGetCatalogueRequest.Parser);
                Assert.Equal("GB", request.Region);
                return Task.FromResult(WorkFixture.Reply(new CommerceServiceGetCatalogueResponse
                {
                    Meta = WorkFixture.Meta(request.Meta),
                    Value = new()
                    {
                        CatalogueVersion = "fixture-catalogue-v9",
                        Items =
                        {
                            new Offer
                            {
                                OfferId = offerId, Kind = "subscription", Region = "GB", Currency = "GBP", Interval = "month",
                                PriceVersion = offerVersion, Amount = new() { Value = "12.37" },
                            },
                        },
                    },
                }));
            }

            if (captured.Url.AbsolutePath.EndsWith(Purchase, StringComparison.Ordinal))
            {
                Assert.Null(purchase);
                purchase = WorkFixture.Read(captured, Purchase, CommerceServiceCreatePurchaseIntentRequest.Parser);
                Assert.Equal(offerId, purchase.OfferId);
                Assert.Equal(offerVersion, purchase.PriceVersion);
                Assert.Equal("GB", purchase.Region);
                Assert.Equal(16, purchase.Meta.CommandId.Value.Length);
                Assert.Equal(16, purchase.PurchaseIntentId.Value.Length);
                return Task.FromResult(WorkFixture.Reply(new CommerceServiceCreatePurchaseIntentResponse
                {
                    Meta = WorkFixture.Meta(purchase.Meta),
                    Value = new() { Purchase = new() { PurchaseIntentId = purchase.PurchaseIntentId, OfferId = offerId, State = "pending" } },
                }));
            }

            if (captured.Url.AbsolutePath.EndsWith(Checkout, StringComparison.Ordinal))
            {
                var request = WorkFixture.Read(captured, Checkout, CommerceServiceCreateCheckoutAttemptRequest.Parser);
                Assert.NotNull(purchase);
                Assert.Equal(purchase.PurchaseIntentId, request.PurchaseIntentId);
                Assert.Equal("/account/subscription", request.ReturnRoute);
                Assert.Equal(16, request.CheckoutAttemptId.Value.Length);
                Assert.Equal(16, request.Meta.CommandId.Value.Length);
                Assert.NotEqual(purchase.Meta.CommandId, request.Meta.CommandId);
                if (checkout is not null)
                {
                    Assert.Equal(checkout.CheckoutAttemptId, request.CheckoutAttemptId);
                    Assert.Equal(checkout.Meta.CommandId, request.Meta.CommandId);
                }
                checkout = request;
                sends++;
                return Task.FromResult(WorkFixture.Reply(new CommerceServiceCreateCheckoutAttemptResponse
                {
                    Meta = WorkFixture.Meta(request.Meta),
                    Value = new()
                    {
                        Checkout = new()
                        {
                            PurchaseIntentId = request.PurchaseIntentId,
                            CheckoutAttemptId = foreignAttempt ? WorkFixture.Id(WorkFixture.NodeId) : request.CheckoutAttemptId,
                            Url = checkoutUrl,
                            ExpiresAt = new() { UnixSeconds = TimeProvider.System.GetUtcNow().AddMinutes(15).ToUnixTimeSeconds() },
                        },
                    },
                }));
            }

            if (captured.Url.AbsolutePath.EndsWith(PurchaseRead, StringComparison.Ordinal))
            {
                var request = WorkFixture.Read(captured, PurchaseRead, CommerceServiceGetPurchaseStateRequest.Parser);
                Assert.NotNull(purchase);
                Assert.Equal(purchase.PurchaseIntentId, request.PurchaseIntentId);
                Assert.Null(request.Meta.CommandId);
                return Task.FromResult(WorkFixture.Reply(new CommerceServiceGetPurchaseStateResponse
                {
                    Meta = WorkFixture.Meta(request.Meta),
                    Value = new() { Purchase = new() { PurchaseIntentId = request.PurchaseIntentId, OfferId = offerId, State = "pending" } },
                }));
            }

            return Task.FromResult(OfficialRead(captured, SubscriptionState.Ended));
        });
        using var context = DashboardLifecycleFixture.Context(server, "official-hosted", typeof(IDashboardSubscription));
        var view = DashboardLifecycleFixture.Render<Subscription>(context);
        const string form = "section[aria-labelledby='subscription-catalogue'] form";
        view.WaitForAssertion(() => Assert.Contains("Ended", view.Markup, StringComparison.Ordinal));
        await view.InvokeAsync(() =>
        {
            view.Find(form + " input").Input("GB");
            view.Find(form).Submit();
        });

        view.WaitForAssertion(() => Assert.Contains("12.37 GBP per month", view.Markup, StringComparison.Ordinal));
        await DashboardLifecycleFixture.ClickAsync(view, "Review purchase");
        Assert.Null(purchase);
        await DashboardLifecycleFixture.ClickAsync(view, "Confirm");
        view.WaitForAssertion(() => Assert.Contains("Purchase prepared", view.Markup, StringComparison.Ordinal));
        Assert.Null(checkout);
        Assert.Empty(view.FindAll($"a[href='{checkoutUrl}']"));
        await DashboardLifecycleFixture.ClickAsync(view, "Review checkout");
        Assert.Null(checkout);
        await DashboardLifecycleFixture.ClickAsync(view, "Confirm");

        if (foreignAttempt)
        {
            view.WaitForAssertion(() => Assert.Contains("Checkout result unconfirmed", view.Markup, StringComparison.Ordinal));
            Assert.Empty(view.FindAll($"a[href='{checkoutUrl}']"));
            await DashboardLifecycleFixture.ClickAsync(view, "Check purchase status");
            Assert.Equal(1, sends);
            await DashboardLifecycleFixture.ClickAsync(view, "Review original checkout");
            Assert.Equal(1, sends);
            await DashboardLifecycleFixture.ClickAsync(view, "Confirm");
            Assert.Empty(view.FindAll($"a[href='{checkoutUrl}']"));
            Assert.Equal(2, sends);
        }
        else
        {
            view.WaitForAssertion(() => Assert.Contains("Payment confirmation pending", view.Markup, StringComparison.Ordinal));
            var link = view.Find($"a[href='{checkoutUrl}']");
            Assert.Equal("_blank", link.GetAttribute("target"));
            Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
            Assert.Equal(1, sends);
        }
        Assert.Single(server.Requests, request => request.Url.AbsolutePath.EndsWith(Purchase, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LostRenewalReplyRequiresReadBackAndExplicitConfirmationBeforeReusingTheOriginalCommand()
    {
        CommerceServiceCancelSubscriptionRequest? original = null;
        int sends = 0;
        using var server = DashboardLifecycleFixture.Server((captured, _) =>
        {
            if (!captured.Url.AbsolutePath.EndsWith(Cancel, StringComparison.Ordinal))
                return Task.FromResult(OfficialRead(captured, SubscriptionState.Active));

            var request = WorkFixture.Read(captured, Cancel, CommerceServiceCancelSubscriptionRequest.Parser);
            Assert.Equal(WorkFixture.Id(SubscriptionId), request.SubscriptionId);
            Assert.Equal(WorkFixture.Revision, request.Meta.ExpectedRev.Value);
            Assert.Equal(16, request.Meta.CommandId.Value.Length);
            sends++;
            if (original is null)
            {
                original = request;
                throw new HttpRequestException("Fixture lost the response for this renewal command.");
            }

            Assert.Equal(original.Meta.CommandId, request.Meta.CommandId);
            Assert.Equal(original.Meta.ExpectedRev, request.Meta.ExpectedRev);
            return Task.FromResult(WorkFixture.Reply(new CommerceServiceCancelSubscriptionResponse
            {
                Meta = WorkFixture.Meta(request.Meta),
                Error = new ArcError { Category = ErrorCategory.Conflict, Code = "revision_conflict", Effect = EffectCertainty.DidNotHappen },
            }));
        });
        using var context = DashboardLifecycleFixture.Context(server, "official-hosted", typeof(IDashboardSubscription));
        var view = DashboardLifecycleFixture.Render<Subscription>(context);
        view.WaitForAssertion(() => Assert.Contains("Review cancellation", view.Markup, StringComparison.Ordinal));

        await DashboardLifecycleFixture.ClickAsync(view, "Review cancellation");
        Assert.Equal(0, sends);
        await DashboardLifecycleFixture.ClickAsync(view, "Confirm");
        view.WaitForAssertion(() => Assert.Contains("The original renewal result is unconfirmed", view.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Review cancellation retry", view.Markup, StringComparison.Ordinal);
        Assert.NotNull(original);
        Assert.Contains(new Guid(original.Meta.CommandId.Value.Span, bigEndian: true).ToString("D"), view.Markup, StringComparison.Ordinal);

        await DashboardLifecycleFixture.ClickAsync(view, "Check current subscription");
        view.WaitForAssertion(() => Assert.Contains("Review cancellation retry", view.Markup, StringComparison.Ordinal));
        await DashboardLifecycleFixture.ClickAsync(view, "Review cancellation retry");
        Assert.Equal(1, sends);
        await DashboardLifecycleFixture.ClickAsync(view, "Resubmit original request");

        view.WaitForAssertion(() => Assert.Contains("The original renewal result is unconfirmed", view.Markup, StringComparison.Ordinal));
        Assert.Equal(2, sends);
        Assert.DoesNotContain("The Center confirmed the renewal change", view.Markup, StringComparison.Ordinal);
    }

    private static HttpResponseMessage OfficialRead(CapturedRequest captured, SubscriptionState state)
    {
        if (captured.Url.AbsolutePath.EndsWith(Entitlement, StringComparison.Ordinal))
            return EntitlementReply(captured, entitled: state != SubscriptionState.Ended);

        if (captured.Url.AbsolutePath.EndsWith(BillingHistory, StringComparison.Ordinal))
        {
            var bill = WorkFixture.Read(captured, BillingHistory, CommerceServiceListBillingHistoryRequest.Parser);
            Assert.True(bill.Period.From.UnixSeconds < bill.Period.Until.UnixSeconds);
            return WorkFixture.Reply(new CommerceServiceListBillingHistoryResponse
            {
                Meta = WorkFixture.Meta(bill.Meta),
                Value = new() { Page = new() },
            });
        }

        var request = WorkFixture.Read(captured, SubscriptionRead, CommerceServiceGetSubscriptionRequest.Parser);
        var now = TimeProvider.System.GetUtcNow();
        return WorkFixture.Reply(new CommerceServiceGetSubscriptionResponse
        {
            Meta = WorkFixture.Meta(request.Meta, WorkFixture.Revision),
            Value = new()
            {
                Subscription = new()
                {
                    SubscriptionId = WorkFixture.Id(SubscriptionId),
                    State = state,
                    Revision = new() { Value = WorkFixture.Revision },
                    CancelAtPeriodEnd = false,
                    CurrentPeriod = new()
                    {
                        From = new() { UnixSeconds = now.AddDays(-20).ToUnixTimeSeconds() },
                        Until = new() { UnixSeconds = now.AddDays(state == SubscriptionState.Ended ? -1 : 10).ToUnixTimeSeconds() },
                    },
                },
            },
        });
    }

    private static HttpResponseMessage EntitlementReply(CapturedRequest captured, bool versioned = true, bool entitled = true)
    {
        var request = WorkFixture.Read(captured, Entitlement, EntitlementServiceGetSnapshotRequest.Parser);
        var snapshot = new EntitlementSnapshot { Quotas = { new QuotaUsage { Kind = "operator-fixture", Limit = (ulong)WorkFixture.Revision } } };
        if (versioned)
            snapshot.Version = WorkFixture.Revision;
        foreach (var capability in new[] { "center.nodes", "remote.view", "remote.control" })
        {
            var (allowed, reason) = (versioned, capability) switch
            {
                (true, _) => (entitled, entitled ? "entitled" : "subscription_expired"),
                (false, "remote.control") => (false, "operator_limit"),
                _ => (true, "self_hosted_default"),
            };
            snapshot.Capabilities.Add(new Availability { Capability = capability, Allowed = allowed, Reasons = { reason } });
        }
        return WorkFixture.Reply(new EntitlementServiceGetSnapshotResponse
        {
            Meta = WorkFixture.Meta(request.Meta),
            Value = new() { Snapshot = snapshot },
        });
    }
}
