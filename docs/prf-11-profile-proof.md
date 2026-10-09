# PRF.11 Blazor WebAssembly production profile and generated C# SDK proof

This is the record of the PRF.11 proof for the Web repository (Design WP-06.05 full, WP-06 package-level contribution, PG-23 foundation contribution). It supersedes PRF.08 (the React proof, `docs/prf-08-profile-proof.md`), whose evidence is not reused. Entries are added as each unit lands. A claim is recorded only with the evidence that backs it. Anything that needs a deployed ingress is recorded as "blocked on CLOUD.21/CLOUD.22, not proven" or as "blocked on CLOUD.85, not proven", never as deferred and never as proven.

## Start identity (U1)

| Item | Value |
| --- | --- |
| Task | PRF.11, lane runtime-proofs, kind proof, size L |
| Repository and branch | Web, `task/prf-11`, local worktree `.worktree/prf-11` |
| Claim | epoch 1, worker `w-deku-20261009-prf-11` (coordinator-held claim) |
| Pre-assigned reviewer | `w-deku-20261008-rev-prf-11` (brief section 10, S25); written into every record with `decision: approved` and `reviewedOn` at write time |
| Base | `origin/main` c8588681 (merge of PR #35 from `task/web-40`, WEB.40) |
| WEB.40 status | `delivered` in the ledger (`ledger/tasks/web-40.md` on Plan main); merged, deployed and released as `web-0.1.0-ci.111.1` (S28(a)) |
| Plan authority | the PRF.11 entry of `phase2-plans.json` (units U1 to U9, defaults D1 to D12), the task record, and brief sections 0, 0a, 0b, 10 and 11 |

### Start edges

| Start edge | Ledger status | Evidence used by this proof |
| --- | --- | --- |
| CON.92 (serialization posture and decode limits) | `inherited` (WP03.02, accepted source e6c4a77) | The decode limits are part of the Contracts source at the pinned identity. Contracts `docs/architecture.md`, "Serialization posture", at ca45f36 states the registry limits: 4 MiB unary and helper messages, 256 KiB inline pages, 32 KiB stream frames, 64 MiB large read projections, and at most 100 nested message levels, with typed `tooLarge`, `tooDeep` and `malformed` refusals. Source e6c4a77 is an ancestor of ca45f36. The P0c precondition holds. |
| WEB.40 (Blazor profile projects and C# static Site generator) | `delivered` | `src/ArcForges.Web.App`, `src/ArcForges.Web.Site`, `tools/ArcForges.Web.Tooling` at c8588681. |
| PRF.07 (deployed Cloud AOT probe reachable same-origin) | `complete` | Consumed only through the deployed ingress in the live units. No substitute probe is used. |
| CON.07 (BrowserSession and NativeAuth HTTP exceptions and generated C# codecs) | `complete` | The NuGet identity is `ArcForges.Contracts.PublicApi 1.0.0-ci.287.1` from source ca45f36cccbdd31380f76b8f6cdecc958fdcb430. That commit is an ancestor of Contracts `origin/main`. Build run 36966686225 published it to NuGet, npm and Maven. The pin is in `Directory.Packages.props` and is unchanged by U1. |

### Completion edges (blocked, not proven)

| Completion edge | Ledger status at start | Consequence for PRF.11 |
| --- | --- | --- |
| CLOUD.21 (real generated public methods on the deployed gRPC-Web ingress) | not started (no ledger record; waits on CLOUD.13) | Exact int64, uint64 and decimal calls, and the `WorkspaceService.GetHealth` and `UpdateSettings` round trips, are blocked on CLOUD.21, not proven. |
| CLOUD.22 (ArcResult and gRPC-Web status and trailer mapping) | not started (waits on CLOUD.21) | Typed failures, deadline and cancel-after-dispatch are blocked on CLOUD.22, not proven. |
| CLOUD.08 (readiness surfaces) | `complete` | Readiness observation is available for the live units. |
| CLOUD.71 (proof-origin route families for the React bytes) | `complete` (history for the React bytes only) | The `/api`, `/session/v1` and `/proof/v1` route precedence is not proven for the Blazor bytes. D11 treats the graph inconsistency as history (brief S16(a)). |
| CLOUD.85 (proof-origin serving of the Blazor Account and Chat shells and the C# Site, under base href "/", CLOUD.85 D1) | not started (no ledger record; waits on WEB.40 and the Cloud deploy) | Same-origin serving, the `/account/` and `/chat/` shells, digest-verified bytes and the exact CSP on served responses are blocked on CLOUD.85, not proven. PRF.11 cannot complete until CLOUD.85 delivers and its checks pass (S27(b)). |

Completion of PRF.11 is therefore blocked. The offline units U1 to U9 are the delivery in this phase (brief S20(d)).

### Shell and base href

The Account and Chat profile shells are served at `/account/` and `/chat/` under base href `"/"` (CLOUD.85 D1, brief S27(b)). The framework files sit at the root. The host page `src/ArcForges.Web.App/wwwroot/index.html` already carries `<base href="/" />`. The earlier base href wording `/account/` and `/chat/` is superseded.

## Inherited WEB.40 surfaces

These surfaces exist at c8588681 and are extended by PRF.11, not duplicated:

- `src/ArcForges.Web.App`: the Account and Chat Razor pages, the `ProfileLayout`, the `HelloProbe`, `SessionProbe`, `ExactUnsigned` and `BoundedBody` probe classes, and the standalone profile project with `RunAOTCompilation` false.
- `src/ArcForges.Web.Site`: the deterministic static Site generator (`/`, `/hello/` and `/cloud-hello/` as static pages; no runtime JavaScript).
- `tools/ArcForges.Web.Tooling`: the candidate builder and verifier, the Site archive, the profile bundle (`profiles bundle` and `profiles verify`), and the profile budget tool (`profiles budget`).
- `tests/ArcForges.Web.App.Tests`, `tests/ArcForges.Web.Site.Tests`, `tests/ArcForges.Web.Tooling.Tests`, and the local opt-in browser project `tests/browser/ArcForges.Web.Browser.Tests` (Microsoft.Playwright for .NET, never built in CI).
- `eng/policy/profile-budgets.json`: the asset budgets for the `account` and `chat` profiles, with `interactionBudgets` status `re-baseline-pending`, owner PRF.11.
- The Ui project `src/ArcForges.Web.Ui` (including `WasmContentSecurityPolicy`) is outside PRF.11's write scope. It is consumed, not changed.

## Declared write scope

The write scope is the PRF.11 `writes` list in the task record, with the D3 additions (brief S20(b), S27(f)): `eng/policy/profile-budgets.json`, `tools/ArcForges.Web.Tooling/**`, `tests/ArcForges.Web.Tooling.Tests/**` and `tests/ArcForges.Web.Site.Tests/**`. `src/ArcForges.Web.Ui/**`, its tests and `.github/workflows/ci.yml` are not in scope. An in-browser CSP or framing change that needs a Ui edit stops the task for a new decision.

## Gaps in the plan recorded at start

- The published output of the App publish is produced by CI after the test step, so tests that read emitted files need a local publish first or are covered through the bundle tooling tests. This is recorded in the U2 section.
- Node is 24.20 locally against the 24.21 pin, so `npm run policy` is recorded as not run locally (hosted CI is authoritative). This is an environment gap and not a fix.
- The WSL2 Debian SDK is 10.0.400, so a locked Linux restore fails with NU1004 there. Linux checks are not run in this phase unless they are affected.

## Local CI conditions used for the gates

The gates run the workflow's commands with `GITHUB_ACTIONS=true`, `CI=true` and `NUGET_PACKAGES` set to an empty folder under the session scratchpad, so every locked restore downloads from the locked feeds. The ArcForges.Build.Policy analyzer (AFP006) requires a complete CI identity under `GITHUB_ACTIONS=true`, so the gate sets `GITHUB_SHA` to the commit under test and `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT` and `GITHUB_RUN_NUMBER` to the placeholder `1`. These placeholders are local emulation values. They name no hosted run, and no artifact from these local gates is published or deployed. Hosted CI remains the authority for the release identity.

## CSP and profile shells (U2)

The emitted policy is the one string in `WasmContentSecurityPolicy` for the host page, pinned in `ArcForges.Web.App.Tests` (`WasmProfilePolicyTests.ExactPolicy`) and in `ArcForges.Web.Tooling.Tests` (`EmittedProfileContractTests`):

- `default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`

`script-src` is exactly `'self'` and `'wasm-unsafe-eval'` (the host page has no inline script, so no hash is added). `style-src` is `'self'`. No `'unsafe-inline'`, `'unsafe-eval'` or `'unsafe-hashes'` appears in the policy or the headers file. The bundle test builds the profile bundle from a publish whose shell is the real host page, then reads the served `account/index.html` and `chat/index.html` (identical bytes, one `<base href="/">` each) and the `_headers` file, which names the exact policy on `/account/*` and `/chat/*`.

Open point for the in-browser check (U7 and the local opt-in run, not yet executed): `base-uri 'none'` is a CSP directive that restricts the URLs a `<base>` element may use. Under CSP Level 3 a `<base href="/">` may then be blocked, so the document base would stay at `/account/` or `/chat/` and root-relative framework loads would resolve under the shell path. The offline tests cannot show this. It is not claimed as proven or as a defect. If the in-browser check confirms it, the fix is in the Ui policy, which is outside the PRF.11 write scope, so the task stops for a new decision (brief 5.13).

## Exact values and CON.92 limits (U3)

The exact-value helpers come from the pinned Contracts Foundation package (`ArcForges.Contracts.Foundation` 1.0.0-ci.287.1), not from new App helpers:

- `ExactInteger.ParseInt64` and `ExactInteger.ParseUInt64` (canonical text, no sign, leading zero or floating point; int64 edges and values above 2^53 stay exact). The App's own `Exact.TryUnsigned` is the probe's uint64 path.
- `ExactDecimal` (the shared exact decimal bound: at most nine fractional digits and twenty-eight significant digits, declared scale kept, no negative zero).

The CON.92 registry bounds are read from `WireLimits.Bytes` and pinned: unary and helper messages 4 MiB, inline pages 256 KiB, stream frames 32 KiB, large read projections 64 MiB, and 100 nested message levels. `WireLimitTests` decodes at the exact bound (accepted) and one byte over (`TooLarge`), shows that the same bytes are admitted as a unary message and refused as a stream frame, refuses truncated, bare-tag and reserved-wire-type frames as `Malformed`, and checks that encoding refuses one byte over the class.

`HelloProbe` now sets the gRPC channel's `MaxReceiveMessageSize` and `MaxSendMessageSize` to the unary class (4 MiB), so the greeting, a unary call, is bounded explicitly rather than by the library default. `HelloProbeTests.AGreetingReplyAboveTheUnaryMessageBoundIsRefusedNeverAccepted` pins that a reply whose message is over the class is `Malformed` and never a greeting.

## Static Site: no-script reading and determinism (U5)

- Determinism: the Site is built twice with the candidate command (`site build --out ... --source-ref <sha>`) and the two output trees are identical (`diff -r`, nine files, including `_headers` and the content-hashed stylesheet). The hosted csharp job repeats this as a gate.
- Offline, CI-eligible: `SiteOutputTests.EveryPageIsReadableWithScriptingDisabled` (existing) scans every page for script elements, event-handler attributes, framework references and `javascript:` URLs. The new `SiteOutputTests.TheOutputCarriesNoScriptOrWebAssemblyFileAtAll` refuses any `.js`, `.mjs` or `.wasm` output file and any script element in the HTML. The Site test run in the local gate passed 75 cases, skipped 1 (`SiteParityTests.TheCSharpSiteAgreesWithTheRecordedReactPrerenderWhenItIsNamed`, which is skipped by design unless a React prerender is named), and failed none.
- Local opt-in, not CI: `tests/browser/ArcForges.Web.Browser.Tests/LocalNoScriptBrowserTests.cs` reads `/`, `/hello/` and `/cloud-hello/` with JavaScript disabled and asserts a 200 status, non-empty `main` text, no script element, and no script request. It skips with the opt-in unset. Under CI conditions it skipped (the opt-in gate also refuses any CI host).
- The one local opt-in run: the Site output was served from `artifacts/site` on `127.0.0.1` (a local static server, not a deployed origin). The browser was the installed Google Chrome, `C:\Program Files\Google\Chrome\Application\chrome.exe`, product version 156.0.8078.12, named by `ARCFORGES_CHROMIUM_PATH`. No browser was downloaded. The test passed: 1 of 1, 4.5 s. Host Windows 11, Microsoft.Playwright 1.62.0 driving the installed binary by path (D10). The match between this Chrome build and the Chromium that Playwright 1.62.0 expects was not verified, so it is recorded as a mismatch risk. Claimant-reported; date 2026-10-09.
- Not claimed: the no-script reading of the deployed proof origin (blocked on CLOUD.85, not proven).

## Server-stream framing (U4)

The binary and grpc-web-text server-stream framings are recorded as fixtures in `StreamFramingTests`, and the transport decision (binary first, grpc-web-text only if the observed run fails) is recorded in `docs/prf-11-stream-transport.md`. That decision is open until the local opt-in observation (LS2) is made. No deployed stream is claimed.

Still not claimed: the live int64, uint64 and decimal calls through the deployed Cloud probe (blocked on CLOUD.21 and CLOUD.22, not proven), and the framing of server-streamed frames on the deployed ingress (U4 records the offline framing fixtures only).
