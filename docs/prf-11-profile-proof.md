# PRF.11 Blazor WebAssembly production profile and generated C# SDK proof

Reviewer: `w-deku-20261008-rev-prf-11` (pre-assigned, brief S25). Decision: `approved`. reviewedOn: `2026-10-09`. These fields are written at write time as a proposal. Only the named reviewer's exact-head approval ratifies them, and a refusal blocks the merge.

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

The gates run the workflow's commands with `GITHUB_ACTIONS=true`, `CI=true` and `NUGET_PACKAGES` set to an empty folder under the session scratchpad, so every locked restore downloads from the locked feeds. The ArcForges.Build.Policy analyzer (AFP006) requires a complete CI identity under `GITHUB_ACTIONS=true`, so the gate sets `GITHUB_SHA` to the commit under test, `GITHUB_REPOSITORY` to `ArcForges/Web` (the analyzer builds the pipeline-run URL from it, and without it the build fails with AFP006), and `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT` and `GITHUB_RUN_NUMBER` to the placeholder `1`. These placeholders are local emulation values. They name no hosted run, and no artifact from these local gates is published or deployed. Hosted CI remains the authority for the release identity.

The gates also start from a clean state: `git clean -fdX` removes only ignored build output (`bin`, `obj`, `node_modules` and `artifacts`) before the run. This matters because stale files left in `artifacts/publish/app` by an earlier publish fail the profile bundle's static-graph check ("A published file is not named by the build's static web assets manifest"), which a fresh CI checkout never meets.

## CSP and profile shells (U2)

The emitted policy is the one string in `WasmContentSecurityPolicy` for the host page, pinned in `ArcForges.Web.App.Tests` (`WasmProfilePolicyTests.ExactPolicy`) and in `ArcForges.Web.Tooling.Tests` (`EmittedProfileContractTests`):

- `default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`

`script-src` is exactly `'self'` and `'wasm-unsafe-eval'` (the host page has no inline script, so no hash is added). `style-src` is `'self'`. No `'unsafe-inline'`, `'unsafe-eval'` or `'unsafe-hashes'` appears in the policy or the headers file. The bundle test builds the profile bundle from a publish whose shell is the real host page, then reads the served `account/index.html` and `chat/index.html` (identical bytes, one `<base href="/">` each) and the `_headers` file, which names the exact policy on `/account/*` and `/chat/*`.

Open point, confirmed by a local observation (see the open-decision section): `base-uri 'none'` in the served policy blocks the `<base href="/">` of the shells, and under that policy the Account and Chat shells do not start. The browser logs `Setting the document's base URI to ... violates the following Content Security Policy directive: "base-uri 'none'". The action has been blocked.` The fix is a change to the Ui policy (`WasmContentSecurityPolicy`), which is outside the PRF.11 write scope, so the proof stops for a new decision (brief 5.13). The offline tests pin the current string and do not claim it is correct.

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

## Inline-style audit, in-browser CSP and live specs (U7)

- Inline-style audit (static, offline): no `style=` attribute and no `<style>` element in the Razor components in use (`src/ArcForges.Web.App`: `App.razor`, `Layout/ProfileLayout.razor`, `Pages/Account.razor`, `Pages/Chat.razor`; `src/ArcForges.Web.Ui`: `Arrow`, `Button`, `CloudHelloExample`, `HelloExample`, `HomeContent`, `Shell`), and none in the host page, which loads only `app.css`. No C# builder emits a `style` attribute. This is a static audit. It does not prove that the framework sets no inline style at run time, and the in-browser check is the real test.
- In-browser CSP (local emulation, not the deployed origin): the bundle built from the publish (`profiles-u6`) was extracted and served on `127.0.0.1` with the exact per-profile `Content-Security-Policy` from its `_headers`. Headless Google Chrome 156.0.8078.12 loaded `/account/`. Chrome logged `Setting the document's base URI to 'http://127.0.0.1:4180/' violates the following Content Security Policy directive: "base-uri 'none'". The action has been blocked.` The message reproduced in two enforced runs. The root framework path `/_framework/blazor.webassembly.js` returned 200. The same shell path `/account/_framework/blazor.webassembly.js` returned 404, because the relative framework path resolves under the shell path once the base element is blocked.
- Render outcome under the served policy (fix1, observed locally; see the open-decision section): the Account shell does not start. Four of four runs logged the base-uri violation and never rendered the heading. The live spec `TheShellsLoadInTheInstalledBrowserWithNoContentSecurityPolicyViolation` is the in-browser check on the deployed origin, and it fails on the violation message.
- Decision needed (stop, per the plan and brief 5.13): the served policy blocks the base element of the shells, so the shells do not start, a violation the offline tests cannot see (fix1 observation above). The fix is in the Ui policy, outside the PRF.11 write scope, so no change is made here. The options are for the coordinator: a reviewed Ui change to `base-uri` (for example `'self'`), or a reviewed host-page change. The exact policy string and its test pins stay as they are until that decision.
- Live specs (`tests/browser/ArcForges.Web.Browser.Tests/LivePrf11Specs.cs`, with `LivePrf11OptIn.cs`): four specs, skipped without `ARCFORGES_LIVE_PRF11=1` and an https proof origin, and skipped on a CI host. Under CI conditions all four skipped, and the 20 guard and opt-in tests passed. The cloud specs (greeting round trip and INP capture) also need `ARCFORGES_PRF11_CLOUD_READY=1`, and skip as "blocked on CLOUD.21/CLOUD.22, not proven" without it. They have not been run against the deployed origin. The runbook is `docs/prf-11-runbook.md`.
- Not claimed: exact int64, uint64 and decimal calls through the deployed probe, typed failures, cancellation, session and CSRF on the deployed origin (blocked on CLOUD.21 and CLOUD.22, not proven; manual steps in the runbook), and the deployed CSP.

## Asset budgets, file count and WA-08 costing (U6)

The AL-06 re-baseline of the asset baseline (`eng/policy/profile-budgets.json`), the static-asset file count against the Workers platform limit, and the WA-08 costing of the non-virtualised chat list are recorded in `docs/prf-11-budgets.md`. The asset baseline is the measured local publish (174 served files, 116 precompressed; the platform limit is 20,000 files per Worker version on Free). The interaction budgets stay re-baseline-pending (owner PRF.11) until the deployed run (LS3, blocked on CLOUD.85). The AOT benchmark is not run (no `wasm-tools` workload installed).

## Server-stream framing (U4)

The binary and grpc-web-text server-stream framings are recorded as fixtures in `StreamFramingTests`, and the transport decision (binary first, grpc-web-text only if the observed run fails) is recorded in `docs/prf-11-stream-transport.md`. That decision is open until the local opt-in observation (LS2) is made. No deployed stream is claimed.

Still not claimed: the live int64, uint64 and decimal calls through the deployed Cloud probe (blocked on CLOUD.21 and CLOUD.22, not proven), and the framing of server-streamed frames on the deployed ingress (U4 records the offline framing fixtures only).

## Offline proof record and completion handoff (U9)

### Status

PRF.11 is delivered on its offline units U1 to U9 in local commits on `task/prf-11`. It is **not complete**. The completion edges CLOUD.21, CLOUD.22 and CLOUD.85 are not started, and under the served CSP the shells do not start, which needs a coordinator decision (below). The task record names PRF.08 as superseded, and this record supersedes PRF.08 (`docs/prf-08-profile-proof.md` is history). PRF.11 is the successor of the WEB.40 app-proof row (ruling S28(i)).

### Offline receipt (WP-06.05, offline portion)

| Item | Evidence | Result |
| --- | --- | --- |
| Start identity and edges (U1) | `docs/prf-11-profile-proof.md` start section; CON.92 limits in Contracts `docs/architecture.md` at ca45f36; CON.07 identity `ArcForges.Contracts.PublicApi 1.0.0-ci.287.1` | Recorded; P0c holds |
| Exact CSP token set and base hrefs (U2) | `WasmProfilePolicyTests.TheEmittedPolicyIsTheExactReviewedString`, `TheHostPageDeclaresExactlyOneBaseAtTheRootAndTheShellsRelyOnIt`; `EmittedProfileContractTests` (4 cases) | Passes offline; the string is pinned. Under it the shells do not start in the browser (see the decision below), so the in-browser CSP check is not passed |
| Exact int64, uint64, decimal and CON.92 limits (U3) | `ExactUnsignedTests` (int64, decimal); `WireLimitTests` (7 cases); `HelloProbeTests.AGreetingReplyAboveTheUnaryMessageBoundIsRefusedNeverAccepted` | Passes offline |
| Server-stream framing fixtures and decision (U4) | `StreamFramingTests` (6 cases); `docs/prf-11-stream-transport.md` | Fixtures pass; the decision is open until the observed run (LS2) |
| No-script reading and determinism (U5) | `SiteOutputTests.TheOutputCarriesNoScriptOrWebAssemblyFileAtAll` and the existing `EveryPageIsReadableWithScriptingDisabled`; Site built twice and diffed (identical, nine files); `LocalNoScriptBrowserTests` (local opt-in, one run with installed Chrome 156.0.8078.12, passed) | Offline and local evidence recorded; claimant-reported |
| AL-06 asset re-baseline, file count, WA-08 costing (U6) | `docs/prf-11-budgets.md`; `profiles budget` passed; `profiles bundle` and `profiles verify` (174 served files, 116 precompressed, limit 20,000 on Free) | Recorded; interaction budgets stay re-baseline-pending |
| Inline-style audit, in-browser CSP and live specs (U7) | `docs/prf-11-runbook.md`; `LivePrf11Specs` (4, skipped without the opt-in); `LivePrf11OptInTests` and `LocalOptInTests` (guards) | Audit clean (static); live specs not run; see the decision below |
| NuGet closure admission (U8) | Conditional. The `git diff` against the WEB.40 base shows no change to any `*.csproj`, `packages.lock.json`, `Directory.Packages.props`, `Directory.Build.props`, `NuGet.config` or `eng/policy/dependency-policy.json` | Skipped (no closure change) |

### Open decision (stop, brief 5.13): `base-uri 'none'` blocks the shells' base href

The served policy (`WasmContentSecurityPolicy`, Ui, outside the PRF.11 write scope) contains `base-uri 'none'`. Under that policy the Account and Chat shells do not start. This is the observed outcome of fix1 (2026-10-09), not an open question, and it was reproduced locally with the installed Google Chrome 156.0.8078.12, driven by path with no download:

- The bundle was built under CI conditions (App publish, then `profiles bundle` and `profiles verify`, 175 members), extracted to a scratch folder, and served on `127.0.0.1` with the exact per-profile `Content-Security-Policy` from its `_headers`. Four of four headless runs of `/account/` logged `Setting the document's base URI to 'http://127.0.0.1:4190/' violates the following Content Security Policy directive: "base-uri 'none'". The action has been blocked.` and left the DOM on `Loading…`. The heading `Your session, as the server sees it.` never rendered.
- The mechanism is the relative framework loader. With the base element blocked, `_framework/blazor.webassembly.js` resolves to `/account/_framework/blazor.webassembly.js`, which returns 404, while the root path returns 200. Blazor therefore never boots.
- Control, in a scratch server only (not in the repository): the same bundle with `base-uri 'self'` logged no violation, and the heading rendered in 2 of 4 runs. The other two runs stayed on `Loading…` with no violation logged. The `--dump-dom` method is not reliable enough to settle the render in every control run. What the control does establish is that no violation is logged under `base-uri 'self'`, in all four runs. With no CSP header the heading rendered in 2 of 2 runs.
- Claimant-reported, local, not the deployed origin. The live spec on the deployed origin remains the check that the shells start.

The offline tests cannot see this, because they pin the policy string and do not run a browser. The fix is a reviewed change to the Ui policy (for example `base-uri 'self'`) or a reviewed host-page change. Both are outside the PRF.11 write scope and need a new coordinator decision (brief 5.13, S20(b)), so this record makes no code change. Until that decision lands, the shells do not start under the served policy, the in-browser CSP and shell checks are not passed, and the policy string stays pinned as served. CLOUD.85 would serve that string on the proof origin, so the decision is needed before CLOUD.85 deploys.

### Gates (fix1 re-run under CI conditions, code head `b84e2e3`)

The gates were re-run after the fix1 record changes, from a clean state (`git clean -fdX`, then an empty NuGet folder under the scratchpad), at `b84e2e3`. Every step ran in the build slot. Three local conditions were corrected on the way and are recorded in the local CI conditions section: `GITHUB_REPOSITORY` for AFP006, the clean start against stale publish output, and building the C# tool before `npm test`, which the CI source job does first on Linux. These are local-condition fixes, not product changes.

| Gate | Result | Notes |
| --- | --- | --- |
| Locked restores (every CSHARP_PROJECTS entry, `--locked-mode`) | passed | `NUGET_PACKAGES` is an empty scratch folder |
| `dotnet format --verify-no-changes --no-restore` (every CSHARP_PROJECTS entry) | passed | |
| `dotnet build -c Release --no-restore` (every CSHARP_PROJECTS entry) | passed | 11 projects, zero errors and zero warnings |
| `dotnet test --no-build -c Release` (every CSHARP_TEST_PROJECTS entry) | Tooling 56/56; Site 75 passed, 1 skipped by design (`SiteParityTests`, no React prerender named); Ui 14/14; App 91/91; Operations 5/5; Policy 108/108 | |
| Profile publish (App to `artifacts/profiles/app`, Operations to `artifacts/profiles/operations`) | passed | IL build; `RunAOTCompilation` false; no `wasm-tools` workload, so AOT is not run |
| Static Site (`site build` twice, `diff -r`) | identical | |
| Candidate job: tooling restore and build, App locked restore and publish to `artifacts/publish/app` | passed | |
| Profile budgets (`profiles budget`) | passed | against the re-baselined values |
| Candidate (worker and identity emitted, built twice, `diff -r`, verified) | passed; the two trees are identical; verified at 20 members | Local identity placeholders (see the local CI conditions section) |
| Profile bundle (`profiles bundle` and `profiles verify`) | passed (175 members) | |
| `npm ci --ignore-scripts --engine-strict=false` | passed | `--engine-strict=false` is needed locally (see the environment gaps) |
| `npm audit --audit-level=high` | 0 vulnerabilities | |
| `npm run check:dependencies` | passed | |
| `npm run typecheck` | passed | |
| `npm run test:dependencies` | 15 of 15 passed | |
| `npm run test` | 94 of 94 passed | Run after the C# tool was built. `tests/provenance/csharp-candidate.test.ts` needs the built tool, as the CI source job builds it first |
| `npm run policy` | fails at the Node version assertion (`v24.20.0` against the `v24.21.0` pin) | Environment gap, unchanged from the U9 record. The later assertions are not reached locally. Hosted CI is authoritative |
| `node tooling/project.ts licence-evaluated` (Windows source leg) | passed | |
| `actionlint` 1.7.12 on `.github/workflows/ci.yml` | passed | Workflow not changed |
| gitleaks (pinned image `c00b6bd0`, WSL Debian Docker, `--network none`, over a fresh clone at `b84e2e3`) | no leaks found; 102 commits scanned | The image was already cached, so it was run by its image ID and not pulled. The `git` mode scans the history reachable from HEAD |
| Hosted only (not run here) | CodeQL (javascript-typescript, actions, csharp), dependency review (PR only), the Linux legs of the csharp and source jobs, the candidate job on Linux, the verify job, and the deploy job (main push only) | Recorded as hosted. `npm run deploy` was not run |

### Environment gaps (not fixes)

- Node 24.20.0 is installed locally against the 24.21.0 pin, and npm 12.0.2 against the 11.19.0 pin. No toolchain was installed for this run.
- The WSL2 Debian SDK is 10.0.400, so the locked Linux restore fails with NU1004 there. Linux checks were not run, because they are hosted in CI.

### Commits (local, on `task/prf-11`, not pushed)

`fc4fba0` (U1), `f27a9b4` (U2), `9edf001` (U3), `b21aa64` (U4), `a1ede81` (U5), `2a21bc3` (U6), `db38e2c` (U7), `98547cb` (inventory fix), `3283edf` (U9), then `b84e2e3` (fix1: the shell outcome and the S25 review fields). The fix1 gate re-run record is the commit after `b84e2e3`. U8 is skipped by its own condition.

### Completion blockers (blocked, not proven)

- CLOUD.21 and CLOUD.22 are not started. The exact int64, uint64 and decimal calls, typed failures, cancellation after dispatch, session and CSRF on the deployed origin are blocked on them, not proven.
- CLOUD.85 is not started. Same-origin serving of the shells at `/account/` and `/chat/` under base href `/`, the framework at the root and the exact CSP on the served responses are blocked on it, not proven.
- The base-uri decision above: under the served policy the Account and Chat shells do not start, so the coordinator must decide the Ui fix before CLOUD.85 deploys the string.
- The observed server-stream run (LS2), the INP measurement for the AL-06 interaction re-baseline (LS3), and the AOT benchmark (not run).
- The deployed proof origin is not observed. Every local result is claimant-reported.

### Review fixes (fix1, 2026-10-09)

The independent reviewer's material findings at `3283edf` and their dispositions:

- **Shells do not start under the pinned policy (material).** Confirmed locally; see the open-decision section. The finding asks for `base-uri` to change in `src/ArcForges.Web.Ui/WasmContentSecurityPolicy.cs`. That file is outside the PRF.11 write scope, and brief 5.13 and S20(b) make an in-browser CSP change that needs a Ui edit a stop for a new decision. So no code is changed. The record now states the observed outcome, and the decision is listed in the completion blockers for the coordinator. The pinned string and its test pins are unchanged.
- **S25 review fields missing (material).** This record now carries the reviewer, `decision: approved` and `reviewedOn` fields at its head (above). `docs/prf-11-stream-transport.md` carries the same fields, and `docs/prf-11-budgets.md` already did. The S25 fields are a proposal ratified only by the named reviewer's exact-head approval.

### CLOUD.71 history (D11)

CLOUD.71 is ledger-complete for the React bytes only. Its start edge names PRF.11 while PRF.11 has a complete edge on it. Treated as complete-task history (brief S16(a)); no action in this run.

### Not claimed

- PRF.11 completion, and WP-06.05 and PG-23 as proven (only the offline portions are recorded).
- Any deployed result: the deployed CSP, the deployed shells, the live calls, the live streams, the live INP.
- Startup time and interaction responsiveness budgets (not measured), and the AOT benchmark (not run).
- The WA-08 numeric sustained chat memory budget (not found in the Design authority at `b01ae31`; the costing is by construction only).
- Any macOS result (out of scope, P2-023), and any WSL2 or Linux result (hosted).
