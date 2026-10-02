# Account and Chat production profiles (PRF.08 proof)

This workspace holds the two minimal production React profiles of the future Account/Chat application. They exist to prove, offline and once locally, that a production build from the Web root lock can use the exact published generated SDK. They are not product surfaces and are not linked from the public site. See [the proof record](../../docs/prf-08-profile-proof.md) for what was and was not observed.

| Profile   | Page                                                                              | Wire                                                                                          |
| --------- | --------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `account` | Reads the session once on open, shows it, signs out with the bootstrap CSRF token | Same-origin cookie `GET /session/v1/bootstrap` and `POST /session/v1/logout` (HTTP exception) |
| `chat`    | One anonymous greeting per click, cancellable, typed refusals                     | Binary gRPC-Web `HelloService/SayHello` to same-origin `/api`                                 |

A build serves one profile: `ARCFORGES_PROFILE` selects the route module before bundling, so one profile's wire code never reaches the other's assets.

```sh
npm run build:profiles                                                  # build, gate and measure both profiles
npm run build --workspace @arcforges/web-app -- --twice                 # also require byte-identical rebuilds
npm run build --workspace @arcforges/web-app -- --write-baseline        # reviewed change only
AOT_HOST_EXE=<Cloud Native AOT host> npm run probe:local --workspace @arcforges/web-app   # local opt-in, never CI
```

Rules this code keeps:

- Wire shapes, route paths and bodies come from the published SDK: the generated `browserSessionRoutes` catalogue and strict JSON codecs for the session records, the generated Hello client for gRPC-Web. No handwritten DTO, no `JSON.parse` or `JSON.stringify`, and the calls go through an injected fetcher, not a global `fetch` call. Error bodies of the session routes are not modelled by the contract, so they are never parsed: the HTTP status alone decides the failure kind.
- 64-bit values (`recoveryGeneration`) stay canonical decimal text, validated by the generated `parseUInt64`, and are displayed as the same text.
- The CSRF token and everything else about the session live only in memory. Nothing is written to storage, and the page never sees the cookie.
- Server text is never echoed into a failure message; each failure kind has fixed text.
- The profile builds are measured and gated against `budgets.json`, but are not sealed or deployed. They carry no browser-graph observer or provenance profile; the deployable candidate remains `apps/site`.

Browser sessions, business APIs and authentication stay in the C# Cloud host. Do not introduce a Node business server, share parent-domain cookies, or use route placement as an authorization boundary.
