# ArcForges Web

React and TypeScript Web foundation for the ArcForges family. This first increment contains a public Hello World site, an interactive local greeting, shared UI, published Contracts consumption and an automated Cloudflare Workers Static Assets delivery pipeline.

It does not implement the planned Account/Chat application, authentication, payments or a C# backend. The greeting runs locally and sends no name to a server. Business authority remains in ArcForges Cloud.

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
npx --no-install playwright install chromium firefox webkit
npm run test:e2e
npm run preview
```

The preview serves the actual candidate through local Wrangler at `http://127.0.0.1:4173`. Stop it before running browser tests, which start their own instance. No Cloudflare login is required for these commands. On Linux, use `playwright install --with-deps` to install browser system dependencies.

## Layout

| Path                                | Purpose                                                               |
| ----------------------------------- | --------------------------------------------------------------------- |
| `apps/site`                         | Prerendered home and `/hello` pages                                   |
| `apps/app`                          | Documented boundary for the future Account/Chat profiles              |
| `packages/ui`                       | Shared components and Tailwind/CSS styles                             |
| `tooling`                           | TypeScript build, provenance, policy and Cloudflare delivery commands |
| `tests`                             | Unit, published SDK wire fixtures, browser and accessibility tests    |
| `artifacts/candidate`               | Ignored immutable delivery artifact, manifest and SBOMs               |
| `win.slnx` / `ArcForges.Web.esproj` | Optional Visual Studio JavaScript project                             |

Baseline: TypeScript **7.0.2**, React **19.3.0**, React Router **8.4.0**, Vite **8.3.0**, Tailwind **4.3.3**, Wrangler **4.132.0**. One committed npm lockfile covers the entire workspace. Contracts packages are pinned to **1.0.0-ci.25.1**; no submodules or adjacent source references are used.

## Delivery

PRs run source checks on Linux/Windows, dependency auditing/review, secret scanning, CodeQL, and three-browser tests of a single candidate. Every successful `main` push then deploys those exact candidate bytes to `arcforges-web.<account-subdomain>.workers.dev`, verifies them over HTTPS, and creates a GitHub prerelease with an automatically generated version. CI does not publish these private workspaces to npm.

**Deployment setup is currently deferred by the owner.** The repository's main-only `cloudflare` environment and account variable exist; its API token has not been configured. PR checks work without it. A main deployment without that secret fails with a setup message and creates no successful release. See [deployment setup and recovery](docs/deploying.md) before merging when a live release is wanted.

Workers Static Assets supports this static React build directly. Frameworks that need request-time server code require a Workers-compatible adapter/runtime. This setup does not host C# or provide an API proxy. See the [official React guide](https://developers.cloudflare.com/workers/framework-guides/web-apps/react/) and [static assets guide](https://developers.cloudflare.com/workers/static-assets/get-started/).

## Contribute

Read [development](docs/development.md), [contributing](CONTRIBUTING.md), [security](SECURITY.md), [validation](docs/validation.md), and the [bootstrap plan](docs/bootstrap-plan.md).

The existing repository license is **AGPL-3.0-only**; see [LICENSE](LICENSE). Upstream Contracts and other dependencies retain their own licenses. The built site exposes the license, source link and generated third-party notices. See [third-party notices](THIRD_PARTY_NOTICES.md).
