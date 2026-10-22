# Repository instructions

- Read README and the relevant development/deployment guide before changes. Use English for source and repository documentation.
- Collect the relevant issues, decide a bounded plan, implement and verify. Use a separate worktree; leave adjacent repositories and primary checkouts unchanged.
- Preserve AGPL-3.0-only and third-party notices. Consume exact published Contracts packages; no sibling source imports or submodules.
- Use the pinned .NET 10 SDK (10.0.401, `global.json`) with central NuGet management and locked restores, and the documented commands. C# is the implementation language of every product surface (P2-021): the static Site, the Blazor WebAssembly profiles and the Razor class library. TypeScript is only the thin Cloudflare Worker adapter (`worker/index.ts`); Node runs wrangler and the reviewed policy checks. The server connection page only prepares the published Hello packaging example; business APIs, sessions, billing and AI execution remain outside this Web bootstrap.
- Keep PR checks credential-free. Never print or commit tokens or put private values in browser bundles. A Cloudflare API token belongs in the GitHub environment secret, not a client variable.
- Build and verify once, then deploy those same bytes. Distinguish mocked contract tests, local production-browser tests, real Cloudflare delivery and future C# product integration.
- Follow the user's authorization for remote settings/deployment. Do not merge a PR without authorization; under the Plan, the user's authorization of a range of tasks covers the coordinator's merges for those tasks.

## Flagship role

Web is an **active** repository of the ArcForges flagship ([AD-0001](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0001-flagship-pivot.md)); the design authority is [ArcForges-Design](https://github.com/ArcForges/ArcForges-Design). Per [architecture 01 §5](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/architecture/01-system-overview.md#5-repository-and-project-map), the [web experience](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/experience/web.md) and the [reuse classification](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/reuse/inventory-and-classification.md#4-web), it reuses the static Site generator, `ArcForges.Web.Ui`, the profile bundle and budget tooling, the Worker static-asset adapter and the guarded deploy. The public Site pages are rewritten for ArcForges, the `account` profile is adapted, the `fleet` profile is new and `ArcForges.Web.Operations` becomes the operator console. The Chat profile is retained and not routed (conflict C-12). Flagship surfaces deploy dark; publishing the public site and enabling production surfaces is Plan task AF-PROD-01 and needs the user's approval.

## Delivery (serial, ArcForges/Plan)

Tasks for this repository come from the [Plan delivery graph](https://github.com/ArcForges/Plan/blob/main/delivery-graph.json) and are delivered under the Plan [execution policy](https://github.com/ArcForges/Plan/blob/main/execution-policy.md) and [delivery procedure](https://github.com/ArcForges/Plan/blob/main/delivery-procedure.md) ([AD-0014](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0014-delivery-method.md)). They are binding. The historical P2-018 model (delivery lanes, adoption slices, `delivery.py` claims and handoffs, parallel workers, integration-owner roles, Plan leases, the build slot and the launcher) is retired and never resumed.

- One task is active at a time. Work only inside its write scope, in a worktree `.worktree/<task-id-lower>` on branch `task/<task-id-lower>`, never in the primary checkout.
- Delegated workers and reviewers work locally. Only the coordinator pushes, verifies the pushed head on both hosts, opens pull requests, merges (at the reviewed head, after the merge gate: the required statuses `arcforges/windows` and `arcforges/linux` for every PR, and hosted CI too for external PRs) and deploys through the guarded main-push path.
- Title pull requests `[<TASK-ID>] <summary>`.
- Shared files: the application shell owns root route registration, and each surface adds its own route module and per-origin edge directory; shared UI components live in `ArcForges.Web.Ui`. Lock files are regenerated after rebase, never hand-merged.
- Implemented baseline: the static Site generator, the canonical-host Worker and the Hello pages, with the WP02 build, dependency and provenance baselines; the Account and Chat profiles prove the session and gRPC-Web paths and are not a product yet; Operations is a skeleton.
- Earlier dated bootstrap and validation records under `docs/` describe their original scope; they are evidence, not execution instructions. Where older documents prefix a command with `delivery.py build-slot run … --` or assign a step to an integration owner, that is historical: run the wrapped command directly, and leave pushing and merging to the coordinator.

## Verification after the push (AD-0020)

Every PR into `main` is verified after the push, on the two verification hosts, and gated by GitHub ([AD-0016](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0016-local-first-verification.md) as amended by [AD-0020](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0020-post-push-verification-and-required-checks.md)). The flow: Push → Fetch → Local verification → Report status → Required checks → Review → Merge. The cross-repository rules are the Plan [AGENTS.md](https://github.com/ArcForges/Plan/blob/main/AGENTS.md) rules 6 and 7.

- **Push** the branch to GitHub; nothing is verified before a push. The only hook is the staged-whitespace `pre-commit` check. `python eng/verify.py install`, once per clone from the main checkout on `main`, installs it into the clone's common hooks directory and makes it executable. It also removes the retired pre-push hook and any `core.hooksPath` (this repository's retired `npm run hooks` set one). Re-run it when `.githooks/` changes.
- **Two legs on two hosts** ([AD-0019](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0019-mcp-first-verification.md)). Each step runs on one host, `win` (native Windows) or `linux` (native Linux), and `eng/verify.py` runs only the leg of the host's own operating system, never through WSL or a remote shell.
  - The Windows leg runs on the Windows 11 workstation, through the Windows MCP server.
  - The Linux leg runs on the Linux MCP host, through the Linux MCP server.
  - Both are standard MCP servers that Claude Code and Codex share; their client configuration and credentials stay outside this repository. Local WSL is never a venue, not even as a fallback.
- **Fetch and verify on each host.** In the host's clone, fetch the pushed commit from GitHub and run the command in a worktree at it:

  ```
  git fetch origin <branch>
  git worktree add --detach .worktree/verify-<sha> <sha>
  cd .worktree/verify-<sha>
  python eng/verify.py run-and-report
  cd ../..
  git worktree remove --force .worktree/verify-<sha>
  ```

  For an external PR (Dependabot included), read the diff first; for one from a fork, fetch `refs/pull/<n>/head`.

- **Report status.** Each host reports the commit status `arcforges/windows` or `arcforges/linux` on that exact commit:
  - `pending` when it starts;
  - `success` only when every step of its leg ran and passed;
  - `failure` when a step failed, `error` when the leg could not complete.

  The command exits 0 after success, 1 when a step failed and 2 when the leg could not complete or be reported. The host reports with `ARCFORGES_STATUS_TOKEN` when set (a token limited to commit statuses), otherwise with its own `gh` login; steps never receive either through their environment, and neither is printed. The `gh` login stays readable from the host's `gh` configuration by the code being verified, so set `ARCFORGES_STATUS_TOKEN` on the host.

- **Required checks.** The `main` ruleset requires both statuses for every PR, with no bypass actors, alongside the hosted `Verify` check (skipped for the owner's PRs). The branch must be up to date with `main`, and every new push needs verifying again on both hosts.
- **Stop rule** (AD-0019 §5). If the Linux (or Windows) MCP server is unavailable, unreachable or its host lacks a prerequisite, stop and report the exact failure: the status stays missing and the PR cannot merge. Never skip, mock or weaken a step, or run the leg on another host (local WSL or an agent's own sandbox included).
- `python eng/verify.py list` shows the profile and each host's steps. `python eng/verify.py run` runs this host's leg of `HEAD` without reporting.
- **What runs.** The commit, never the working tree, as listed in `eng/verify_steps.py`: every validation job of `ci.yml` and the `candidate` that `deploy` ships, each on the host the hosted job used. Every change runs the full profile (the naming scan and the provenance inventory read Markdown too).
  - Lane `win`, in `<main checkout>/.worktree/_verify`: `Source (windows-latest)`: `npm ci --ignore-scripts` (with the npm bundled with the selected Node) and `node tooling/project.ts licence-evaluated`; then `C# restore, build, test and publish (windows-latest)`: for each project of `ci.yml`'s `CSHARP_PROJECTS` the locked restore, `dotnet format --verify-no-changes` and the Release build; the tests of `CSHARP_TEST_PROJECTS`; the App and Operations publishes; the static Site built twice and compared byte for byte.
  - Lane `linux`, in the Linux-native clone `~/.cache/arcforges-verify/Web`: `clone-identity` (the `origin` URL is this GitHub repository, which the naming scan requires, HEAD a local branch at the commit and `origin/main` the merge base, which the provenance audit compares against; nothing is fetched from it), `npm ci --ignore-scripts`, `npm audit --audit-level=high` (from `quality`), the same C# steps as on Windows, `npm run check` (`Source (ubuntu-latest)`, with the Release tool of that build) and its evidence files; then `candidate`: a clean-tree test, the App publish, the profile budgets, the Worker and build identity emission, the sealed candidate built twice and compared, `node tooling/candidate.ts verify` and the profile bundle with its verification, at the local version `0.1.0-local`.
  - Lane `scan`: `git diff --check` and the pinned gitleaks image over the commits since the merge base with `.gitleaks.toml`, without network. `actionlint` when `.github/workflows/**` changed.
  - Both lanes start from an empty `artifacts/`. `CI` and `GITHUB_*` are not set; the MSBuild properties that `CI=true` selects (`RestoreLockedMode`, `ContinuousIntegrationBuild`) are set directly. The Blazor App is built as each hosted runner builds it: on Linux, `WasmNativeWorkload=false` keeps any installed `wasm-tools` workload out, as on `ubuntu-latest`, which has none (with it, the publish relinks the runtime and breaks the profile budgets); on Windows the workload stays in, as in the hosted Windows C# job, which installs `wasm-tools` at workload set 10.0.401 into its isolated SDK root and relinks its Release build and publishes with Emscripten 10.0.12. The Windows steps use the one SDK root that `DOTNET_ROOT` names (put first on `PATH`), whichever shell runs the leg; the fingerprint records it. Cloudflare credentials are unset and nothing contacts Cloudflare. Not reproduced: dependency review and CodeQL (GitHub services; external PRs, dispatch and the weekly schedule keep them). The browser test project stays local opt-in and is not part of the host verification.
- **Prerequisites** (installs are the user's): on Windows, `DOTNET_ROOT` naming the SDK root (Env.md: the user-local `C:\Users\J7Rdm\.dotnet`) with the .NET SDK selected by `global.json` and the `wasm-tools` workload at workload set 10.0.401 (Mono toolchain 10.0.112, Emscripten 10.0.12 packs; the App's lock records the SDK's `Microsoft.NET.Sdk.WebAssembly.Pack` 10.0.12). `win-workload-preflight` checks this read-only and prints the command for that root, on this workstation `& "C:\Users\J7Rdm\.dotnet\dotnet.exe" workload update --version 10.0.401`. Also on Windows: Node as pinned in `.node-version` with its bundled npm, PowerShell 7, Git and Python 3.14; on the Linux host, the same .NET SDK, Node and npm as pinned (`.node-version`, `packageManager`), Python 3.14 (`python` on PATH) and a running Docker Engine with the pinned gitleaks image; on both, `gh` with a credential that can write commit statuses, and network access to GitHub and the NuGet and npm registries.
- **Failures.** A failed, timed-out or unrunnable step reports `failure` and prints the step, the last log lines and the log path (`<git common dir>/arcforges-verify/logs/`, with `summary.json`). Read the log, fix the cause, push, verify again.
- **Statuses and rulesets.** A status is reported only by `python eng/verify.py run-and-report` on the host it names, at the commit it verified. Never post, edit or copy an `arcforges/*` status by hand, and never change the ruleset or required checks to get a PR merged.
- AI agents must never use `git push --no-verify` to bypass required validation.
- They must also never disable, remove, modify, or otherwise circumvent the hooks or required checks merely to get a change through.
- If validation fails, investigate and fix the underlying issue.
- If a required local test cannot run, do not silently skip it or claim successful verification.
- Changing `eng/verify*.py`, `.githooks/` or the ruleset as reviewed engineering work is allowed; changing them to get a change through is not.

## Efficient execution

The Plan's rule 7 applies. In this repository:

- Install once (`npm ci --ignore-scripts`, `dotnet restore <project> --locked-mode` for the changed projects), then build incrementally: `dotnet build <project> --no-restore -c Release`.
- While iterating, run only the affected checks: the changed test project (`dotnet test <test-project> --no-build -c Release`, with `--filter-class` for one class), the changed Node test file (`node --test tests/provenance/<name>.test.ts` or `tests/worker/<name>.test.ts`), `npm run check:dependencies` after a bound input changed, `npm run typecheck` and `dotnet format <project> --no-restore --include <files>`.
- The full profile (both C# legs, `npm run check` and the candidate) runs once per pushed head, on each host; `python eng/verify.py run` checks a leg before handover without reporting.

## Hosted CI

- **Owner PRs:** no hosted validation or security analysis; `Source`, `C# restore, build, test and publish`, `Dependency audit and repository checks`, `Dependency review`, `CodeQL`, the candidate and `Verify` show as skipped. `Verify` is the required hosted check (ruleset `protectmain`); a skipped `Verify` satisfies it. The `main` ruleset also requires the host statuses `arcforges/windows` and `arcforges/linux` for every PR ([Verification after the push](#verification-after-the-push-ad-0020)), which are the owner PRs' gate together with the review.
- **External PRs** (Dependabot included) and manual dispatches: the full CI as before: both `Source` and both `C#` legs, `quality` (audit, actionlint, the full-history secret scan), dependency review (PRs), CodeQL for C#, JavaScript/TypeScript and Actions, the candidate after `source` and `quality`, and `Verify`, which requires every one of them. An external PR merges only when `Verify` passes, both host statuses succeed and the code-scanning `CodeQL` check is not failing.
- **Main pushes:** delivery only: the candidate (with the main-push-only profile bundle upload), then `deploy`, which needs the candidate and keeps its environment, concurrency and steps. A broken candidate is caught before merge by the local profile, which builds it.
- **Schedule** (weekly): the security analysis: CodeQL and `quality` (dependency audit, actionlint and the full-history secret scan). No source, C# or candidate job.

## Required validation limits

Follow [validation policy](docs/validation-policy.md), which supersedes older runtime and release-test requirements. Its Design authority is the historical [CI and local validation policy](https://github.com/ArcForges/ArcForges-Design-Parallel/blob/64173614f2c41543bfff8f6df8d5a9e41207eb0a/docs/assurance/ci-and-local-validation-policy.md) (ArcForges-Design-Parallel at `64173614`), except its rule 9 (parallel delivery), which AD-0014 supersedes; where they differ from the Plan execution policy, the Plan governs. Never add or execute macOS CI, device/emulator/GUI/browser E2E CI, live service or inference CI, installed-consumer CI or public-download verification. Keep runtime checks explicit local opt-in. Do not repeat public archive/hash checks, passing tests or post-merge runtime cycles. Preserve lock/signature/licence/provenance checks at actual trust handoffs. Linux checks run on the Linux MCP host, through the Linux MCP server, never through local WSL, not even as a fallback ([AD-0018](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0018-native-platforms-without-wsl-integration.md), [AD-0019](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0019-mcp-first-verification.md)); installs there are the user's. Do not configure proxy 7890 or install toolchains solely for testing. Diagnose network failures, retry ordinary ones with bounded backoff, inspect remote state before retrying a write, and report the exact operation if it persists (Plan execution policy §5). The host verification after the push ([Verification after the push](#verification-after-the-push-ad-0020) above) is the one place the full build, tests and candidate run before an owner PR merges; the pre-commit hook checks only the staged whitespace.

Dependency additions and upgrades follow [the enforced admission policy](docs/dependency-policy.md); update its input-bound review and retain the existing class and provenance gates.
