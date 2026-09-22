# Development

## Toolchain and commands

Install the exact Node version in `.node-version`, which includes npm 11.19.0. `fnm use` or an equivalent version manager can select it. Run every command from the repository root. `npm ci --ignore-scripts` restores the reviewed lock without dependency lifecycle scripts; explicit build/test commands still run normally.

| Command                    | Purpose                                                                                |
| -------------------------- | -------------------------------------------------------------------------------------- |
| `npm run hooks`            | Enable hooks only in the current Git worktree                                          |
| `npm run dev`              | React Router development server, loopback only                                         |
| `npm run format`           | Format source/docs/workflow files                                                      |
| `npm run check`            | Toolchain/lock policy, formatting, Biome, strict TypeScript, unit/component/wire tests |
| `npm run build`            | Build once, generate notices/SBOMs/CSP/provenance and seal a candidate                 |
| `npm run verify:candidate` | Verify source, complete file set, SHA-256 hashes and delivery configuration            |
| `npm run preview`          | Run the sealed assets and redirect Worker using local Wrangler                         |
| `npm run test:e2e`         | Optional local browser checks using existing installations; never CI                   |

Hooks check whitespace only. They never repeat builds/tests at commit or push. Git enables `extensions.worktreeConfig` so the hook path does not alter the main checkout's configuration. The `.githooks` shell files only dispatch the whitespace check. On Windows they run through Git for Windows.

The protected `main` branch requires both `Verify` and the separate GitHub `CodeQL` scanning result. A successful CodeQL analysis job alone does not imply that it found no blocking security alerts.

Open `win.slnx` with Visual Studio's JavaScript project support, installed Chrome, and the pinned Node/npm on its PATH. Select `ArcForges.Web` as the startup project and `ArcForges Web (Chrome)` as its launch profile. The solution explicitly enables Build and local IDE Deploy for the esproj; that local Deploy starts the development profile and never publishes to Cloudflare.

For an explicit locked restore and solution build on Windows:

```sh
dotnet msbuild win.slnx -t:Restore
dotnet msbuild win.slnx -t:Build -p:Configuration=Release
node tooling/ide.ts
```

Restore runs root `npm ci --ignore-scripts`. Build delegates to the same root `npm run build` used by portable CI and never installs dependencies; IDE design-time evaluation does not restore. CI evaluates the IDE declarations without another solution restore/build. The explicit local IDE diagnostic remains available when its dispatch behavior changes.

F5 uses `.vscode/launch.json` and the same root `npm run dev` as the CLI, serving `http://127.0.0.1:5173`. The server uses strict port selection: stop an existing development session before starting another; an occupied port fails rather than moving the IDE to an unrelated service. Stopping browser debugging may leave the development server running; stop that session's terminal when finished. No C# host or business proxy is started. Portable Linux/macOS development uses the root npm commands directly, without Visual Studio or MSBuild. The `.esproj` produces no .NET application or server.

## Application boundaries

- React Router framework mode is configured with `ssr: false` and build-time prerendering of `/`, `/hello` and `/cloud-hello`. A temporary server build is used by the framework during prerendering and excluded from the candidate. Its generated SPA fallback is also excluded.
- The private importless Worker redirects the exact `www.arcforges.com` hostname to the HTTPS apex with HTTP 308, preserving path and query, before static routing. Other requests use the `ASSETS` binding unchanged. `run_worker_first: true` includes navigations, so the page's same-origin API client starts on the canonical host. This is edge request routing, not SSR or an API proxy.
- Initial text and links work without JavaScript. Greeting controls remain disabled until hydration, cannot submit names as native form query parameters and are covered by `form-action 'none'`. Names are trimmed, limited to 80 Unicode code points and reject control characters. React renders the greeting as text.
- The local example serializes real `@arcforges/proto` messages. The separate server connection page uses `@arcforges/api-client` against same-origin `/api`, with the published Hello contract and an explicitly unavailable state when the service cannot be reached. Browser protobuf fixtures are separate from live verification. The client's `redirect: "error"` remains intact; reload a www page opened before the canonical redirect deployment. See [Cloud Hello](cloud-hello.md) for the exact endpoint and evidence boundary.
- No AI, database, analytics, service worker, privileged proxy or user-data storage is included. The production custom domain is `arcforges.com`. React Router's scroll restoration may store scroll positions in session storage.
- Future Account/Chat/operator/status delivery profiles remain separate work. Shared components live in `packages/ui`; profiles must not import business source from adjacent repositories.

## Dependencies

Use exact direct versions and commit the root `package-lock.json` after intentional dependency changes. Keep React/DOM/types and Router packages aligned. Keep protobuf/Connect versions compatible with the published Contracts packages. Do not replace pinned Contracts versions with `latest`, Git URLs, local paths or floating ranges. Update the pins and lock through a reviewed PR; Dependabot groups related updates and the full pipeline validates them.

Lock provenance checks require npm registry URLs and SHA-512 integrity for downloaded artifacts. npm's `inBundle` entries are extracted from an enclosing package tarball and may omit their own URL/hash; the check traces them to that verified artifact, including nested bundles. Orphaned entries and workspace links cannot substitute for a registry artifact. Keep this metadata when Dependabot adds it; see the [npm lockfile format](https://docs.npmjs.com/cli/v11/configuring-npm/package-lock-json/#packages).

Node runtime and `@types/node` stay on major 24. Updating Node also requires `.node-version`, engines and `packageManager` to remain consistent. Use existing compatible Playwright browsers only for an affected local diagnostic; no browser installation is part of CI.

If a dependency update changes license packaging, review `third-party/README.md` and update the generated-notice handling. Build failures for missing licenses are deliberate rather than silently discarding notices.

## Evidence

Browser reports, traces for failures and responsive screenshots are under `playwright-report` and `test-results`. They are local diagnostic outputs and are not uploaded or required by CI. The candidate contains no tests, source maps, SSR runtime, `.env` files or npm dependencies directory. Do not commit any generated artifacts.

Redirect coverage uses targeted offline tests for the host boundary, HTTPS destination, path/query retention, 308 behavior and untouched asset fallback. Existing local Wrangler may be used once to inspect the affected navigation redirect and apex asset response; this is local edge-runtime evidence, not a real Cloud service test. Do not add browser/live CI or install toolchains for this check. Main deployment success proves provider completion, not a post-merge browser or RPC test.

Local Wrangler persistence is explicitly placed at the repository's ignored `.wrangler/state`, outside the immutable candidate. Browser tests are explicit local opt-in; no repeated verification follows them automatically.

`manifest.json` records a full source SHA, version, dirty flag and hash of every other candidate file. Local modified builds are useful for testing but rejected by automatic deployment. `__build.json` exposes only the non-sensitive source/version identity. A successful build is not evidence of a real Cloudflare deployment or working C# business API.

## Provenance changes

Follow [the provenance process](provenance.md) before introducing reused or generated material. `npm run check` includes offline source-policy/resolver tests without rebuilding a candidate. `test:artifact` is an explicit local packaging investigation, separate from normal checks. `npm run verify:candidate` validates the independent browser resource profile and complete legal/SBOM closure as well as the outer manifest.
