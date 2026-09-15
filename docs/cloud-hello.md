# Connecting the future Cloud service

## Current Web behavior

`/hello/` remains local. `/cloud-hello/` uses the published Contracts client to send one fixed diagnostic greeting after the user clicks **Check connection**. It sends no user-entered name, account cookie, authorization header or Cloudflare management token. Requests have a ten-second deadline, are cancelled on navigation, and are never automatically retried. Only the expected server response is displayed as success.

No Cloud service is currently deployed by this repository. Until its API route exists, the static Worker rejects the Hello POST with 405 Method Not Allowed; a GET at that missing path returns 404. The page shows an unavailable response. No mock is deployed and no successful response is generated locally.

## Fixed integration boundary

| Item                                  | Value                                                                                                                                                          |
| ------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Browser SDK                           | `@arcforges/api-client` and `@arcforges/proto`, both `1.0.0-ci.25.1`                                                                                           |
| Browser base URL                      | Same origin, `/api`                                                                                                                                            |
| Public method                         | `POST https://arcforges.com/api/arcforges.hello.v1.HelloService/SayHello`                                                                                      |
| Container method after prefix removal | `/arcforges.hello.v1.HelloService/SayHello`                                                                                                                    |
| Transport                             | Unary binary gRPC-Web, `application/grpc-web+proto`                                                                                                            |
| Request                               | Published `SayHelloRequest`, `name = "ArcForges"`                                                                                                              |
| Expected reply                        | Published `SayHelloResponse`, `message = "Hello, ArcForges!"`                                                                                                  |
| Authority                             | [Contracts Hello schema](https://github.com/ArcForges/Contracts/blob/main/public/proto/arcforges/hello/v1/hello.proto), a packaging example, not a product API |

The Web origin stays `https://arcforges.com`; CSP keeps `connect-src 'self'` and no cross-origin exception is needed. A later account/authenticated API must define its own session behavior; this anonymous diagnostic must not silently start forwarding credentials.

## Work owned by the future Cloud repository

1. Build and test the actual C# Native AOT Linux container implementing this published wire contract. Cloudflare currently requires a `linux/amd64` image. Validate its AOT build, startup, gRPC-Web response/trailer framing, and failure statuses; the existing Contracts HelloHost example alone is not AOT evidence.
2. Deploy its Cloud Worker and Container binding in the same Cloudflare account. Forward to the container through that binding, removing only the leading `/api` from the public path. Preserve request/response protobuf bytes and gRPC-Web content type, statuses and framed trailers. Do not convert the payload to ad-hoc JSON or forward back to the public API URL.
3. Attach **Worker route** `arcforges.com/api/*` to the Cloud Worker. Keep the apex **Custom Domain** attached to `arcforges-web`. A route runs ahead of that custom-domain origin. The API route needs no second DNS hostname, browser token, Web service binding or Web rebuild. Unknown API methods must return an API error/404 rather than Web HTML.
4. Keep the example explicitly bounded: only the Hello diagnostic, with input/resource limits and no model call, paid user operation or database mutation. Authentication, quotas and commercial APIs remain a separate product increment. Worker/Container account permissions and plan availability are configured when Cloud is implemented.
5. Cloud's deployment gate must invoke the **public same-origin method using the published client**, verify the expected protobuf reply, and verify both success and failure behavior. Then check the button in the deployed Web page. A container health endpoint, mocked fixture or Web deployment alone does not establish this chain.

This repository does not provision the missing Cloud Worker, container image, API route, billing plan or credentials.

## Evidence and recovery

`cloud-hello-fixture.spec.ts` intercepts the browser API request with explicitly labelled protobuf wire fixtures. It verifies the actual published client's request, unavailable response and recovery. It is excluded from live Web verification so mocked success cannot be reported as a real C# integration. Live Web tests check that the new page loads and sends nothing automatically.

Web delivery still verifies its assets and Web-owned public 404 behavior. Candidate-only tests verify that the static Worker cannot fake a successful API response; the live Web gate does not require Web HTML at `/api/*`, because Cloud will own those paths. Cloud owns the later API/container deployment and its independent real integration gate. Removing Cloud's API route restores the static Worker's rejection of that API request; the connection page reports failure instead of silently falling back to a local greeting.

References: [routes before a Custom Domain](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/#interaction-with-routes), [Cloudflare Containers setup](https://developers.cloudflare.com/containers/get-started/).
