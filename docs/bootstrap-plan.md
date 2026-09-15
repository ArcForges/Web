# Web bootstrap plan

## Collected baseline

- The repository starts at `9c683d5` with the AGPL-3.0 license only. Work is isolated on `codex/web-bootstrap` in a separate worktree.
- The accepted Web architecture uses React, strict TypeScript, React Router framework mode, Vite, Node 24 LTS and npm workspaces. Public content is rendered at build time; business authority stays in the C# Cloud host.
- Registry metadata checked on 2026-09-15: TypeScript 7.0.2, React 19.3.0, Router 8.4.0, Vite 8.3.0, Node 24.21.0/npm 11.19.0. Router 8.4 is the current stable minor in the selected major. Exact dependencies and the resolved lockfile will be committed.
- Published Contracts currently expose the Hello example in `@arcforges/proto` and `@arcforges/api-client` 1.0.0-ci.25.1. Use those immutable packages, with their declared protobuf runtime, rather than adjacent source.
- Cloudflare supports Workers Static Assets directly. This bootstrap needs no request-time application server, database, AI binding, domain change or Workers paid feature.
- The repository initially has no Cloudflare environment or credentials. Create a main-only `cloudflare` environment and configure the existing account ID; the owner must supply its deployment API token through GitHub's secret UI.

Owner decision during implementation: defer the deployment credential. Complete local/PR validation and the deployment pipeline, but do not perform a real Cloudflare deployment in this task. The environment and account variable have been configured; the token remains absent.

## Bounded implementation

1. Create one root npm workspace, a public Hello site under `apps/site`, shared UI under `packages/ui`, TypeScript tooling/tests, and an optional Windows IDE solution. `apps/app` documents the future Account/Chat boundary; this bootstrap does not claim those products are implemented.
2. Pre-render the public home and greeting example. Initial content/navigation work without JavaScript. Demonstrate the published protobuf messages locally; exercise the published gRPC-Web client against explicit wire fixtures in tests. Do not invent a business API or claim a real C# integration.
3. Add Git ignore/attributes/editor settings, exact toolchain/dependency pins, formatting/lint/type checks, worktree-local hooks, license/notices, contribution/security/conduct guidance, issue/PR templates and Dependabot groups.
4. Add unit/component, Chromium/Firefox/WebKit and accessibility checks. Test production assets served by local Wrangler, including no-JavaScript content, keyboard interaction, narrow layout, CSP, cache headers and real 404s for missing assets/API paths.
5. Build a single immutable candidate containing only static browser assets, generated security headers, public build identity, release manifest, source/dependency provenance, licenses and SBOMs. Verify the file set and hashes before promotion. No runtime SSR bundle or credential is shipped.
6. CI validates Windows/Linux source, dependency/security/CodeQL checks, builds once and verifies that candidate in browsers. Main deploys the same bytes with Wrangler, checks their identity/content over HTTPS, and creates a verified GitHub prerelease only after live verification. PRs never access deployment secrets.
7. Enable the repository's supported Dependabot/private-reporting/secret-scanning settings. Validate locally and in PR CI; perform a real Cloudflare deployment when credentials are available. Report each evidence boundary explicitly.

## Closure

- Local and CI checks pass; generated production pages render and their interaction works.
- Candidate verification rejects changed files, wrong source and accidental public secrets/maps; deployment never rebuilds.
- Main deployment cannot silently skip because the PR-only dependency review skipped, or because a secret is missing.
- Live verification checks the deployed candidate, with bounded read-only polling for propagation. It does not create more deployments on failure.
- The primary checkout, Design, Contracts and AI source remain unchanged. The branch is committed and submitted as a PR with setup and recovery instructions.

## Product boundary

This is a Hello World and delivery foundation, not the completed public catalogue, Account/Chat applications, Cloud sessions, payments, AI harness or commercial release. Their existing design scope is retained for subsequent implementation. Custom domains, the complete profile release set and real C# integration belong to that work.
