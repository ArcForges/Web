# Project licence boundaries (WP00.02)

The [accepted Design profile](https://github.com/ArcForges/ArcForges-Design/blob/6ba885ad38dd71de532c74d7b69f439d01d19a0a/docs/architecture/01-solution-and-project-layout.md#41-project-declaration-and-verification-profile)
assigns Web's original code, tests and tooling to AGPL-3.0-only / AGPL.
`eng/policy/licence-boundary.json` enumerates every current project/build manifest.
The inventory check discovers tracked and nonignored files independently; changing
the policy cannot change the permitted repository assignment.

The existing `npm run check` and candidate build check effective npm declarations,
source declarations for every build scope, imported MSBuild properties, project
reference containment and locked first-party package ownership. Tests exercise
new projects, missing/inconsistent properties, imports, escaped references, npm
aliases, unknown transitive first-party packages and invalid Gradle declarations.
No adjacent repository is imported or built. Third-party licences and existing
candidate notices remain separately owned and enforced.

`node tooling/project.ts licence-evaluated` evaluates owned MSBuild projects in
Debug and Release and retains their actual properties and reference edges. Build
targets reject wrong effective values before build/pack. Source and evaluated
reports include the exact commit, dirty state, inventory and findings under
`artifacts/evidence/licence-*.json`; CI uploads the reports.

The local opt-in accessibility re-proof (WEB.40 U6, `tests/browser`, P2-021 item 8) admits four test-only NuGet packages through the
dependency policy: Deque.AxeCore.Playwright 4.13.0 (MIT) and its dependencies Newtonsoft.Json 13.0.1 (MIT) and
System.IO.Abstractions 17.0.24 (MIT), and Deque.AxeCore.Commons 4.13.0, which carries the axe-core script under MPL-2.0. The
MPL-2.0 row is admitted only with `testOnly: true`; no product project restores it, it is never shipped or compiled into a product
output, and the policy suite fails if a product project references a testOnly row. The axe-core script is injected only into the
page under test. The AngleSharp 1.7.0 parser (MIT) is a direct reference of the Site parity test, which is also test-only.

The npm inventory covers the root manifest only (wrangler, TypeScript and `@types/node`). The former first-party Contracts npm packages `@arcforges/proto` and `@arcforges/api-client` are retired (WEB.40 unit U5); the Contracts naming and identity bytes are recorded by `contracts-publication-r1`. The React workspaces and the JavaScript IDE adapter are retired (WEB.40 unit U5). Windows CI evaluates the owned C# projects with .NET SDK 10.0.401. The final static candidate retains its licence/provenance checks. Browser and public-download verification are removed from CI under [validation policy](validation-policy.md).

These source/build-policy results do not establish product functionality or close
later commercial gates. Local candidate identity and fixture/runtime evidence are
recorded separately from the merged deployment and public release.
