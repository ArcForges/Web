# Cloud Hello preparation

Historical implementation plan. Its former CI/runtime acceptance requirements are superseded by [the current validation policy](validation-policy.md). Retained results are historical, not instructions to repeat them.

## Boundary and findings

The owner selected a future C# Native AOT service in Cloudflare Containers. The custom-domain site is already deployed and its real three-browser suite passes. The local ArcForges directory has no Cloud checkout and no Cloud service is available for integration yet.

Web currently uses published Contracts `1.0.0-ci.25.1`, whose `HelloService.SayHello` is an explicitly non-product packaging example. The existing greeting is entirely local. This increment prepares a separate server connection example without claiming to implement the Cloud service, authentication, billing or AI.

The existing live delivery gate also expects Web's HTML 404 at `/api/*`. That assertion would conflict with the future Cloud route. Keep API fallback checks in local candidate tests; live Web verification must check only Web-owned paths. Cloud owns the public API's response assertions.

## Decisions and implementation order

1. Keep static Web delivery on `arcforges.com`. Add `/cloud-hello/` as a prerendered connection page. It sends a fixed `ArcForges` greeting only after an explicit click; the name entered in the existing local example is never sent.
2. Use the existing published `@arcforges/api-client` in binary gRPC-Web mode, with same-origin base `/api`. The exact request path is `/api/arcforges.hello.v1.HelloService/SayHello`. No REST replacement, new schema, registry package or browser secret is needed.
3. Use a ten-second deadline, cancel on navigation, suppress concurrent clicks and perform no automatic retries. Check for the contract's exact expected reply. Show unavailable/invalid responses as failure, never a locally generated successful server response.
4. The future Cloud repository will own a Worker route `arcforges.com/api/*`, forwarding requests to its C# container after removing `/api`. This route can precede the existing Web Custom Domain. Web needs neither an API proxy Worker nor a deployment-time binding to a service that does not exist yet.
5. Add browser tests with explicitly labelled gRPC-Web fixtures for request framing, success, unavailable service and recovery. Keep the local API 404 check alongside those local-only tests. Live Web tests check the idle page and Web-owned static paths without asserting Cloud's responses or simulating a backend. Record the separate gate that the future Cloud deployment must satisfy.

## Closure and evidence

Run source checks, candidate build/verification and the three-browser candidate suite; inspect the new page. PR CI must pass without credentials or access to a live C# service. Real Web delivery is distinct from the fixture tests. The actual Native AOT container, public API route and C#/protobuf transport require Cloud's later real integration test; they are not accepted by this Web PR.

Post-implementation review corrected the live API-path ownership assertion and documented the static Worker's actual GET 404 / POST 405 behavior. Local source, candidate and all eighteen browser checks now pass; the live suite excludes the fixture cases. See [validation evidence](validation.md). No Cloud infrastructure or formal Design document was changed.

Reference: [Cloudflare Custom Domains and routes](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/#interaction-with-routes).
