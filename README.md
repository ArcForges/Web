# ArcForges Web

C#-first Web foundation for the ArcForges family (P2-021). The public Site is a static C# generator and publishes no JavaScript. The Dashboard is a standalone Blazor WebAssembly application. The Cloudflare Worker that serves the static assets is a thin TypeScript adapter.

The public Site serves `/`, `/hello/` and `/cloud-hello/`. Its pages, texts, element structure, stylesheet bytes and security headers match the React prerender they replace, as recorded in [the parity record](docs/web-40-site-parity.md). Its interactive greeting and server-connection checks have no static equivalent; their placement is an open decision recorded there.

The Dashboard serves Overview, Devices and device details for one configured Center origin. It uses the Center browser session for device reads and passkey-confirmed revocation. It reports an empty Run list only for a successfully queried, authorized Sandbox. The existing digest-named bundle keeps its historical Account and Chat archive labels ([layout and rules](docs/profile-bundle.md)). The Operations profile is a skeleton on its own origin.

## Flagship role

Web is an active repository of the ArcForges flagship ([AD-0001](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/decisions/AD-0001-flagship-pivot.md)). It provides the public ArcForges site (product pages and downloads) and the `account` (adapted) and `fleet` (new) profiles, and its Operations profile becomes the operator console ([architecture 01 §5](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/architecture/01-system-overview.md#5-repository-and-project-map), [reuse classification](https://github.com/ArcForges/ArcForges-Design/blob/main/docs/reuse/inventory-and-classification.md#4-web)). The Dashboard replaces the old Account and Chat probe navigation. The historical probes remain as test fixtures. Work is scheduled in the [ArcForges/Plan delivery graph](https://github.com/ArcForges/Plan/blob/main/delivery-graph.json) and delivered serially under its [execution policy](https://github.com/ArcForges/Plan/blob/main/execution-policy.md); flagship publication and production activation are tracked in AFV2-PROD-01.

## Toolchain

- .NET SDK **11.0.100** (`global.json`), with central NuGet package management and locked restores.
- Node.js **26.13.1** and npm **12.2.2** (`.node-version`, `packageManager`), for the Worker adapter, wrangler and the reviewed policy checks.
- Exact versions throughout: Blazor WebAssembly standalone with `RunAOTCompilation=false`, browser Contracts packages at **1.0.0-ci.481.1**, wrangler **4.162.0**, TypeScript **7.0.2**.

Start with [development](docs/development.md) for the commands and the solution layout.

## Layout

| Path | Role |
| --- | --- |
| `src/ArcForges.Web.Site` | Static public Site generator (first-party Razor `HtmlRenderer`) |
| `src/ArcForges.Web.Ui` | Razor class library: shared components and the profile CSP helper |
| `src/ArcForges.Web.App` | Blazor WebAssembly Dashboard |
| `src/ArcForges.Web.Operations` | Operations profile skeleton |
| `tools/ArcForges.Web.Tooling` | C# candidate, Site, profile bundle and budget tool |
| `tests/` | xUnit, bUnit and policy suites; local opt-in browser checks |
| `worker/index.ts` | Canonical-host Worker adapter (`www` to apex redirect, otherwise `env.ASSETS`) |
| `tooling/` | Node tooling: Worker emission, identity, the Cloudflare deployment wrapper and the policy checks |
| `eng/` | Reviewed policy, dependency receipts and provenance records |

## Delivery

Pull requests run the locked restores, formatting, builds, tests, publishes, policy checks, dependency audit, secret scan and the candidate build: external pull requests in hosted CI (with dependency review and CodeQL), and every pull request on the owner's two hosts after the push, a Windows leg on native Windows and a Linux leg on native Linux whose statuses `main` requires (`eng/verify.py run-and-report`, [AGENTS.md](AGENTS.md)). On a main push, CI builds the C# static Site and the profiles once, seals the Site as a candidate and verifies it before upload, without re-running the pull-request checks. The main-push workflow keeps the candidate as a GitHub Actions artifact. Its Cloudflare deployment job and coupled public prerelease are disabled pending AFV2-PROD-01. Previews and `workers.dev` are disabled. Local runs of the candidate use `npm run preview` with the local Wrangler.

Browser checks, live-service checks, device and macOS checks are local opt-in only. Hosted CI runs no browser, device, live-service or inference job.
