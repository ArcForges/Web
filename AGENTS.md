# Repository instructions

- Read README and the relevant development/deployment guide before changes. Use English for source and repository documentation.
- Collect the relevant issues, decide a bounded plan, implement and verify. Use a separate worktree; leave adjacent repositories and primary checkouts unchanged.
- Preserve AGPL-3.0-only and third-party notices. Consume exact published Contracts packages; no sibling source imports or submodules.
- Use the pinned Node/npm toolchain, strict TypeScript, one root workspace lockfile and the documented commands. The server connection page only prepares the published Hello packaging example; business APIs, sessions, billing and AI execution remain outside this Web bootstrap.
- Keep PR checks credential-free. Never print or commit tokens or put private values in browser bundles. A Cloudflare API token belongs in the GitHub environment secret, not a client variable.
- Build and verify once, then deploy those same bytes. Distinguish mocked contract tests, local production-browser tests, real Cloudflare delivery and future C# product integration.
- Follow the user's authorization for remote settings/deployment. Do not merge a PR without authorization.

## Required validation limits

Follow [validation policy](docs/validation-policy.md), which supersedes older runtime and release-test requirements. Never add or execute macOS CI, device/emulator/GUI/browser E2E CI, live service or inference CI, installed-consumer CI or public-download verification. Keep runtime checks explicit local opt-in. Do not repeat public archive/hash checks, passing tests or post-merge runtime cycles. Preserve lock/signature/licence/provenance checks at actual trust handoffs. Do not invoke wsl.exe, configure proxy 7890 or install toolchains solely for testing. Stop and report the exact failed network operation. Hooks do not rebuild/test on commit or push.

Dependency additions and upgrades follow [the enforced admission policy](docs/dependency-policy.md); update its input-bound review and retain the existing class and provenance gates.
