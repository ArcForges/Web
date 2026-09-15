# Repository instructions

- Read README and the relevant development/deployment guide before changes. Use English for source and repository documentation.
- Collect the relevant issues, decide a bounded plan, implement and verify. Use a separate worktree; leave adjacent repositories and primary checkouts unchanged.
- Preserve AGPL-3.0-only and third-party notices. Consume exact published Contracts packages; no sibling source imports or submodules.
- Use the pinned Node/npm toolchain, strict TypeScript, one root workspace lockfile and the documented commands. Business APIs, sessions, billing and AI execution are outside this static Web bootstrap.
- Keep PR checks credential-free. Never print or commit tokens or put private values in browser bundles. A Cloudflare API token belongs in the GitHub environment secret, not a client variable.
- Build and verify once, then deploy those same bytes. Distinguish mocked contract tests, local production-browser tests, real Cloudflare delivery and future C# product integration.
- Follow the user's authorization for remote settings/deployment. Do not merge a PR without authorization.
