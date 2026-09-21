# Bootstrap validation

Historical verification evidence. Current execution follows [validation policy](validation-policy.md); these recorded runs are not repeated CI or post-merge requirements.

Evidence is recorded separately for source checks, local production assets, hosted CI and real delivery. The bootstrap plan is in `bootstrap-plan.md`.

## Local evidence (2026-09-15)

- Windows pinned toolchain: exact dependency restore, policy, formatting, Biome, strict TypeScript and eight unit/component/SDK/delivery tests have passed.
- A static candidate with source identity, file hashes, CSP, licenses, Contracts provenance and SBOMs has been built and verified locally.
- Twelve production-browser tests passed across Chromium, Firefox and WebKit against local Wrangler. They cover hydration under the generated CSP, greeting/validation, no transmitted name, no-JavaScript content/navigation, axe WCAG checks, narrow layout, cache/security headers and real 404s. Wide and narrow browser viewport screenshots were visually inspected; these are not native desktop or Android application tests.
- npm audit reported zero known vulnerabilities. The SHA-verified actionlint 1.7.12 executable accepted the workflow.
- `dotnet build win.slnx --no-restore` accepted the optional solution with workload resolution disabled; it is not evidence of interactive Visual Studio startup or a separate .NET build.
- GitHub PR checks will provide separate hosted Linux/Windows, CodeQL, dependency-review, secret-scan and browser evidence. Their result is reported on the PR.

## Real deployment evidence (2026-09-15)

- [Main CI run 35030151116](https://github.com/ArcForges/Web/actions/runs/35030151116) deployed source `485150f00cbe9491e02f22a9156f9ce667e9f6dd` as `0.1.0-ci.5.1` and passed remote file/header/404 verification at the initial Workers subdomain. A verified prerelease was created.
- Additional Chromium, Firefox and WebKit checks against that real deployment passed navigation, hydration and greeting with no runtime errors.
- The owner attached `arcforges.com`; HTTPS and `__build.json` identify the same deployment. Browser validation then identified an automatically injected Cloudflare analytics script blocked by CSP. The custom-domain change prevents that injection and adds real browser checks to the release gate. Its deployment is not claimed by the preceding bootstrap evidence.

## Custom-domain local validation

Custom-domain local validation passed the pinned restore, source checks, nine unit/component/delivery tests, candidate build, twelve Chromium/Firefox/WebKit tests and post-browser candidate verification. The live browser gate itself requires the new configuration to be deployed; local results do not establish that the Cloudflare edge honors `no-transform`.

## Custom-domain hosted delivery

The custom-domain change and its bounded propagation correction are now deployed: [main run 35033094886](https://github.com/ArcForges/Web/actions/runs/35033094886) passed on source `0405df4b85c36f32988b0847d8c53fc0c80f1c24`, version `0.1.0-ci.10.1`. Its real file/header/404 checks and twelve Chromium/Firefox/WebKit tests passed, and the verified release was created. An independent public check returned that identity from `https://arcforges.com/__build.json` and HTTP 404 from the disabled `arcforges-web.sammilergood.workers.dev` entry. This establishes the existing site's delivery, not the subsequent Cloud connection feature or a running C# service.

## Cloud Hello preparation: local evidence

- Source checks passed with twelve unit/component/SDK/delivery tests after rebasing onto the verified custom-domain main commit.
- The candidate build and all eighteen Chromium/Firefox/WebKit checks passed, followed by candidate hash/file-set verification. Wide and narrow browser viewport connection pages were visually inspected.
- Candidate tests confirm GET 404 and POST 405 before a Cloud API exists, explicitly mocked unavailable/success responses through the published binary client, and no automatic requests or retries. These do not validate a C# container.
- Listing the live suite confirms fifteen static/idle-page tests and excludes the three browser wire-fixture cases. This feature has not been deployed; its public API and Native AOT evidence remain pending Cloud implementation.

## Outside scope

- The gRPC-Web tests use binary wire fixtures; they do not call a real C# Cloud service.
- Complete Account/Chat flows, login, payment, AI and commercial product acceptance are outside this Hello bootstrap.
- Visual Studio interactive startup requires the appropriate local IDE; npm is the authoritative validated build path.
