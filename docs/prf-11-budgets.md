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

The measured values are the same for `account` and `chat`, because both profiles are served from one published application (the same shell and the same framework set). The U6 baseline recorded in this table (the `c8588681` measurement) was superseded by the head re-measurement and the ceiling decision in the section "Head re-measurement and ceilings (review fix 1)" below. The gate allows 10 percent growth above the baseline. Against the WEB.40 baseline, the relative deltas are: htmlBytes +6.5 percent, initialCssGzip +6.0 percent, initialRequests +1.8 percent (one request), initialWasmGzip +0.3 percent, totalGzip +0.2 percent, initialJsGzip +0.05 percent, and none for the data and other metrics. The large relative changes are on small metrics. The cause of each delta was not isolated. The measurement is at the final WEB.40 head merged as `c8588681`, and the WEB.40 U5 baseline was taken before the later WEB.40 changes.

## Head re-measurement and ceilings (review fix 1)

The independent review of `2643127` found that the U6 baseline raised every ceiling above the pre-PRF.11 WEB.40 ceiling, while every head value fitted under the WEB.40 ceiling. The raised ceilings therefore loosened the gate by a margin the measurement did not require. This section records the re-measurement at the head and the decision that followed.

- Measured at code head `006b4b8` under CI conditions (`GITHUB_ACTIONS=true`, `CI=true`, the placeholder run identity `1`, a fresh `NUGET_PACKAGES` folder), with `App` published to `artifacts/publish/app` and `profiles budget` run against it. The budget commit that follows changes only `eng/policy/profile-budgets.json` and this file, so the publish output is the same.
- The gate is `value <= baseline + baseline * regressionPercent / 100` (`ProfileBudget.Limit`, rounded down), with `regressionPercent` 10 unchanged.

| Metric (gzip bytes unless stated) | WEB.40 baseline (`c5a5f2e`), the pre-PRF.11 ceiling basis | U6 baseline (`2a21bc3`, `c8588681` measurement), before this fix | Head measurement (`006b4b8`), account and chat | Ceiling now (= WEB.40 ceiling) | Headroom at head |
| --- | --- | --- | --- | --- | --- |
| initialRequests | 57 (ceiling 62) | 58 (ceiling 63) | 58 | 62 | 4 |
| htmlBytes | 719 (ceiling 790) | 766 (ceiling 842) | 766 | 790 | 24 |
| initialCssGzip | 2245 (ceiling 2469) | 2380 (ceiling 2618) | 2328 | 2469 | 141 |
| initialJsGzip | 125944 (ceiling 138538) | 126011 (ceiling 138612) | 126013 | 138538 | 12525 |
| initialWasmGzip | 3227235 (ceiling 3549958) | 3236688 (ceiling 3560356) | 3236690 | 3549958 | 313268 |
| initialDataGzip | 848189 (ceiling 933007) | 848189 (ceiling 933007) | 848189 | 933007 | 84818 |
| initialOtherGzip | 238 (ceiling 261) | 238 (ceiling 261) | 238 | 261 | 23 |
| totalGzip | 4203851 (ceiling 4624236) | 4213506 (ceiling 4634856) | 4213458 | 4624236 | 410778 |

Decision (review fix 1): every head value fits under its pre-PRF.11 WEB.40 ceiling, so no measurement requires a looser ceiling. The baseline of both profiles is therefore the WEB.40 baseline (`eng/policy/profile-budgets.json` is byte-identical to `c5a5f2e`). Each ceiling equals its WEB.40 ceiling, and every ceiling is tighter than the U6 ceiling it replaces.

- **Remaining loosening: none.** No ceiling is looser than the pre-PRF.11 WEB.40 ceiling.
- The head measurement is above the WEB.40 baseline in six metrics: initialRequests +1, htmlBytes +47, initialCssGzip +83, initialJsGzip +69, initialWasmGzip +9455, and totalGzip +7607. These increases are inside the existing 10 percent allowance and are recorded as the head measurement, not as the baseline. The cause of each increase since `c5a5f2e` was not isolated, and it does not affect any ceiling.
- Cross-check: the review measured a fresh clone at `2643127` and recorded initialWasmGzip 3236705 and totalGzip 4213473, 15 bytes above this measurement of `006b4b8` in each case. The other values matched. The 15-byte difference is not isolated. Both values fit under the same ceiling (3549958 and 4624236), so the decision does not depend on it.

## Largest single static file (review fix 6)

Measured at head `006b4b8` in `artifacts/publish/app/wwwroot` (174 files, 116 precompressed):

- The largest single file is `_framework/dotnet.native.rw4kynp763.wasm`, 3,001,422 bytes uncompressed (2.86 MiB). That is 11.4 percent of the 25 MiB (26,214,400 bytes) single-file limit. Its precompressed variants are 1,207,786 bytes (`.gz`) and 976,255 bytes (`.br`).
- The next largest files are `_framework/System.Private.CoreLib.o10ikxvddh.wasm` (1,682,197 bytes) and `_framework/icudt_no_CJK.lfu7j35m59.dat` (1,107,168 bytes).
- The single-file limit is therefore met with a margin of 88.6 percent. The earlier statement "the largest single file was not measured" is superseded by this measurement.

## Measurement

- Command, under CI conditions (`GITHUB_ACTIONS=true`, `CI=true`, a fresh `NUGET_PACKAGES`): `dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/publish/app`, then `dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll profiles budget --publish artifacts/publish/app --budgets eng/policy/profile-budgets.json`.
- The build is the IL build. `RunAOTCompilation` is `false` (D6, D-007). The publish printed that it published without optimizations and recommended the `wasm-tools` workload, which is not installed. The AOT benchmark is therefore recorded as **not run** (no wasm-tools workload or emscripten was installed, and no user install is performed).
- SDK 10.0.401, Windows 11, the local publish. The budget tool reported `Profile budgets passed.` against the new baseline, and the Tooling test project passed 56 of 56.
- Startup time and interaction responsiveness (INP) were not measured offline. They need the deployed profile origin and are blocked on CLOUD.85 (LS3, not proven). `interactionBudgets` stays `re-baseline-pending` with owner PRF.11, and the React interaction numbers (`apps/app/interaction-budgets.json` at `80900a1`) are not carried over. They were derived from one observation and are not comparable with Blazor (D8).

## Static-asset file count (platform limit)

- The published `wwwroot` holds 174 files, 116 of them precompressed (`.br` and `.gz`). Precompressed variants are counted, as the gate requires.
- The profile bundle (`web-profiles-43f87a11...tar`, built and verified by `profiles bundle` and `profiles verify`) has 175 members: the manifest and 174 served files. The served set is the `wwwroot` set minus the shell and its two encodings (`index.html`, `index.html.br`, `index.html.gz`), plus the profile pages `account/index.html` and `chat/index.html` and `_headers`. The served count is therefore 174.
- The platform limit, taken from the Cloudflare Workers platform limits page (Static Assets section, read on 2026-10-09): "Files per Worker version" is 20,000 on Workers Free and 100,000 on Workers Paid, and "Individual file size" is 25 MiB on both plans. The page does not say whether precompressed variants count. The gate uses the stricter 20,000.
- Result: 174 files against 20,000 (0.87 percent), so the Blazor profiles are inside the limit whether or not the precompressed variants count. The Site adds nine files (`artifacts/site`). Even if the two outputs were served from one Worker version, the total is 183 files, 0.92 percent of the limit. The largest single static file is `dotnet.native` (`.wasm`), 3,001,422 bytes uncompressed, 11.4 percent of the 25 MiB single-file limit (measured at head `006b4b8`; see "Largest single static file (review fix 6)").

## WA-08 costing of the non-virtualised lists

- Chat transcript (`src/ArcForges.Web.App/Pages/Chat.razor`): an `ol` rendered with `@foreach` over `_entries`, each entry an `li` with one `span` and text. The list is capped: `MaxEntries = 20`, and the oldest entries are removed when the cap is exceeded. The list therefore holds at most 20 `li` and 20 `span` elements, 40 elements and 40 text nodes, and is bounded by construction. No virtualisation is needed (Virtualize is refused by the strict `style-src`, and CS-01 states the .NET 10 position).
- Library lists: the Web App at `c8588681` has no library list, and the Account page renders no list (only a workspace count). No library-list cost is claimed here.
- Sustained chat memory (WA-08): WA-08 says the sustained chat memory has a recorded budget. The numeric value was not found in the Design authority (`ArcForges-Design` `main`, `b01ae31`) or in the Plan and Web records. The costing is therefore by construction only (the 20-entry cap above). The numeric sustained-memory budget and any measured memory for the chat profile are open and are not claimed. They need the recorded WA-08 value, and a measurement on the deployed profile would be a local opt-in run (blocked on CLOUD.85).

## Not claimed

- No startup, INP or AOT number is claimed. Those are blocked on CLOUD.85 or are not run.
- No deployed file count is claimed. The count above is for the local publish and the local bundle.
- The WA-08 numeric sustained chat memory budget is not claimed. It is open pending the recorded value.
- No deployed measurement is claimed. The head measurement is local and claimant-reported.
