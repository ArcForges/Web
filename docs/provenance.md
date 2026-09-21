# Reuse and artifact provenance

Design's [current-repository and packaging profiles](https://github.com/ArcForges/ArcForges-Design/blob/5322d698a1b650a52a5a139d986dd85b00b48581/docs/assurance/reference-coverage-and-provenance.md#31-current-repository-implementation-profile) govern this owner. The current Web repository is the audit subject. Retired initialization repositories are not producers or build inputs.

## Before accepting material

The Licensing and Provenance Owner, including a maintainer-authorized implementation reviewer, inspects the particular source, tests, assets, wrappers, generated inputs and legal documents. Record the actual reviewer, date and compatibility rationale. The Architecture Owner resolves ownership/boundary questions; the Product Owner decides product changes. The five-row decision table in `eng/policy/reuse-policy.json` is closed. An unknown expression or origin fails; a package-root label alone does not settle a file's licence. Unlicense is explicitly classified after reviewing its complete commercial-copying permission. This does not add a policy exception.

Copy `eng/provenance/template.json` into a new `eng/provenance/records/<material>-r1.json`. Complete the ten evidence subjects: canonical repository, immutable commit, explicit paths, file-level licence evidence, attribution, targets, disposition, verification oracle, distributed notices and lifetime. The template is not approved material. Complete and review the record **before** introducing the reused material. Generated records identify every generator and input, their respective licences, the command and resulting licence position. Temporary reuse also names an owner and observable removal trigger.

Bind each target in `eng/provenance/files.json` to its record and SHA-256. Text hashes explicitly normalize CRLF to LF; binary hashes use raw bytes. Account for all tracked files, including tracked ignored files, and non-ignored new files. Authored classifications require contribution review, including reused material inserted into an existing file. Inventory automation cannot prove authorship.

Used records are append-only. Do not edit, reformat or remove them. A changed input, target, intent or obligation needs a new revision citing `supersedes`; update the active binding and retain history. Initial reconciliation states the inspected owner commit and does not claim that records existed before historical copying. The canonical AGPL record identifies a byte oracle, not an invented historical download origin. Legal-document records admit full legal text only, never the implementation governed by it.

`eng/provenance/NOTICE.txt` is a deterministic active-record summary. Regenerate it after reviewing inventory changes with the exported `auditProvenance(root, { writeNotice: true })` operation, then run normal checks. It supplements full legal texts and existing dependency notices. Source tooling copied from Cloud stays AGPL, has exact source/test attribution and creates no sibling-source dependency.

## Actual browser candidate

The immutable browser profile binds exact npm integrity, upstream commits and source/archive evidence, installed input hashes, the actual parsed and emitted graph, and every browser resource. Vite observes the actual client compilation without changing output. Generated Vite/Rolldown helpers, Tailwind styles, Router's embedded turbo-stream and the original protobuf schemas are explicit origins. The complete original MIT companions and Google BSD terms supplement the existing notices. Parsed-only modules and prerender-only isbot are distinguished from emitted implementation.

The initial independent oracle used two actual builds at different source identities. All 18 browser files matched after allowing only the exact source revision, asset basenames and the Router manifest fingerprint derived from its entry/routes. New or changed executable bodies, resources, graph members or legal bytes fail even when an outer manifest and receipt are recalculated. Dependency or application changes affecting those inputs require a newly reviewed profile and superseding artifact record. Never regenerate an oracle merely to make a failure pass.

The build SBOM uses the complete lockfile, including platform alternatives, and has a stable workspace name. The separate browser SBOM records actual emitted origins and module-derived relationships, including embedded origins and generated helpers. The initial `npm sbom --omit=dev` output omitted several emitted React-family packages; it is not the browser closure oracle. Build-only tools do not become runtime components merely because they generate code.

The candidate carries its active records, reviewed profile, observed graph and source-bound receipt under private `provenance/`. The receipt contains every actual candidate member hash. Full licences, third-party notices and the provenance summary are also public assets. The same gate checks exact Contracts package legal/source/SBOM bytes, both SBOMs, derived CSP/security/cache headers, deployment configuration, complete file membership and source/version identity at candidate construction and the deployment trust handoff. Source maps, SSR implementation, dependencies and private evidence remain outside public assets.

`npm run check` runs source enforcement and offline units once on Linux; it does not build or rescan a candidate. CI fetches full trusted history: PR base SHA, push `before` SHA, or the current committed baseline for scheduled/manual runs. Missing history fails. Commit/push hooks check whitespace only. CI retains source and actual graph evidence; the deploy job verifies the downloaded candidate without rebuilding it.

## Conflicting contributions

Register the affected material and evidence at `eng/provenance/conflicts/<id>.json` with `schemaVersion`, `id`, `material`, `evidence`, `boundary`, `owner`, `requiredDecision`, `status` and `resolution`. An unresolved conflict blocks acceptance and distribution. Return it to the accountable owner; resolve it through the formal decision and a new admissible record, or remove the affected material. No silent licence exception, boundary change or dropped product is permitted. An existing release conflict also invokes the owning release-remediation process.

Browser tests exercise the CF Web application in Chromium, Firefox and WebKit. Wide and narrow viewport screenshots are browser evidence. They do not establish Avalonia desktop or Kotlin/Compose Android behaviour. Native clients retain their separate implementation and verification boundaries.

WP02.04 appends `browser-resources-r2` for the independently sourced build-info asset and reviewed generator/catalog inputs. Existing browser graph/templates and all earlier profiles remain unchanged; the new asset is checked independently and is included in the closed candidate receipt.

## Current validation boundary

[Validation policy](validation-policy.md) supersedes earlier automatic browser/live/public-byte gates. Normal checks run offline source-policy/resolver tests; `test:artifact` is a separate opt-in investigation. Candidate construction retains required provenance, and the deployment entry point performs one trust-handoff check. Neither path launches a browser or downloads public assets to repeat verification.
