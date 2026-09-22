# Cloudflare delivery

## Current setup

The public Hello site uses **Workers Static Assets** and a small importless Worker that redirects `www.arcforges.com` to `https://arcforges.com` with HTTP 308 before serving assets. It preserves the path and query. There is no paid AI binding, request-time SSR or C# host in Web. Wrangler deploys `artifacts/candidate/assets` and the private `artifacts/candidate/worker/index.js` module from the same verified candidate, with `no_bundle: true`, an `ASSETS` binding and `run_worker_first: true`. The Worker is `arcforges-web`; `workers_dev` and preview URLs remain explicitly disabled.

Cloudflare manages the existing apex Custom Domain, `www.arcforges.com/*` Web route and proxied www DNS record. Keep all three. The configuration intentionally omits `routes`, does not recreate DNS/domain mappings and requires no new Cloudflare permissions for this redirect change. The separate Cloud Worker retains `arcforges.com/api/*`.

GitHub's `cloudflare` environment contains the account variable and API token, with deployments restricted to `main`. The first real deployment passed on 2026-09-15, and the owner subsequently attached the custom domain. See [validation evidence](validation.md) for the distinction between that bootstrap deployment and this domain configuration.

## One-time owner setup

When ready to enable delivery:

1. In Cloudflare, select **Workers & Pages > arcforges-web > Domains**. The current account already has the apex Custom Domain and the `www.arcforges.com/*` Route; retain both and the existing proxied www DNS record. A fresh account needs the Worker and an active zone first; use **Add Domain** for `arcforges.com`, and ensure its www entry also reaches this Worker. This established production pipeline refuses to disable the default address before the apex custom-domain mapping exists. Do not delete or recreate the working bindings to deploy the redirect.
2. Create or reuse a Cloudflare API token restricted to that account with **Account / Workers Scripts / Edit**. Deploying this Worker and static assets needs no Workers AI, DNS or Zone permissions because the bindings remain managed separately. Save the token privately.
3. Open [Web environment settings](https://github.com/ArcForges/Web/settings/environments), enter **cloudflare**, and add **Environment secret** `CLOUDFLARE_API_TOKEN`. Do not put it in variables, code, chat or a `VITE_` value.
4. Check **Environment variable** `CLOUDFLARE_ACCOUNT_ID` contains the account's 32-character ID. Both this and the deployment secret are configured. Environments are repository-specific; the AI repository's environment is not automatically inherited.
5. Keep deployments restricted to `main`. No enable switch or human approval is required by this workflow. Merge the tested PR when ready, or rerun the failed deployment after configuring the secret if the source is still current main.

The token stays in CI. The browser never receives Cloudflare management credentials. CI uses this API token independently of any developer's `wrangler login` browser session. Credential rotation consists of replacing the environment secret; do not commit an example credential.

## Automatic sequence

1. Linux and Windows restore the exact lock; Linux validates source and Windows evaluates IDE declarations. Dependency auditing, history secret scanning and CodeQL run; dependency review additionally runs for PRs.
2. Linux builds one candidate containing the prerendered assets and private redirect Worker, generates required CSP/notices/SBOM/provenance metadata and seals the candidate.
3. `Verify` requires applicable checks. Main deployment consumes the workflow candidate by artifact ID; its entry point performs one promotion integrity/identity check without rebuilding.
4. Deployment confirms the current main commit and intended domain mapping, then runs Wrangler. No DNS mapping is recreated.
5. Successful provider completion records `status: deployed` in `artifacts/deployment.json` and creates a prerelease with the original candidate archive and deployment record. It does not claim live HTTP or browser acceptance.

There are no browser installations, local/live E2E, public-file hash downloads, readiness polling or real Cloud calls in CI. The optional local diagnostic commands reject CI execution. See [validation policy](validation-policy.md).

PR, schedule and manually dispatched workflows validate but do not deploy. Main pushes deploy automatically once the credential is present. No second Cloudflare Git integration is required; enabling one would create an independent deployment path that bypasses this candidate process.

## Serving and security

Page content is static and public. The Worker runs before static asset routing, including navigation requests, and redirects the exact www hostname to the HTTPS apex. A 308 preserves the request method; it does not proxy or replay an API call. Other hosts pass the original request to `ASSETS.fetch`, retaining normal HTML, asset and 404 handling. The private Worker module is outside the public assets directory.

HTML revalidates, hashed `/assets/*` files are immutable, and `__build.json` is not cached. Static responses use `no-transform` to preserve the reviewed bytes and prevent automatic edge analytics injection. The generated `_headers` file applies strict script hashes, self-only connections, `nosniff`, a restrictive permissions policy, framing protection and no-index headers. There is no generic SPA fallback and no API forwarding.

The source link identifies the candidate's source revision. Public content contains no secret; open-source code does not grant deployment authority. Adding a backend/AI proxy in future requires its own authentication, authorization, quota and abuse controls. This static preview has no paid model invocation path.

Use `https://arcforges.com`. The former Workers subdomain was another public entry to the same deployment, not a staging environment. Do not re-enable it merely to run CI. Disabling it in the dashboard alone is insufficient if a later Wrangler configuration enables it; the source configuration is authoritative. The [Cloud Hello boundary](cloud-hello.md) prepares an anonymous same-origin API call. Authenticated sessions and production profile separation remain future integration work. Live Web delivery checks only Web-owned paths; it must not require its HTML 404 at `/api/*` once Cloud owns that route.

The www failure occurred because the page used its own origin for `/api`, while Cloud owned only the apex API route. DNS and the Web catchall could serve the page without routing its RPC to Cloud. Canonical navigation fixes that mismatch before the page starts: `https://www.arcforges.com/cloud-hello/?example=1` redirects to `https://arcforges.com/cloud-hello/?example=1`. A www page opened before this deployment must be reloaded. Its existing RPC client deliberately uses `redirect: "error"`; the redirect does not make that old page automatically follow API redirects or gain cross-origin access.

## Failure and recovery

- **Source or build failure:** fix the failing check in a PR. Nothing deploys.
- **Missing/invalid token:** configure the environment, then rerun. No successful release is created merely because the build passed.
- **Missing custom-domain mapping:** restore `arcforges.com` on `arcforges-web` in Cloudflare before rerunning. CI checks ownership before deploying or disabling the default address.
- **www page reaches Web but cannot connect:** retain the www DNS record and Web route. Confirm the redirect deployment result and reload the page so its address becomes the apex. This fix changes Web request handling; it does not require another DNS change or a second Cloud API route.
- **Superseded run:** do not rerun an old main commit. Use the newest tested main run; the guard prevents stale queued runs from rolling back production.
- **Wrangler failure:** inspect the deployment record, job log and provider status before a scoped retry. A failed confirmation can follow a successful upload. On a local network failure, report the exact operation and stop; do not change proxies or blindly rerun. Diagnose a CI failure before fixing its concrete cause and continuing the pipeline.
- **Rollback:** prefer reverting the offending source in a PR so current main produces a newly verified release. For an urgent operator rollback, select the prior known-good deployment/version in Cloudflare's deployment history and promptly reconcile main. Confirm the provider's deployment result without starting another public-download or runtime-test cycle. The automatic workflow deliberately cannot deploy stale source. Keep the previous verified candidate archive for recovery; do not reconstruct it from mutable dependencies.

Candidate artifacts remain for 30 days and deployment records for 90 days. Local browser evidence is not a CI artifact. Verified GitHub prereleases preserve their candidate archive beyond those artifact windows. Each main deployment confirms the intended domain mapping and provider operation. Public propagation and runtime/browser behavior are not tested by CI.

References: [static assets](https://developers.cloudflare.com/workers/static-assets/get-started/), [Worker asset binding](https://developers.cloudflare.com/workers/static-assets/binding/), [routing](https://developers.cloudflare.com/workers/static-assets/routing/worker-script/), [headers](https://developers.cloudflare.com/workers/static-assets/headers/).
