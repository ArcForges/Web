# PRF.11 AL-06 budget re-baseline, static-asset file count and WA-08 costing (U6)

This is the reviewed AL-06 record for the Blazor profile asset budgets (`eng/policy/profile-budgets.json`). It changes baseline values only. The 10 percent regression gate (`regressionPercent` 10) is unchanged. It is written in the same commit as the baseline change, so the change is recorded and not silent (WA-08, AL-06).

Reviewer: `w-deku-20261008-rev-prf-11` (pre-assigned, brief S25). Decision recorded at write time: `approved`, reviewedOn `2026-10-09`. That field is a proposal. Only the named reviewer's exact-head approval ratifies it.

## Why a re-baseline is needed

Under WA-08 and AL-06, until a reviewed re-baseline record is merged, the React-measured values stay the enforced ceiling for Blazor builds. The React profile budgets were in `apps/app/budgets.json` at the PRF.08 commit `f8123b6` in Web history. Those values are roughly 119 KB gzip for the whole Account profile. A Blazor WebAssembly runtime alone is about 3.2 MB gzip, so the React ceiling cannot be met by any Blazor build. The WEB.40 baseline, measured at its U5 step (`c5a5f2e`), replaced the React ceiling in CI. This record makes that replacement reviewed, and it is the only AL-06 change for the asset budgets in PRF.11.

## Values

| Metric (gzip bytes unless stated) | React ceiling (PRF.08, `f8123b6`) account / chat | WEB.40 baseline (`c5a5f2e`) | Measured at base `c8588681` (this record) | Delta against WEB.40 |
| --- | --- | --- | --- | --- |
| initialRequests | 8 / 8 | 57 | 58 | +1 |
| htmlBytes | 2967 / 3271 | 719 | 766 | +47 |
| initialCssGzip | 3050 / 3050 | 2245 | 2380 | +135 |
| initialJsGzip | 116363 / 144698 | 125944 | 126011 | +67 |
| initialWasmGzip | not in React file | 3227235 | 3236688 | +9453 |
| initialDataGzip | not in React file | 848189 | 848189 | 0 |
| initialOtherGzip | not in React file | 238 | 238 | 0 |
| totalGzip | 119413 / 147748 | 4203851 | 4213506 | +9655 |

The measured values are the same for `account` and `chat`, because both profiles are served from one published application (the same shell and the same framework set). The new baseline is the measured value. The gate allows 10 percent growth above it. Against the WEB.40 baseline, the relative deltas are: htmlBytes +6.5 percent, initialCssGzip +6.0 percent, initialRequests +1.8 percent (one request), initialWasmGzip +0.3 percent, totalGzip +0.2 percent, initialJsGzip +0.05 percent, and none for the data and other metrics. The large relative changes are on small metrics. The cause of each delta was not isolated. The measurement is at the final WEB.40 head merged as `c8588681`, and the WEB.40 U5 baseline was taken before the later WEB.40 changes.

## Measurement

- Command, under CI conditions (`GITHUB_ACTIONS=true`, `CI=true`, a fresh `NUGET_PACKAGES`): `dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/publish/app`, then `dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll profiles budget --publish artifacts/publish/app --budgets eng/policy/profile-budgets.json`.
- The build is the IL build. `RunAOTCompilation` is `false` (D6, D-007). The publish printed that it published without optimizations and recommended the `wasm-tools` workload, which is not installed. The AOT benchmark is therefore recorded as **not run** (no wasm-tools workload or emscripten was installed, and no user install is performed).
- SDK 10.0.401, Windows 11, the local publish. The budget tool reported `Profile budgets passed.` against the new baseline, and the Tooling test project passed 56 of 56.
- Startup time and interaction responsiveness (INP) were not measured offline. They need the deployed profile origin and are blocked on CLOUD.85 (LS3, not proven). `interactionBudgets` stays `re-baseline-pending` with owner PRF.11, and the React interaction numbers (`apps/app/interaction-budgets.json` at `80900a1`) are not carried over. They were derived from one observation and are not comparable with Blazor (D8).

## Static-asset file count (platform limit)

- The published `wwwroot` holds 174 files, 116 of them precompressed (`.br` and `.gz`). Precompressed variants are counted, as the gate requires.
- The profile bundle (`web-profiles-43f87a11...tar`, built and verified by `profiles bundle` and `profiles verify`) has 175 members: the manifest and 174 served files. The served set is the `wwwroot` set minus the shell and its two encodings (`index.html`, `index.html.br`, `index.html.gz`), plus the profile pages `account/index.html` and `chat/index.html` and `_headers`. The served count is therefore 174.
- The platform limit, taken from the Cloudflare Workers platform limits page (Static Assets section, read on 2026-10-09): "Files per Worker version" is 20,000 on Workers Free and 100,000 on Workers Paid, and "Individual file size" is 25 MiB on both plans. The page does not say whether precompressed variants count. The gate uses the stricter 20,000.
- Result: 174 files against 20,000 (0.87 percent), so the Blazor profiles are inside the limit whether or not the precompressed variants count. The Site adds nine files (`artifacts/site`). Even if the two outputs were served from one Worker version, the total is 183 files, 0.92 percent of the limit. The largest single file was not measured. The single-file limit is 25 MiB, and the WebAssembly payload is about 3.2 MB gzip, but the uncompressed size of the largest `.wasm` file was not checked against the 25 MiB limit, so that is not claimed as proven.

## WA-08 costing of the non-virtualised lists

- Chat transcript (`src/ArcForges.Web.App/Pages/Chat.razor`): an `ol` rendered with `@foreach` over `_entries`, each entry an `li` with one `span` and text. The list is capped: `MaxEntries = 20`, and the oldest entries are removed when the cap is exceeded. The list therefore holds at most 20 `li` and 20 `span` elements, 40 elements and 40 text nodes, and is bounded by construction. No virtualisation is needed (Virtualize is refused by the strict `style-src`, and CS-01 states the .NET 10 position).
- Library lists: the Web App at `c8588681` has no library list, and the Account page renders no list (only a workspace count). No library-list cost is claimed here.
- Sustained chat memory (WA-08): WA-08 says the sustained chat memory has a recorded budget. The numeric value was not found in the Design authority (`ArcForges-Design` `main`, `b01ae31`) or in the Plan and Web records. The costing is therefore by construction only (the 20-entry cap above). The numeric sustained-memory budget and any measured memory for the chat profile are open and are not claimed. They need the recorded WA-08 value, and a measurement on the deployed profile would be a local opt-in run (blocked on CLOUD.85).

## Not claimed

- No startup, INP or AOT number is claimed. Those are blocked on CLOUD.85 or are not run.
- No deployed file count is claimed. The count above is for the local publish and the local bundle.
- The WA-08 numeric sustained chat memory budget is not claimed. It is open pending the recorded value.
