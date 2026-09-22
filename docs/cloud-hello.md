# Connecting the Cloud service

## Current Web behavior

`/hello/` remains local. `/cloud-hello/` uses the published Contracts client to send one fixed diagnostic greeting after the user clicks **Check connection**. It sends no user-entered name, account cookie, authorization header or Cloudflare management token. Requests have a ten-second deadline, are cancelled on navigation, and are never automatically retried. Only the expected server response is displayed as success.

The Cloud repository deploys the API Worker and Native AOT container independently of Web. A public browser check on 2026-09-18 verified that this page sends no API request on load, then sends exactly one POST after the click and displays `Hello, ArcForges!` from the real service. That observation used Web commit `86d6fded796d060eb064125baca7db690bc65419` and Cloud commit `6554400c04817491fe68d5e6319434034c5dc356`; it is a diagnostic integration result, not acceptance of later product APIs. If the Cloud API route is absent, the static Worker rejects the Hello POST with 405 Method Not Allowed; a GET at that missing path returns 404. The page shows an unavailable response. No mock is deployed and no successful response is generated locally.

## Fixed integration boundary

| Item                                  | Value                                                                                                                                                          |
| ------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Browser SDK                           | `@arcforges/api-client` and `@arcforges/proto`, both `1.0.0-ci.44.1`                                                                                           |
| Browser base URL                      | Same origin, `/api`                                                                                                                                            |
| Public method                         | `POST https://arcforges.com/api/arcforges.hello.v1.HelloService/SayHello`                                                                                      |
| Container method after prefix removal | `/arcforges.hello.v1.HelloService/SayHello`                                                                                                                    |
| Transport                             | Unary binary gRPC-Web, `application/grpc-web+proto`                                                                                                            |
| Request                               | Published `SayHelloRequest`, `name = "ArcForges"`                                                                                                              |
| Expected reply                        | Published `SayHelloResponse`, `message = "Hello, ArcForges!"`                                                                                                  |
| Authority                             | [Contracts Hello schema](https://github.com/ArcForges/Contracts/blob/main/public/proto/arcforges/hello/v1/hello.proto), a packaging example, not a product API |

The Web origin stays `https://arcforges.com`; CSP keeps `connect-src 'self'` and no cross-origin exception is needed. The Web Worker redirects www navigation to the HTTPS apex with HTTP 308, preserving path and query before serving HTML. This prevents a www page from calling an API path owned only on the apex. The existing www Web route and proxied DNS record remain in place; Cloud requires no additional www API route.

The browser client deliberately uses `redirect: "error"`. Reload a www page opened before the redirect deployment so navigation establishes the apex origin; the existing page's POST will not silently follow an API redirect. A later account/authenticated API must define its own session behavior; this anonymous diagnostic must not silently start forwarding credentials.

## Responsibilities owned by the Cloud repository

1. Build the actual C# Native AOT Linux container implementing this published wire contract. Cloudflare currently requires a `linux/amd64` image. CI compiles and packages it with static and offline checks; startup, gRPC-Web response/trailer framing and failure-status runtime tests remain explicit local diagnostics under the validation policy. The existing Contracts HelloHost example alone is not AOT evidence.
2. Deploy its Cloud Worker and Container binding in the same Cloudflare account. Forward to the container through that binding, removing only the leading `/api` from the public path. Preserve request/response protobuf bytes and gRPC-Web content type, statuses and framed trailers. Do not convert the payload to ad-hoc JSON or forward back to the public API URL.
3. Attach **Worker route** `arcforges.com/api/*` to the Cloud Worker. Keep the apex **Custom Domain** attached to `arcforges-web`. A route runs ahead of that custom-domain origin. The API route needs no second DNS hostname, browser token, Web service binding or Web rebuild. Unknown API methods must return an API error/404 rather than Web HTML.
4. Keep the example explicitly bounded: only the Hello diagnostic, with input/resource limits and no model call, paid user operation or database mutation. Authentication, quotas and commercial APIs remain a separate product increment. Cloud owns the Worker/Container account permissions and required plan availability.
5. Promote the sealed Cloud candidate through its required deployment job. Do not run live RPC, browser or installed-consumer tests in CI. Local integration diagnostics may exercise an affected behavior once when supported by the existing environment. A container health endpoint, mocked fixture or successful Web deployment alone does not establish a real browser-to-Cloud round trip.

Cloud owns provisioning the Worker, container image, API route, billing plan and deployment credentials.

## Evidence and recovery

`cloud-hello-fixture.spec.ts` intercepts the browser API request with explicitly labelled protobuf wire fixtures. It verifies the actual published client's request, unavailable response and recovery. These browser fixtures are optional local diagnostics, excluded from live Web verification so mocked success cannot be reported as a real C# integration. No browser or live Cloud test runs in CI.

Web's candidate gate checks its assets, private redirect module and delivery configuration. Offline redirect tests distinguish canonical navigation from asset fallback; optional local candidate diagnostics can verify that Web cannot fake a successful API response. Cloud owns API/container deployment. Removing Cloud's apex API route restores Web's rejection of that API request; the connection page reports failure instead of silently falling back to a local greeting. No Web check requires its HTML at production `/api/*`.

References: [routes before a Custom Domain](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/#interaction-with-routes), [Cloudflare Containers setup](https://developers.cloudflare.com/containers/get-started/).
