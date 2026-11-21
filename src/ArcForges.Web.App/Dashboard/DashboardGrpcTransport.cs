// SPDX-License-Identifier: AGPL-3.0-only
using System.Net.Http.Headers;
using ArcForges.Web.App.Probe;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace ArcForges.Web.App.Dashboard;

/// <summary>Generated dashboard RPCs over binary gRPC-Web and same-origin browser credentials.</summary>
internal sealed class DashboardGrpcTransport(HttpMessageHandler transport)
{
    public GrpcChannel Open(Uri origin, string csrfToken, int requestBytes, int responseBytes) =>
        GrpcChannel.ForAddress(origin, new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new BrowserHandler(transport, origin, csrfToken)),
            DisposeHttpClient = true,
            ThrowOperationCanceledOnCancellation = true,
            MaxSendMessageSize = requestBytes,
            MaxReceiveMessageSize = responseBytes,
        });

    private sealed class BrowserHandler(HttpMessageHandler inner, Uri origin, string csrfToken) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _transport = new(inner, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var destination = request.RequestUri;
            if (request.Method != HttpMethod.Post || destination is not { IsAbsoluteUri: true } ||
                destination.UserInfo.Length != 0 ||
                !string.Equals(destination.GetLeftPart(UriPartial.Authority), origin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal) ||
                destination.Query.Length != 0 || destination.Fragment.Length != 0)
            {
                throw new DashboardApiException(DashboardFailure.Incompatible);
            }

            var path = destination.AbsolutePath;
            if (path is not (
                "/arcforges.management.v2.DeviceService/List" or
                "/arcforges.management.v2.DeviceService/Inspect" or
                "/arcforges.management.v2.DeviceService/Revoke" or
                "/arcforges.publicapi.v1.IdentityService/GetProfile" or
                "/arcforges.publicapi.v1.IdentityService/UpdateProfile" or
                "/arcforges.publicapi.v1.IdentityService/GetAccountDeletion" or
                "/arcforges.publicapi.v1.IdentityService/ListSessions" or
                "/arcforges.publicapi.v1.IdentityService/ListAuthIdentities" or
                "/arcforges.publicapi.v1.IdentityService/RevokeSession" or
                "/arcforges.publicapi.v1.EntitlementService/GetSnapshot" or
                "/arcforges.publicapi.v1.CommerceService/GetCatalogue" or
                "/arcforges.publicapi.v1.CommerceService/CreatePurchaseIntent" or
                "/arcforges.publicapi.v1.CommerceService/CreateCheckoutAttempt" or
                "/arcforges.publicapi.v1.CommerceService/GetPurchaseState" or
                "/arcforges.publicapi.v1.CommerceService/GetSubscription" or
                "/arcforges.publicapi.v1.CommerceService/CancelSubscription" or
                "/arcforges.publicapi.v1.CommerceService/ReactivateSubscription" or
                "/arcforges.publicapi.v1.CommerceService/ListBillingHistory" or
                "/arcforges.sandbox.v2.NodeService/GetOperation" or
                "/arcforges.sandbox.v2.NodeService/ListRemoteGrants" or
                "/arcforges.sandbox.v2.NodeService/RevokeRemoteGrant" or
                "/arcforges.sandbox.v2.SandboxService/List" or
                "/arcforges.sandbox.v2.SandboxService/Inspect" or
                "/arcforges.sandbox.v2.SandboxService/Create" or
                "/arcforges.sandbox.v2.RunService/List" or
                "/arcforges.sandbox.v2.RunService/Inspect" or
                "/arcforges.sandbox.v2.RunService/Start" or
                "/arcforges.sandbox.v2.RunService/Stop" or
                "/arcforges.sandbox.v2.RunService/WatchOutput" or
                "/arcforges.sandbox.v2.ResourceService/GetBudget" or
                "/arcforges.sandbox.v2.ResourceService/SetPolicy" or
                "/arcforges.sandbox.v2.ResourceService/WatchUsage" or
                "/arcforges.sandbox.v2.DataService/BeginImport" or
                "/arcforges.sandbox.v2.DataService/WriteImport" or
                "/arcforges.sandbox.v2.DataService/CommitImport" or
                "/arcforges.sandbox.v2.DataService/BeginExport" or
                "/arcforges.sandbox.v2.DataService/ReadExport" or
                "/arcforges.sandbox.v2.DataService/InspectTransfer" or
                "/arcforges.sandbox.v2.DataService/CloseTransfer"))
            {
                throw new DashboardApiException(DashboardFailure.Incompatible);
            }

            request.RequestUri = new Uri(origin, "/api" + path);
            request.Headers.Add(SessionProbe.CsrfHeader, csrfToken);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            request.SetBrowserRequestOption("redirect", "error");
            if (path is "/arcforges.sandbox.v2.RunService/WatchOutput" or "/arcforges.sandbox.v2.ResourceService/WatchUsage")
                request.SetBrowserResponseStreamingEnabled(true);
            return _transport.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _transport.Dispose();
            base.Dispose(disposing);
        }
    }
}
