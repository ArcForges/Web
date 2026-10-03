# Web architecture policy

`tooling/project.ts policy`, already part of the required PR source check, runs
`architecture.ts` against the tracked and nonignored source inventory, then runs
the exact published Contracts naming scanner. Biome remains the repository linter.
No additional lint framework, browser test or service call is introduced.

The AST parser and types are the existing locked Babel 7.29.8 build inputs, now
declared directly so policy execution does not depend on accidental npm hoisting.
Source imports/re-exports, type imports, literal dynamic imports and `require`
form a recursive graph. Release roots include application source, workspace source
and the owned Worker; reachable code outside these directories is also checked.
Computed imports and unresolved local code fail closed. Generated Router type-only
paths and static assets retain their existing generator/build validation.

| Rule                                | Passing fixture                                   | Refused fixture                                                                          |
| ----------------------------------- | ------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| workspace                           | Exact root workspaces and matching npm v3 lock    | Nested/extra manifests, secondary npm or alternate lock                                  |
| pins                                | Exact Node/npm/compiler/generator declarations    | Floating tool or dependency selector                                                     |
| sdk-ui                              | Apache-only source and manifest closure           | Direct or transitive Apache-to-AGPL workspace edge                                       |
| private-import                      | Published public Contracts imports                | Private/server/local-RPC package or sibling source                                       |
| wire-source                         | Published generated type alias and UI-local model | Handwritten wire declaration, JSON codec, direct business fetch or descriptor generator  |
| desktop-dom                         | DOM in Web source                                 | DOM/React in a desktop workspace graph                                                   |
| obsolete-target                     | Current Web graph                                 | Obsolete WebAssembly project/dependency target                                           |
| portable-reference                  | Standalone Windows adapter                        | Managed project referencing an esproj                                                    |
| production-command                  | Explicit local dev, production build              | Build-to-dev alias, install/HMR command, unknown/cyclic script                           |
| release-fixture                     | Production source-only transitive graph           | Imported fixture/helper, including through a bridge outside app/                         |
| computed-import / unresolved-import | Resolved literal source graph                     | Dynamic specifier or missing local source                                                |
| canonical naming                    | Current terms                                     | Every name in the published forbidden-name list, tested in an isolated local Git fixture |

These are source/build rules, not proof of live runtime behavior. Business network
calls use the published generated transport; ad-hoc JSON/direct-fetch exceptions
need an explicit future HTTP-contract admission. The owned edge Worker continues
to redirect and forward static assets. The rules are not a general security sandbox
for arbitrary dynamic JavaScript; they reject unsupported static graph forms.

The naming candidate identity and original publication receipt are recorded in
`naming-candidate.json`. Its scanner and policy SHA-256 values, package version and
source commit are checked before invocation. The scanner runs on the real Web Git
root; the asset is build-only and is not imported into the browser graph.

The Contracts 129 pin retains the public Hello fixture. Its API now fixes binary
gRPC-Web, unknown-field preservation and the shared recursion limit internally;
the two obsolete explicit binary selectors are removed without changing caller
origin, timeout, credentials or redirect behavior. Immutable browser profile r7
records 254 parsed and 123 emitted modules. Only `wire.js` newly emits and only
the Cloud Hello template changes; the other 17 normalized templates remain exact.
Original r1-r6 profiles and receipts remain unchanged. The final hosted build must
reproduce the reviewed graph and templates with all strict guards enabled.

The Contracts pin is now 1.0.0-ci.287.1 (PRF.08). The declared BrowserSession HTTP exception is consumed by
`apps/app` through the generated `browserSessionRoutes` catalogue and strict generated codecs, with an injected
fetcher exactly as the existing Hello page, so the direct-fetch and JSON rules above are unchanged. The policy still
has no explicit HTTP-exception admission; that remains future work and is not implied by this change.
