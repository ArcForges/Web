# Cloudflare delivery

## Current setup

The public Hello site uses **Workers Static Assets**, with no Worker script, paid AI binding, request-time SSR or C# host. Wrangler deploys `artifacts/candidate/assets` using the configuration inside that same verified candidate. The Worker is `arcforges-web`, serving `https://arcforges.com`. `workers_dev` and preview URLs are explicitly disabled. The existing domain binding is managed in Cloudflare; the configuration intentionally omits `routes` and does not create or replace DNS/domain mappings.

GitHub's `cloudflare` environment contains the account variable and API token, with deployments restricted to `main`. The first real deployment passed on 2026-09-15, and the owner subsequently attached the custom domain. See [validation evidence](validation.md) for the distinction between that bootstrap deployment and this domain configuration.

## One-time owner setup

When ready to enable delivery:

1. In Cloudflare, select Worker `arcforges-web` > **Settings > Domains & Routes > Add > Custom Domain**, enter `arcforges.com`, and confirm. Cloudflare provisions the DNS record and HTTPS certificate. This binding is already configured for the current account. A fresh account needs the Worker and an active zone first; this established production pipeline refuses to disable the default address before the custom-domain mapping exists.
2. Create or reuse a Cloudflare API token restricted to that account with **Account / Workers Scripts / Edit**. Static assets need no Workers AI, DNS or Zone permissions. Save the token privately.
3. Open [Web environment settings](https://github.com/ArcForges/Web/settings/environments), enter **cloudflare**, and add **Environment secret** `CLOUDFLARE_API_TOKEN`. Do not put it in variables, code, chat or a `VITE_` value.
4. Check **Environment variable** `CLOUDFLARE_ACCOUNT_ID` contains the account's 32-character ID. Both this and the deployment secret are configured. Environments are repository-specific; the AI repository's environment is not automatically inherited.
5. Keep deployments restricted to `main`. No enable switch or human approval is required by this workflow. Merge the tested PR when ready, or rerun the failed deployment after configuring the secret if the source is still current main.

The token stays in CI. The browser never receives Cloudflare management credentials. CI uses this API token independently of any developer's `wrangler login` browser session. Credential rotation consists of replacing the environment secret; do not commit an example credential.

## Automatic sequence

1. Linux and Windows restore the exact lock and validate source. Dependency auditing, history secret scanning and CodeQL run; dependency review additionally runs for PRs.
2. Linux builds one static candidate, generates CSP hashes from its actual inline scripts, adds upstream notices and full/runtime CycloneDX SBOMs, and seals the complete candidate file set with SHA-256.
3. Chromium, Firefox and WebKit test that candidate served through local Wrangler, including accessibility, no-JavaScript behavior, local greeting, CSP/cache behavior and missing-path 404s.
4. `Verify` requires all applicable checks. On a `push` to `main`, the deployment job downloads the exact candidate artifact by its ID and verifies source/hashes. It never rebuilds it. A skipped PR-only dependency review cannot skip deployment.
5. Missing account/token configuration is an explicit failure. The job checks that the source is still current main and that `arcforges.com` belongs to `arcforges-web` in production, serializes deployment, and deploys with Wrangler. Checking the mapping only needs the existing Workers Scripts permission.
6. Bounded, read-only HTTPS polling waits for the expected `https://arcforges.com/__build.json`; then every public candidate file is compared by hash, security/cache headers are checked, and missing routes must return the real 404. A shared two-minute deadline permits those responses to converge after the identity becomes available; permanent differences fail with the last diagnostic. HTML requests explicitly accept HTML. The existing Chromium, Firefox and WebKit suite then runs against the real domain to check browser-visible edge behavior. A failed check never automatically creates another deployment.
7. Only successful file and browser verification creates a GitHub prerelease containing the original candidate archive and deployment evidence. Versions are `0.1.0-ci.<run_number>.<run_attempt>`; reruns are distinct. These are preview releases, not an assertion that the complete product is implemented.

PR, schedule and manually dispatched workflows validate but do not deploy. Main pushes deploy automatically once the credential is present. No second Cloudflare Git integration is required; enabling one would create an independent deployment path that bypasses this candidate process.

## Serving and security

All content is static and public. HTML revalidates, hashed `/assets/*` files are immutable, and `__build.json` is not cached. Responses use `no-transform` to preserve the reviewed bytes and prevent automatic edge analytics injection. The generated `_headers` file applies strict script hashes, self-only connections, `nosniff`, a restrictive permissions policy, framing protection and no-index headers. There is no generic SPA fallback and no API forwarding.

The source link identifies the candidate's source revision. Public content contains no secret; open-source code does not grant deployment authority. Adding a backend/AI proxy in future requires its own authentication, authorization, quota and abuse controls. This static preview has no paid model invocation path.

Use `https://arcforges.com`. The former Workers subdomain was another public entry to the same deployment, not a staging environment. Do not re-enable it merely to run CI. Disabling it in the dashboard alone is insufficient if a later Wrangler configuration enables it; the source configuration is authoritative. The [Cloud Hello boundary](cloud-hello.md) prepares an anonymous same-origin API call. Authenticated sessions and production profile separation remain future integration work. Live Web delivery checks only Web-owned paths; it must not require its HTML 404 at `/api/*` once Cloud owns that route.

## Failure and recovery

- **Source or browser failure:** fix the failing check in a PR. Nothing deploys.
- **Missing/invalid token:** configure the environment, then rerun. No successful release is created merely because the build passed.
- **Missing custom-domain mapping:** restore `arcforges.com` on `arcforges-web` in Cloudflare before rerunning. CI checks ownership before deploying or disabling the default address.
- **Superseded run:** do not rerun an old main commit. Use the newest tested main run; the guard prevents stale queued runs from rolling back production.
- **Wrangler or live-check failure:** inspect `cloudflare-evidence-*`, the job log and the Workers dashboard. A deployment may have completed even when later validation failed. Retrying propagation only issues reads. Do not infer a successful release from upload completion.
- **Rollback:** prefer reverting the offending source in a PR so current main produces a newly verified release. For an urgent operator rollback, select the prior known-good deployment/version in Cloudflare's deployment history, verify its public `__build.json` and pages, and promptly reconcile main. The automatic workflow deliberately cannot deploy stale source. Keep the previous verified candidate archive for recovery; do not reconstruct it from mutable dependencies.

Candidate artifacts remain for 30 days, local browser evidence for 14 days and deployment evidence (including live browser reports) for 90 days. Verified GitHub prereleases preserve their candidate archive beyond those artifact windows. Each main deployment validates the actual domain mapping, remote propagation and public HTTP/browser behavior; local success cannot prove those account-specific conditions.

References: [static assets](https://developers.cloudflare.com/workers/static-assets/get-started/), [headers](https://developers.cloudflare.com/workers/static-assets/headers/), [routing](https://developers.cloudflare.com/workers/static-assets/routing/advanced/html-handling/).
