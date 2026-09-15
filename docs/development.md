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

Open `win.slnx` with a Visual Studio release supporting the JavaScript project SDK for optional IDE navigation and startup. Install dependencies explicitly first. The npm commands are the authoritative cross-platform build; no .NET application or server is produced by the `.esproj`.

## Application boundaries

- React Router framework mode is configured with `ssr: false` and build-time prerendering of `/` and `/hello`. A temporary server build is used by the framework during prerendering and excluded from the candidate. Its generated SPA fallback is also excluded.
- Initial text and links work without JavaScript. Greeting controls remain disabled until hydration, cannot submit names as native form query parameters and are covered by `form-action 'none'`. Names are trimmed, limited to 80 Unicode code points and reject control characters. React renders the greeting as text.
- The example serializes real `@arcforges/proto` messages locally. `@arcforges/api-client` is tested with binary gRPC-Web success and failure fixtures. No Cloud endpoint or credential is invented. Real Cloud integration requires its published API, session and CORS decisions.
- No AI, database, analytics, service worker, custom domain, privileged proxy or user-data storage is included. React Router's scroll restoration may store scroll positions in session storage.
- Future Account/Chat/operator/status delivery profiles remain separate work. Shared components live in `packages/ui`; profiles must not import business source from adjacent repositories.

## Dependencies

Use exact direct versions and commit the root `package-lock.json` after intentional dependency changes. Keep React/DOM/types and Router packages aligned. Keep protobuf/Connect versions compatible with the published Contracts packages. Do not replace pinned Contracts versions with `latest`, Git URLs, local paths or floating ranges. Update the pins and lock through a reviewed PR; Dependabot groups related updates and the full pipeline validates them.

Node runtime and `@types/node` stay on major 24. Updating Node also requires `.node-version`, engines and `packageManager` to remain consistent. Keep Playwright browser binaries aligned with its package version.

If a dependency update changes license packaging, review `third-party/README.md` and update the generated-notice handling. Build failures for missing licenses are deliberate rather than silently discarding notices.

## Evidence

Browser reports, traces for failures and responsive screenshots are under `playwright-report` and `test-results`. CI uploads them separately from the deployable candidate. The candidate contains no tests, source maps, SSR runtime, `.env` files or npm dependencies directory. Do not commit any generated artifacts.

Local Wrangler persistence is explicitly placed at the repository's ignored `.wrangler/state`, outside the immutable candidate. Verification runs again after browser tests to catch any unexpected tool writes into that artifact.

`manifest.json` records a full source SHA, version, dirty flag and hash of every other candidate file. Local modified builds are useful for testing but rejected by automatic deployment. `__build.json` exposes only the non-sensitive source/version identity. A successful build is not evidence of a real Cloudflare deployment or working C# business API.
