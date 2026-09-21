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
| `npm run preview`          | Run the sealed static candidate using local Wrangler                                   |
| `npm run test:e2e`         | Start local Wrangler; run Chromium, Firefox, WebKit, axe and delivery checks           |

Hooks run `npm run check` at commit and push. CI repeats checks independently. Git enables `extensions.worktreeConfig` so the hook path does not alter the main checkout's configuration. The `.githooks` shell files only dispatch the TypeScript/npm checks. On Windows they run through Git for Windows.

The protected `main` branch requires both `Verify` and the separate GitHub `CodeQL` scanning result. A successful CodeQL analysis job alone does not imply that it found no blocking security alerts.

Open `win.slnx` with Visual Studio's JavaScript project support, installed Chrome, and the pinned Node/npm on its PATH. Select `ArcForges.Web` as the startup project and `ArcForges Web (Chrome)` as its launch profile. The solution explicitly enables Build and local IDE Deploy for the esproj; that local Deploy starts the development profile and never publishes to Cloudflare.

For an explicit locked restore and solution build on Windows:

```sh
dotnet msbuild win.slnx -t:Restore
dotnet msbuild win.slnx -t:Build -p:Configuration=Release
node tooling/ide.ts
```

Restore runs root `npm ci --ignore-scripts`. Build delegates to the same root `npm run build` used by portable CI and never installs dependencies; IDE design-time evaluation does not restore. The verifier requires actual npm dispatch and a verified candidate, so a successful solution that skips the esproj fails verification. It also checks that neither restore nor build changes the committed lock, and Build leaves installed dependency metadata untouched.

F5 uses `.vscode/launch.json` and the same root `npm run dev` as the CLI, serving `http://127.0.0.1:5173`. The server uses strict port selection: stop an existing development session before starting another; an occupied port fails rather than moving the IDE to an unrelated service. Stopping browser debugging may leave the development server running; stop that session's terminal when finished. No C# host or business proxy is started. Portable Linux/macOS development uses the root npm commands directly, without Visual Studio or MSBuild. The `.esproj` produces no .NET application or server.

## Application boundaries

- React Router framework mode is configured with `ssr: false` and build-time prerendering of `/`, `/hello` and `/cloud-hello`. A temporary server build is used by the framework during prerendering and excluded from the candidate. Its generated SPA fallback is also excluded.
- Initial text and links work without JavaScript. Greeting controls remain disabled until hydration, cannot submit names as native form query parameters and are covered by `form-action 'none'`. Names are trimmed, limited to 80 Unicode code points and reject control characters. React renders the greeting as text.
- The local example serializes real `@arcforges/proto` messages. The separate server connection page uses `@arcforges/api-client` against same-origin `/api`, with the published Hello contract and an explicitly unavailable state until Cloud is deployed. Browser protobuf fixtures are separate from live verification. See [Cloud Hello](cloud-hello.md) for the exact endpoint and remaining backend acceptance.
- No AI, database, analytics, service worker, privileged proxy or user-data storage is included. The production custom domain is `arcforges.com`. React Router's scroll restoration may store scroll positions in session storage.
- Future Account/Chat/operator/status delivery profiles remain separate work. Shared components live in `packages/ui`; profiles must not import business source from adjacent repositories.

## Dependencies

Use exact direct versions and commit the root `package-lock.json` after intentional dependency changes. Keep React/DOM/types and Router packages aligned. Keep protobuf/Connect versions compatible with the published Contracts packages. Do not replace pinned Contracts versions with `latest`, Git URLs, local paths or floating ranges. Update the pins and lock through a reviewed PR; Dependabot groups related updates and the full pipeline validates them.

Lock provenance checks require npm registry URLs and SHA-512 integrity for downloaded artifacts. npm's `inBundle` entries are extracted from an enclosing package tarball and may omit their own URL/hash; the check traces them to that verified artifact, including nested bundles. Orphaned entries and workspace links cannot substitute for a registry artifact. Keep this metadata when Dependabot adds it; see the [npm lockfile format](https://docs.npmjs.com/cli/v11/configuring-npm/package-lock-json/#packages).

Node runtime and `@types/node` stay on major 24. Updating Node also requires `.node-version`, engines and `packageManager` to remain consistent. Keep Playwright browser binaries aligned with its package version.

If a dependency update changes license packaging, review `third-party/README.md` and update the generated-notice handling. Build failures for missing licenses are deliberate rather than silently discarding notices.

## Evidence

Browser reports, traces for failures and responsive screenshots are under `playwright-report` and `test-results`. CI uploads them separately from the deployable candidate. The candidate contains no tests, source maps, SSR runtime, `.env` files or npm dependencies directory. Do not commit any generated artifacts.

Local Wrangler persistence is explicitly placed at the repository's ignored `.wrangler/state`, outside the immutable candidate. Verification runs again after browser tests to catch any unexpected tool writes into that artifact.

`manifest.json` records a full source SHA, version, dirty flag and hash of every other candidate file. Local modified builds are useful for testing but rejected by automatic deployment. `__build.json` exposes only the non-sensitive source/version identity. A successful build is not evidence of a real Cloudflare deployment or working C# business API.

## Provenance changes

Follow [the provenance process](provenance.md) before introducing reused or generated material. `npm run check` includes `npm run test:provenance`, which first builds an actual candidate and then runs source-policy and resealed-candidate failure scenarios. `npm run verify:candidate` validates the independent browser resource profile and complete legal/SBOM closure as well as the outer manifest.
