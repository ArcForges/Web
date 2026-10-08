# Development

The Web product is C#-first (P2-021): the public Site is a static C# generator, the Account and Chat profiles are standalone Blazor WebAssembly applications, and the shared UI is a Razor class library. TypeScript is only the thin Cloudflare Worker adapter (`worker/index.ts`), and Node runs only wrangler and the reviewed policy checks.

## Toolchain

| Tool | Pin | Source |
| --- | --- | --- |
| .NET SDK | 10.0.401 (`global.json`, rollForward disabled) | `dotnet --version` |
| Node.js | 24.21.0 (`.node-version`, `engines`) | `node --version` |
| npm | 11.19.0 (`packageManager`) | `npm --version` |

Restores are locked: every project has a `packages.lock.json` and CI runs `dotnet restore --locked-mode`. NuGet versions are central in `Directory.Packages.props`. Every package is admitted by a receipt in `eng/policy/dependency-reviews/`.

## Commands

Run every command from the repository root. Use `npm ci --ignore-scripts` once after a checkout.

| Command | Purpose |
| --- | --- |
| `npm run hooks` | Enable the worktree-local hooks (`git diff --check` only) |
| `npm run check` | Dependency admission, reviewed policy, Worker typecheck and the Node test suites |
| `npm run policy` | Naming, licence, provenance, exact pins and lock provenance (requires the pinned Node) |
| `npm run build:worker` | Emit the Worker JavaScript from `worker/index.ts` by type stripping |
| `npm run verify:candidate` | Verify a sealed candidate through the C# verifier and the identity check |
| `npm run preview` | Run the sealed candidate with the local Wrangler dev server |
| `npm run deploy` | Deploy the sealed candidate (CI main-push only; needs the Cloudflare token) |

The C# product is built with the explicit project list in `.github/workflows/ci.yml` (`CSHARP_PROJECTS`, `CSHARP_TEST_PROJECTS`):

```text
dotnet restore <project> --locked-mode
dotnet build <project> --no-restore -c Release
dotnet test <test-project> --no-build -c Release
dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/publish/app
dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll site build --out artifacts/site --source-ref <40-hex>
dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll profiles budget --publish artifacts/publish/app --budgets eng/policy/profile-budgets.json
dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll candidate build|verify ...
dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll profiles bundle|verify ...
```

`tools/ArcForges.Web.Tooling` is the only build tool of the Web product. Its commands are listed in `Program.cs`; every other command is refused.

## Solution layout

| Path | Role |
| --- | --- |
| `src/ArcForges.Web.Site` | Static public Site generator (first-party Razor `HtmlRenderer`), its stylesheet and public files |
| `src/ArcForges.Web.Ui` | Razor class library (`ArcForges.Web.Ui`): shared components, the greeting and the CSP helper |
| `src/ArcForges.Web.App` | Blazor WebAssembly standalone Account and Chat profiles |
| `src/ArcForges.Web.Operations` | Operations profile skeleton on its own origin (no operator features yet) |
| `tools/ArcForges.Web.Tooling` | C# build tool: Site, candidate, profile bundle and budgets |
| `tests/ArcForges.Web.*.Tests` | xUnit and bUnit suites; `ArcForges.Web.Policy.Tests` holds the reviewed architecture, licence, naming and dependency rules |
| `tests/browser/ArcForges.Web.Browser.Tests` | Local opt-in Microsoft.Playwright for .NET checks (never CI) |
| `worker/index.ts` | Canonical-host Cloudflare Worker adapter |
| `eng/policy` | Reviewed policy: dependency receipts, licence inventory, profile budgets |
| `eng/provenance` | Provenance inventory, records and the active browser-resource profile |

## Browser checks

Browser checks are local opt-in and never run in CI. Set `ARCFORGES_LOCAL_BROWSER=1` and `ARCFORGES_BROWSER_BASE_URL` to a local or deployed origin, then run `dotnet test tests/browser/ArcForges.Web.Browser.Tests/ArcForges.Web.Browser.Tests.csproj -c Release`. Set `ARCFORGES_AXE_SCRIPT` to a local axe-core script to run the accessibility rules; axe-core is injected only into the page under test. The CI accessibility gate is the bUnit and xUnit semantic set.

## Dependencies

Use exact direct versions. Change the NuGet closure only through a successor admission receipt (`eng/policy/dependency-reviews/web-40-admission-rN.json`) and `eng/policy/dependency-policy.json`. Change the npm closure (wrangler, TypeScript and `@types/node`) through the same receipt chain. Commit the root `package-lock.json` after intentional changes. The Contracts identity is the NuGet publication record under `eng/contracts` (bound by `eng/version-sources.json` and the central pin in `Directory.Packages.props`), and the naming authority is the byte copy under `eng/naming` (digests in `eng/policy/naming-candidate.json`); both are bound by provenance records. Prettier and Biome are retired. Formatting gates: `dotnet format <project> --verify-no-changes` for each C# project in the CI `csharp` job, the text rules (one final newline, no trailing whitespace) of the audited inputs in the policy suite, and `npm run typecheck` for the thin TypeScript Worker.

Use the pinned npm for lock regeneration. A lock regenerated on a Node older than the pin needs `--engine-strict=false` for that one command; the repository setting stays `engine-strict=true`.

## Local validation limits (P2-017, P2-024)

Hosted CI is the authority for the Linux locked restore and for the CI job graph. Linux-affected checks may run once, scope-limited, in the local WSL2 Debian distribution through PowerShell on a Linux-native filesystem (`wsl.exe -d Debian -- ...`, not `/mnt/c`). Record the distribution, kernel, SDK and toolchain. Toolchain installs inside WSL are performed by the user. A local Node older than the pin (24.20 against 24.21) is a recorded local gap: the policy script's Node assertion is not run locally, and hosted CI is authoritative.
