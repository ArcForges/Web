# Blazor profile bundle

The Account and Chat profiles are one published application (`src/ArcForges.Web.App`, `RunAOTCompilation=false`) served at two routes. The C# tool packs its publish output into one deterministic, digest-named archive that the Cloud proof deployment consumes (CLOUD.85). This document records the layout and the rules that consumers may rely on.

## Name and layout

- File name: `web-profiles-<sha256>.tar`, where the digest is the SHA-256 of the archive bytes. The pattern and the first entry are the same as the React bundle it replaces, so the name pattern does not change for consumers.
- First entry: `manifest.json` (schema 1). It lists each profile (`page`, `buildDigest`, `csp`) and every other entry with its SHA-256 and byte count.
- Root `_headers`: security headers for `/*`; one block per profile path (`/account/*`, `/chat/*`) with its Content-Security-Policy, `X-Frame-Options: DENY` and `no-cache`; `/assets/*` immutable; `/_framework/*` immutable; `/_framework/blazor.webassembly.js` and `/_framework/dotnet.js` revalidated (`no-cache`).
- Profile pages: `account/index.html` and `chat/index.html`, each the application shell, with the profile's own policy.
- Served tree: the publish `wwwroot` at its own root paths. The shell `index.html` is not served at the root, and neither are its precompressed siblings `index.html.br` and `index.html.gz`, because they only encode the shell. Blazor's base href is `/`, so `_framework/` files are root-relative. Every other file of the publish is kept, including the `.br` and `.gz` siblings of the `_framework/` files that the SDK writes.

## Rules the tool enforces

- Every `_framework` file is fingerprinted (ten lower-case characters before `.js`, `.wasm` or `.dat`, including the `.br` and `.gz` siblings) or is one of the two unfingerprinted loaders. Any other framework file fails the build, so the immutable rule covers fingerprinted content only.
- The bundle verifies its own name, its strict tar layout, its manifest and every member's digest, the profile pages, each profile policy against its page, and the exact headers file.
- Each profile policy stays within the Cloudflare header line budget. No `unsafe-eval`, no `unsafe-inline`, and `wasm-unsafe-eval` only where the App policy requires it (P2-021).
- Each profile policy sets `base-uri 'self'`, so the shells' `<base href="/">` takes effect. `base-uri 'none'` blocks that element, the relative framework loader then resolves under `/account/` or `/chat/`, and the shell never starts (S36). The public Site policy keeps `base-uri 'none'`.

## Size budgets

`eng/policy/profile-budgets.json` holds the reviewed baseline of the published application, measured by `dotnet ... profiles budget` with SDK 10.0.401:

| Metric | Baseline |
| --- | --- |
| `initialRequests` | 57 |
| `htmlBytes` | 719 |
| `initialCssGzip` | 2245 |
| `initialJsGzip` | 125944 |
| `initialWasmGzip` | 3227235 |
| `initialDataGzip` | 848189 |
| `initialOtherGzip` | 238 |
| `totalGzip` | 4203851 |

Each metric may grow by at most `regressionPercent` (10), rounded down. The candidate job enforces the budgets after the App publish. Gzip sizes are computed in-process at the fixed compression level. A baseline changes only by a reviewed change that names the measurement (AL-06). The React interaction ceilings in `apps/app/interaction-budgets.json` are re-baseline-pending under PRF.11, because the Blazor measurements are not comparable with them.

## CLOUD.71 and CLOUD.85 coordination

- CLOUD.85 serves the Blazor WebAssembly profiles and the static Site from the proof origin. It consumes the bundle by its name pattern and its root-relative layout above. Any change to the name pattern, the `manifest.json` schema, the profile paths or the framework layout needs a reviewed change in both repositories.
- CLOUD.71 records the bundle naming need. The coordination note with the Cloud owner must be recorded before the Cloud proof deployment uses this bundle. That note is pending; it is not claimed here.
