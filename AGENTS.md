# Repository instructions

- Read README and the relevant development/deployment guide before changes. Use English for source and repository documentation.
- Collect the relevant issues, decide a bounded plan, implement and verify. Use a separate worktree; leave adjacent repositories and primary checkouts unchanged.
- Preserve AGPL-3.0-only and third-party notices. Consume exact published Contracts packages; no sibling source imports or submodules.
- Use the pinned Node/npm toolchain, strict TypeScript, one root workspace lockfile and the documented commands. The server connection page only prepares the published Hello packaging example; business APIs, sessions, billing and AI execution remain outside this Web bootstrap.
- Keep PR checks credential-free. Never print or commit tokens or put private values in browser bundles. A Cloudflare API token belongs in the GitHub environment secret, not a client variable.
- Build and verify once, then deploy those same bytes. Distinguish mocked contract tests, local production-browser tests, real Cloudflare delivery and future C# product integration.
- Follow the user's authorization for remote settings/deployment. Do not merge a PR without authorization.

## Delivery model (P2-018)

Work is scheduled as delivery tasks in the [delivery graph](https://github.com/ArcForges/ArcForges-Design-B/blob/fd16c5f285de0bda2d0320cdff4d52c34c9098ed/docs/planning/delivery/README.md) and executed through the [Plan execution entry](https://github.com/ArcForges/Plan-B/blob/0cb637d1bfbf64d7db22a96a2b7370a409a25d8e/arcforges-implementation.md). There is no Current task, numbered substep order or single main context.

- Baseline: The accepted bootstrap is the static site generator, canonical-host Worker and Hello pages with the WP02 build, dependency and provenance baselines. Account, Chat and Operations are open tasks. This repository's tasks are in the [web](https://github.com/ArcForges/ArcForges-Design-B/blob/fd16c5f285de0bda2d0320cdff4d52c34c9098ed/docs/planning/delivery/lanes/web.md), [operations](https://github.com/ArcForges/ArcForges-Design-B/blob/fd16c5f285de0bda2d0320cdff4d52c34c9098ed/docs/planning/delivery/lanes/operations.md) lanes and parts of the governance, release and runtime-proof lanes.
- Start only a task that Plan-B's `python tools/delivery.py ready --claims` lists and whose `claims/<task-id>` branch you hold. A task here becomes ready only after the adoption slice for its lane (`ADOPT.09.<lane>`) is recorded.
- Several workers may work here at once, each on a different claimed task in its own retained worktree and `task/<task-id>` branch, inside the task's write scope. Site, Account, Chat and Operations surfaces each add their own route modules and features, so they proceed in parallel on the shared client and design system.
- Shared files follow their [declared protocols](https://github.com/ArcForges/ArcForges-Design-B/blob/fd16c5f285de0bda2d0320cdff4d52c34c9098ed/docs/planning/delivery/shared-resources.md): the application shell task owns root route registration and each surface adds its own route module and per-origin edge directory; workspaces, the lock file, CI and performance budgets are appended by the task that needs them and the lock file is regenerated after rebase, never hand-merged; the design-system task owns the shared UI package and surfaces request components through it. The Web integration owner orders merges and merges only pull requests of the claimant at the current claim epoch.
- Title pull requests `[<TASK-ID>] <summary>`; a bundle of compatible ready tasks lists each ID, and planning alignment uses `[P2-018]`.
- Earlier dated bootstrap and validation records under `docs/` describe their original scope; they are evidence, not execution instructions.

## Required validation limits

Follow [validation policy](docs/validation-policy.md), which supersedes older runtime and release-test requirements. Never add or execute macOS CI, device/emulator/GUI/browser E2E CI, live service or inference CI, installed-consumer CI or public-download verification. Keep runtime checks explicit local opt-in. Do not repeat public archive/hash checks, passing tests or post-merge runtime cycles. Preserve lock/signature/licence/provenance checks at actual trust handoffs. Do not invoke wsl.exe, configure proxy 7890 or install toolchains solely for testing. Stop and report the exact failed network operation. Hooks do not rebuild/test on commit or push.

Dependency additions and upgrades follow [the enforced admission policy](docs/dependency-policy.md); update its input-bound review and retain the existing class and provenance gates.
