# PRF.11 live runbook (local opt-in, not executed in CI)

Reviewer: `w-deku-20261008-rev-prf-11` (pre-assigned, brief S25). Decision: `approved`. reviewedOn: `2026-10-09`. These fields are a proposal written at write time. Only the named reviewer's exact-head approval ratifies them, and a refusal blocks the merge.

This runbook is for the local opt-in live run of the PRF.11 proof, against the deployed proof origin. It is not a CI job, and nothing in it runs on a CI host. The live specs are in `tests/browser/ArcForges.Web.Browser.Tests/LivePrf11Specs.cs` and skip without the opt-in. The record of what has been observed is in `docs/prf-11-profile-proof.md`. No credential is in this file or in the specs. Do not add one.

## Preconditions (all must hold before a run is recorded as evidence)

- CLOUD.85 is delivered: the Account and Chat shells are served at `/account/` and `/chat/` under base href `/`, with the framework at the root and the exact CSP on the served responses (CLOUD.85 D1). Until then, the shell checks are "blocked on CLOUD.85, not proven".
- CLOUD.21 and CLOUD.22 are delivered: the real generated methods are on the deployed gRPC-Web ingress, with the typed mapping. Until then, the exact-value, typed-failure and cancellation checks are "blocked on CLOUD.21/CLOUD.22, not proven".
- The lease `RES-cloud-deployment` is held by the claimant for the live run only (the coordinator grants it). Release it when the run ends. Nothing in this runbook takes or renews a lease.
- The browser is the installed Google Chrome or Microsoft Edge, named by path. No browser is downloaded (no `playwright install`).
- The run is on a developer machine (Windows 11 for this proof). It is not a CI host: `CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `BUILD_BUILDID` and `GITLAB_CI` must be unset, or the gate refuses the run.

## Opt-in variables

| Variable | Value | Meaning |
| --- | --- | --- |
| `ARCFORGES_LIVE_PRF11` | `1` | Enables the live specs. |
| `ARCFORGES_PROOF_ORIGIN` | `https://proof.arcforges.com/` | The proof origin. It must be https, with no path, query, fragment or user information. |
| `ARCFORGES_CHROMIUM_PATH` | the installed `chrome.exe` or `msedge.exe` path | The browser executable. |
| `ARCFORGES_PRF11_CLOUD_READY` | `1` | Marks the cloud parts ready. Set only after CLOUD.21, CLOUD.22 and CLOUD.85 are delivered and the ingress is observed. |
| `ARCFORGES_PRF11_RESULTS` | a local folder outside the repository | Where the INP measurement is written. The folder is not committed. |

## Run

1. Build the browser project locally, after a locked restore of `tests/browser/ArcForges.Web.Browser.Tests/ArcForges.Web.Browser.Tests.csproj`.
2. Run the live specs with the variables above set, and with the CI markers unset:

   `dotnet test tests/browser/ArcForges.Web.Browser.Tests/ArcForges.Web.Browser.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~LivePrf11Specs"`

3. Read the skip reasons. A skipped cloud spec is "blocked on CLOUD.21/CLOUD.22, not proven" and is not evidence.

## What each spec records

- `TheShellsAreServedOnTheProofOriginWithRootBaseAndTheExactPolicy`: HTTP 200 for `/account/` and `/chat/`, one `<base href="/">`, the exact CSP string on each response, and `/_framework/blazor.webassembly.js` at the root.
- `TheShellsLoadInTheInstalledBrowserWithNoContentSecurityPolicyViolation`: the in-browser CSP check. The page is loaded with JavaScript enabled, and any console or page-error message that names a Content-Security-Policy violation fails the run. This is the check for the `base-uri 'none'` open point.
- `TheAnonymousGreetingRoundTripsOnTheDeployedIngress` (cloud-ready only): the Chat greeting for a name returns the exact text on the deployed origin.
- `TheGreetingInteractionResponsivenessIsCapturedForTheRebaseline` (cloud-ready only): five greetings and the `event`-type INP durations, written as JSON under `ARCFORGES_PRF11_RESULTS`. No budget is asserted. The values feed the AL-06 interaction re-baseline (owner PRF.11).

## Manual steps (not automated, and not run by these specs)

These need an authenticated session or the CLOUD.21 methods, and they must not be automated with credentials here:

- The exact int64, uint64 and decimal values through the real Cloud probe (for example `WorkspaceService.GetHealth` counters and an `UpdateSettings` revision precondition), read back exactly.
- Typed failures from the real mapping (CLOUD.22), including an HTTP 200 with error trailers, and a deadline.
- Cancellation after dispatch.
- Session expiry, the CSRF token on a state change, and logout receipt handling on the deployed origin.
- The binary `application/grpc-web+proto` server stream observation (LS2 in `docs/prf-11-stream-transport.md`), and grpc-web-text only if the binary observation fails.

Record each manual step with the host, the browser and its version, the commit, the date, the deployment identity, and that the result is claimant-reported. Do not record a manual step as automated.

## Recording

The claimant records the host, browser, commit, date and deployment identity in `docs/prf-11-profile-proof.md` for every run that is cited as evidence. Failed or skipped runs are recorded as such. Nothing here is a CI result.
