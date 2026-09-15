# Bootstrap validation

Evidence is recorded separately for source checks, local production assets, hosted CI and real delivery. The bootstrap plan is in `bootstrap-plan.md`.

## Local evidence (2026-09-15)

- Windows pinned toolchain: exact dependency restore, policy, formatting, Biome, strict TypeScript and eight unit/component/SDK/delivery tests have passed.
- A static candidate with source identity, file hashes, CSP, licenses, Contracts provenance and SBOMs has been built and verified locally.
- Twelve production-browser tests passed across Chromium, Firefox and WebKit against local Wrangler. They cover hydration under the generated CSP, greeting/validation, no transmitted name, no-JavaScript content/navigation, axe WCAG checks, narrow layout, cache/security headers and real 404s. Desktop and mobile screenshots were visually inspected.
- npm audit reported zero known vulnerabilities. The SHA-verified actionlint 1.7.12 executable accepted the workflow.
- `dotnet build win.slnx --no-restore` accepted the optional solution with workload resolution disabled; it is not evidence of interactive Visual Studio startup or a separate .NET build.
- GitHub PR checks will provide separate hosted Linux/Windows, CodeQL, dependency-review, secret-scan and browser evidence. Their result is reported on the PR.

## Deferred or outside scope

- Real Cloudflare deployment is deferred by the owner because the environment API token is not being configured yet. No public deployment or live verification is claimed.
- The gRPC-Web tests use binary wire fixtures; they do not call a real C# Cloud service.
- Complete Account/Chat flows, login, payment, AI and commercial product acceptance are outside this Hello bootstrap.
- Visual Studio interactive startup requires the appropriate local IDE; npm is the authoritative validated build path.
