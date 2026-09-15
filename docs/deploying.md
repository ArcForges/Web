# Cloudflare delivery

## Current setup

The public Hello site uses **Workers Static Assets**, with no Worker script, paid AI binding, request-time SSR or C# host. Wrangler deploys `artifacts/candidate/assets` using the configuration inside that same verified candidate. The worker name is `arcforges-web`; it does not alter the AI worker or any existing domain. `workers_dev` is enabled and preview URLs are disabled.

The owner has deferred automatic deployment credentials. GitHub's `cloudflare` environment exists with a deployment branch rule for `main` and its account ID variable. No real deployment or live verification has been performed for this bootstrap. Local and PR tests remain usable.

## One-time owner setup

When ready to enable delivery:

1. In the selected Cloudflare account, ensure a `workers.dev` account subdomain exists. An existing Workers deployment normally means this is already configured.
2. Create or reuse a Cloudflare API token restricted to that account with **Account / Workers Scripts / Edit**. Static assets need no Workers AI, DNS or Zone permissions. Save the token privately.
3. Open [Web environment settings](https://github.com/ArcForges/Web/settings/environments), enter **cloudflare**, and add **Environment secret** `CLOUDFLARE_API_TOKEN`. Do not put it in variables, code, chat or a `VITE_` value.
4. Check **Environment variable** `CLOUDFLARE_ACCOUNT_ID` contains the account's 32-character ID. This is already configured. Environments are repository-specific; the AI repository's environment is not automatically inherited.
5. Keep deployments restricted to `main`. No enable switch or human approval is required by this workflow. Merge the tested PR when ready, or rerun the failed deployment after configuring the secret if the source is still current main.

The token stays in CI. The browser never receives Cloudflare management credentials. CI uses this API token independently of any developer's `wrangler login` browser session. Credential rotation consists of replacing the environment secret; do not commit an example credential.

## Automatic sequence

1. Linux and Windows restore the exact lock and validate source. Dependency auditing, history secret scanning and CodeQL run; dependency review additionally runs for PRs.
2. Linux builds one static candidate, generates CSP hashes from its actual inline scripts, adds upstream notices and full/runtime CycloneDX SBOMs, and seals the complete candidate file set with SHA-256.
3. Chromium, Firefox and WebKit test that candidate served through local Wrangler, including accessibility, no-JavaScript behavior, local greeting, CSP/cache behavior and missing-path 404s.
4. `Verify` requires all applicable checks. On a `push` to `main`, the deployment job downloads the exact candidate artifact by its ID and verifies source/hashes. It never rebuilds it. A skipped PR-only dependency review cannot skip deployment.
5. Missing account/token configuration is an explicit failure. The job checks that the source is still current main, serializes deployment, and deploys with Wrangler.
6. Bounded, read-only HTTPS polling waits for the expected `__build.json`; then every public candidate file is compared by hash, security/cache headers are checked, and missing routes must return the real 404. A failed check never automatically creates another deployment.
7. Only successful live verification creates a GitHub prerelease containing the original candidate archive and deployment evidence. Versions are `0.1.0-ci.<run_number>.<run_attempt>`; reruns are distinct. These are preview releases, not an assertion that the complete product is implemented.

PR, schedule and manually dispatched workflows validate but do not deploy. Main pushes deploy automatically once the credential is present. No second Cloudflare Git integration is required; enabling one would create an independent deployment path that bypasses this candidate process.

## Serving and security

All content is static and public. HTML revalidates, hashed `/assets/*` files are immutable, and `__build.json` is not cached. The generated `_headers` file applies strict script hashes, self-only connections, `nosniff`, a restrictive permissions policy, framing protection and no-index headers. There is no generic SPA fallback and no API forwarding.

The source link identifies the candidate's source revision. Public content contains no secret; open-source code does not grant deployment authority. Adding a backend/AI proxy in future requires its own authentication, authorization, quota and abuse controls. This static preview has no paid model invocation path.

Use the emitted workers.dev URL initially. Custom domains, CORS for C# Cloud, auth cookies, API origins and production profile separation require explicit configuration later; they are not silently provisioned here. Self-only CSP must be intentionally updated when real backend calls are introduced.

## Failure and recovery

- **Source or browser failure:** fix the failing check in a PR. Nothing deploys.
- **Missing/invalid token:** configure the environment, then rerun. No successful release is created merely because the build passed.
- **Superseded run:** do not rerun an old main commit. Use the newest tested main run; the guard prevents stale queued runs from rolling back production.
- **Wrangler or live-check failure:** inspect `cloudflare-evidence-*`, the job log and the Workers dashboard. A deployment may have completed even when later validation failed. Retrying propagation only issues reads. Do not infer a successful release from upload completion.
- **Rollback:** prefer reverting the offending source in a PR so current main produces a newly verified release. For an urgent operator rollback, select the prior known-good deployment/version in Cloudflare's deployment history, verify its public `__build.json` and pages, and promptly reconcile main. The automatic workflow deliberately cannot deploy stale source. Keep the previous verified candidate archive for recovery; do not reconstruct it from mutable dependencies.

Candidate artifacts remain for 30 days, browser evidence for 14 days and deployment evidence for 90 days. Verified GitHub prereleases preserve their candidate archive beyond those artifact windows. The first real main deployment must validate API-token permissions, account subdomain, remote propagation and public HTTP behavior; local success cannot prove those account-specific conditions.

References: [static assets](https://developers.cloudflare.com/workers/static-assets/get-started/), [headers](https://developers.cloudflare.com/workers/static-assets/headers/), [routing](https://developers.cloudflare.com/workers/static-assets/routing/advanced/html-handling/).
