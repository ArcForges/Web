# ArcForges Web

React and TypeScript Web foundation for the ArcForges family. This first increment contains a public Hello World site, an interactive local greeting, shared UI, published Contracts consumption and an automated Cloudflare Workers Static Assets delivery pipeline.

It does not implement the planned Account/Chat application, authentication, payments or a C# backend. The `/hello/` greeting runs locally and sends no name to a server. A separate `/cloud-hello/` page uses the published gRPC-Web client to call the deployed Cloud container after the user clicks **Check connection**. Business authority remains in ArcForges Cloud.

## Start locally

Use **Node 24.21.0 / npm 11.19.0**, matching `.node-version` and `packageManager`.

```sh
npm ci --ignore-scripts
npm run hooks
npm run dev
```

Open the local URL printed by React Router. Public pages are rendered at build time; there is no request-time Node server in the deployed artifact.

```sh
npm run check
npm run build
npm run preview
```

The preview serves the actual candidate through local Wrangler at `http://127.0.0.1:4173`. Browser checks are explicit local opt-in only when existing browser binaries support the affected behavior; do not install browsers to expand validation. No Cloudflare login is required for preview.

## Layout

| Path                                | Purpose                                                               |
| ----------------------------------- | --------------------------------------------------------------------- |
| `apps/site`                         | Prerendered home, local greeting and server connection pages          |
| `apps/app`                          | Documented boundary for the future Account/Chat profiles              |
| `packages/ui`                       | Shared components and Tailwind/CSS styles                             |
| `tooling`                           | TypeScript build, provenance, policy and Cloudflare delivery commands |
| `tests`                             | Unit, published SDK wire fixtures, browser and accessibility tests    |
| `artifacts/candidate`               | Ignored immutable delivery artifact, manifest and SBOMs               |
| `win.slnx` / `ArcForges.Web.esproj` | Optional Visual Studio JavaScript project                             |

Baseline: TypeScript **7.0.2**, React **19.3.0**, React Router **8.4.0**, Vite **8.3.0**, Tailwind **4.3.3**, Wrangler **4.132.0**. One committed npm lockfile covers the entire workspace. Contracts packages are pinned to **1.0.0-ci.25.1**; no submodules or adjacent source references are used.

## Delivery

PRs run source/static/offline checks, Windows IDE declaration evaluation, security scans and one Linux static candidate build. Main deploys those same bytes and records provider completion in a GitHub prerelease. CI does not install browsers, run E2E, fetch public assets or call Cloud. See [validation policy](docs/validation-policy.md). Private workspaces are not published to npm; Workers subdomains and preview URLs remain disabled.

The main-only GitHub `cloudflare` environment contains the account variable and deployment secret. The custom-domain binding is managed in Cloudflare; CI verifies that it belongs to this Worker before deploying. PR checks remain credential-free. See [deployment setup and recovery](docs/deploying.md) and [evidence](docs/validation.md).

Workers Static Assets supports this static React build directly. Frameworks that need request-time server code require a Workers-compatible adapter/runtime. This setup does not host C# or provide an API proxy. The Cloud Worker owns the same-origin `/api/*` route and forwards to its Native AOT container; see the [Hello integration boundary and Cloud ownership](docs/cloud-hello.md). See also the [official React guide](https://developers.cloudflare.com/workers/framework-guides/web-apps/react/) and [static assets guide](https://developers.cloudflare.com/workers/static-assets/get-started/).

## Contribute

The build and CI enforce the [project licence declarations](docs/licence-boundary.md)
across every npm workspace and the JavaScript IDE adapter.
They also enforce [source and actual browser artifact provenance](docs/provenance.md),
including complete legal notices, immutable reuse records and the emitted browser SBOM.

Read [development](docs/development.md), [contributing](CONTRIBUTING.md), [security](SECURITY.md), [validation](docs/validation.md), and the [bootstrap plan](docs/bootstrap-plan.md).

The existing repository license is **AGPL-3.0-only**; see [LICENSE](LICENSE). Upstream Contracts and other dependencies retain their own licenses. The built site exposes the license, source link and generated third-party notices. See [third-party notices](THIRD_PARTY_NOTICES.md).

The sealed [build identity](docs/build-identity.md) is available at `/__build-info.json` for explicit support diagnostics; CI seals it from independent inputs without a browser runtime.
