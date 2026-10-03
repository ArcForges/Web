# PRF.08 production profile and generated SDK proof

This is the record of what the PRF.08 implementation builds and what was and was not observed (Design WP-06.05, PG-23 foundation contribution only). It claims no deployed result: no Cloudflare resource, Worker route, Container, D1, session issuer or deployed ingress was used or exists for this task.

## What was built

`apps/app` is a workspace with two minimal production React profiles, `account` and `chat`, built with the pinned Node/npm toolchain from the one root lock (`npm run build:profiles`). Each build serves exactly one profile.

| Profile   | Behaviour                                                                                                                                                                                   | Wire (all from the published SDK)                                                                                          |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `account` | Reads the session once when opened; shows display name, locale, time zone, workspace count, expiry times and the exact recovery generation; signs out with the CSRF token of its bootstrap. | `GET /session/v1/bootstrap` and `POST /session/v1/logout`, route catalogue `browserSessionRoutes`, strict generated codecs |
| `chat`    | One anonymous greeting per click; the user can cancel a pending request; typed refusals; fixed failure texts.                                                                               | Binary gRPC-Web `HelloService/SayHello` through `createHelloClient`, same-origin `/api`                                    |

Typed failures (`cancelled`, `timeout`, `unavailable`, `unauthenticated`, `forbidden`, `rejected`, `limit`, `malformed`, `unexpected`) are decided from the HTTP status or gRPC status and, for protocol errors, from the client library's own error text; server messages are never shown. A logout receipt effect other than `happened` is a failure, never a signed-out state. A 401 on logout is shown as an ended session, not a fault. Error bodies of the session routes are not modelled by the contract and are never parsed.

64-bit values stay exact: `recoveryGeneration` is validated by the generated `parseUInt64`, held as a bigint and displayed as the same canonical text. A separate test drives the generated `EventService` client through the public gRPC-Web transport with int64 and uint64 values above 2^53 and checks they decode as exact bigints (a fixture: the Cloud probe does not serve that service).

## SDK pin and admission

The Contracts pin moved from `1.0.0-ci.129.1` to `1.0.0-ci.287.1` for both `@arcforges/api-client` and `@arcforges/proto`. That is the coordinate the Cloud PRF.07 probe pins, and the first one that publishes the BrowserSession route catalogue and codecs (CON.07). The dependency closure of the two packages is unchanged apart from their own versions. Wrangler moved from 4.135.0 to 4.143.1 because the locked `undici 7.29.0` (through `miniflare`) made `npm audit --audit-level=high` fail on the unchanged `main` lock, which would block every Web pull request; 4.143.1 clears it in a scratch install and in the committed lock (`npm audit --audit-level=high` reports 0 vulnerabilities). The `apps/app` manifest is a new workspace of the same closure plus `@connectrpc/connect` 2.2.0, already in the lock through the SDK.

Admission records: a successor dependency receipt, browser profile and record `browser-resources-r8`, build-identity record `cloud-build-identity-r4` and a refreshed naming-candidate pin. Moving the pin did not change the emitted bytes of the site: a scratch build of `apps/site` with the new SDK produced chunk files with identical names (which carry content hashes) and sizes to the baseline; only the parsed module graph grew from 254 to 298 modules (Vite itself reports 299 transformed, counting one virtual module). The published build identity of 287.1 names a multi-file `publicapi` subject through `sources`; `tooling/build-identity.ts` now accepts that shape and still requires every producer schema source to be named exactly once with its digest.

## Measurements (local, pinned Node 24.21.0 and npm 11.19.0, Windows 11)

Both profiles build deterministically: each was built twice from scratch (`--twice`) with identical content digests.

| Profile | Initial requests | Initial JS (raw / gzip) | Initial CSS (raw / gzip) | HTML bytes |
| ------- | ---------------- | ----------------------- | ------------------------ | ---------- |
| account | 8                | 369,204 / 116,363 B     | 9,714 / 3,050 B          | 2,967      |
| chat    | 8                | 467,524 / 144,698 B     | 9,714 / 3,050 B          | 3,271      |

`apps/app/budgets.json` records these values as the baseline. The gate (`checkBudgets`) fails a build when any metric grows more than 10 percent over its baseline, the regression tier of Design TH-01. Design defines no absolute ceiling for these assets, so none is invented here; changing a baseline is a reviewed change. Most of the weight is React and React Router, shared by both profiles. The structural gates also refuse source maps, private files, non-same-origin references, a profile that lost its own wire marker and a profile that contains the other's. The derived Content-Security-Policy keeps `connect-src 'self'` and no unsafe inline script or style.

## Local opt-in browser run (not CI)

`AOT_HOST_EXE=<host> npm run probe:local --workspace @arcforges/web-app` serves the built profiles from a local same-origin server that forwards `/api/*` to the host with the prefix removed, as the deployed Worker route is documented to do, and drives Chromium (Playwright 1.63, existing local install). It ran once, after the final build.

- Host: a local Windows x64 Native AOT build of the Cloud host found in the retained Cloud PRF.07 worktree (`artifacts/aot-win/ArcForges.Cloud.exe`, 17,738,240 bytes). Its own `/healthz` reported `nativeAot: true`, `kind: local`, `dirty: true` and source commit `08c0c638fdb0`. That is a local development build, not the merged Cloud `main` and not a CI candidate; only the Hello endpoint was exercised.
- Real (Chromium, built Chat profile, real host): the page sent nothing before the first click; `Hello, ArcForges!` and a Unicode name round-tripped (56 and 68 ms from click to rendered result); an empty name produced the host's own InvalidArgument refusal, shown as `rejected`; a 257-code-unit name produced its ResourceExhausted refusal, shown as `limit`. No console error or CSP violation was reported in the Chat run. The Account run ignores the browser's `Failed to load resource` console lines, which its 401 and 503 fixture stages provoke by design, and reported no other console error or CSP violation.
- Harness-labelled (not the host): cancellation of a request the harness held back, and every Account scenario (authenticated presentation with the exact uint64, sign out with the CSRF token seen by the harness, session expiry on logout, unavailable, malformed). A real session needs the Worker, D1 and an issued session, none of which exist here.

Interaction times are single local measurements against a host on the same machine. They are information, not a budget.

## Offline validation

Unit tests (`tests/unit/app-*.test.ts*`) cover the session and Hello clients (request shape, status and trailer mapping, malformed frames, bounded bodies, cancellation, the ten second deadline, exact values), the two route components, and the build gates (budgets at their exact boundary, structural gates, profile isolation). They were checked by deliberate mutation: of 48 mutants of the application and gate code, 45 were caught at the first run; three survived. Two (the receipt body bound and the stream bound) were caught after the tests were strengthened to count the bytes read. The third removes the explicit `uint64` guard on `recoveryGeneration`, which the generated codec already enforces before the guard runs, so it is an equivalent mutant; the guard remains as defence in depth. The independent review's own sample then found four more surviving mutants (the literal CSRF header name, the off-by-one of the stream bound, the HTML-bytes baseline and the Chat abort on unmount); tests were added and each of those four mutants is now caught. Two known survivors remain untested: the equivalent `uint64` guard above and the stale-result guard in the Account route (`controller.signal.aborted ? undefined : result`, which only matters when a request resolves after it was aborted). `scripts/profiles.ts` and `scripts/local-run.ts` have no unit tests: they were exercised by the builds and the local run above.

## Not claimed

- No deployed ingress and no live Cloudflare result of any kind. PRF.07 is `delivered` without a live proof, so the deployed same-origin probe that WP-06.05 names does not exist. Nothing here calls Cloudflare.
- No real session, cookie or CSRF round trip against the Cloud host: the Account profile has only fixture evidence. The cookie is HttpOnly and never read by the page.
- No browser E2E in CI, no Firefox or WebKit run of these profiles, no accessibility run and no visual review.
- No Linux or macOS runtime result for the local run; the host was a Windows build.
- The `ArcForges.Web.esproj` and `win.slnx` entry points still build only the site candidate (`npm run build`). The profile builds are portable npm entry points; the Visual Studio project was not extended to build them.
- The architecture policy has no explicit HTTP-exception admission. The session routes are consumed through the generated catalogue and codecs and an injected fetcher, in the same way as the existing Hello page, which keeps the policy's direct-fetch and JSON rules satisfied without widening them; an explicit admission remains future policy work (see `eng/policy/architecture.md`).
- Product behaviour: identity ceremonies, step-up, real chat, offline behaviour, accessibility and the product budgets belong to the WEB tasks. These profiles are not linked from the site and carry no sealed provenance profile, because they are not deployed.

## What the live part needs from the user

Unchanged from the PRF.07 record: a Cloudflare account id and API token with edit rights for Workers Scripts, Containers, D1, R2 and Queues, the proof resources and secrets, Docker, the pinned toolchain and the exclusive lease `RES-cloud-deployment` held only for the live phase. With a deployed ingress (`env.proof` or a successor) that serves `/api` and `/session/v1`, the Account and Chat profiles are then served from the same origin and exercised against it, and the evidence recorded in a reviewed amendment of the PRF.08 ledger record. Until then the task stays `delivered`.
