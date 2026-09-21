# Custom domain delivery

Historical implementation plan. Its former CI/runtime acceptance requirements are superseded by [the current validation policy](validation-policy.md). Retained results are historical, not instructions to repeat them.

## Scope and observed issues

The first main deployment, `485150f00cbe9491e02f22a9156f9ce667e9f6dd`, passed real delivery checks at the Workers subdomain. The owner then attached `arcforges.com` to `arcforges-web`. Both hosts serve version `0.1.0-ci.5.1`.

The remaining issues are bounded:

1. Delivery and live checks still depend on the public Workers subdomain. Disabling that route in the dashboard alone would break verification and the next deployment would re-enable it.
2. Browser requests to the custom domain receive an automatically injected Cloudflare Web Analytics script. The existing CSP correctly blocks it. Generic HTTP requests do not receive the same injection, so their hashes alone miss this browser-visible difference.
3. Operational documentation still says that the deployment credential is deferred.

## Implementation order

1. Keep the existing custom-domain binding managed in Cloudflare. Before deploying, read the account's Worker domain mappings and require `arcforges.com` to belong to this Worker's production environment. Do not create DNS records or alter other domain mappings.
2. Set `workers_dev: false`, retain `preview_urls: false`, and require this in candidate verification. Use only `https://arcforges.com` for live checks.
3. Add `no-transform` to public cache policies while preserving HTML revalidation and immutable asset caching. This prevents edge HTML injection without widening CSP. Fetch HTML as HTML in remote byte verification.
4. Run the existing browser suite against the real domain after delivery, as well as against the candidate before deployment. Create a release only after both remote byte and browser checks pass.
5. Update operational setup/recovery instructions and record actual evidence.

## Closure

- Local source checks, immutable candidate verification and three-browser candidate tests pass.
- PR CI passes without Cloudflare credentials.
- A main deployment validates the existing domain binding and disables the Workers subdomain.
- HTTPS at `arcforges.com` serves the candidate identity and exact public files, with the required headers and real 404 responses; production browser interaction passes without the injected beacon.
- Report real deployment evidence separately from local tests. Cloud/C# Containers and the future Hello API are a subsequent worktree, after this delivery step is verified.

References: [Workers subdomain configuration](https://developers.cloudflare.com/workers/configuration/routing/workers-dev/), [Web Analytics and no-transform](https://developers.cloudflare.com/web-analytics/get-started/).

## Targeted live correction

Main run `35032047709` deployed the custom-domain configuration, but its immediate live check saw a response without `no-transform` after the new identity was already available. Without another deployment, every public file then returned the expected headers and all twelve real browser tests passed. Identity readiness alone therefore cannot establish that every edge response is ready.

The correction is limited to delivery verification: wait at most two minutes for the complete read-only file/header/404 check to converge, with a shared abort deadline and per-request limits. Preserve exact content checks, report the last failing path, never redeploy automatically, and still fail permanently incorrect content/configuration. Browser assertions remain mandatory and are not automatically retried.
