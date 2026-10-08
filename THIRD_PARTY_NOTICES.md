# Third-party notices

First-party Web code retains this repository's **AGPL-3.0-only** license. Dependencies keep their own terms; this document does not relicense them.

- The naming authority (`eng/naming`) and the CON.07 identity record (`eng/contracts`) are byte copies of the Apache-2.0 publication files of [ArcForges Contracts](https://github.com/ArcForges/Contracts) 1.0.0-ci.287.1: the `tools/naming` assets of the NuGet package `ArcForges.Contracts.Validation` and the build identity of `ArcForges.Contracts.PublicApi`. Neither is shipped in the public candidate. The C# Contracts packages are admitted by NuGet receipts. The former npm packages `@arcforges/proto` and `@arcforges/api-client` are retired (WEB.40 unit U5).
- The retained upstream notices for the React-era protobuf, Connect, Vite, Rolldown, turbo-stream and punycode material are kept in [supplementary upstream licenses](third-party/README.md) as immutable history. The current public candidate ships none of them.
- The public candidate ships the Tailwind CSS v4.3.3 preflight (MIT) in its stylesheet. Its notice, and the full AGPL text, are published as `/third-party-notices.txt` and `/license.txt`.
- Development/build dependencies and platform alternatives are recorded in the full lockfile CycloneDX SBOM. The separate browser SBOM describes actual emitted modules, stylesheet/helper origins and their relationships; parsed-only and prerender-only implementations are excluded from that browser closure.

No external fonts, photographs, GPL media library source or reference-product assets are copied into this Hello site. The simple mark and visual elements are first-party SVG/CSS.

## Browser packaging provenance

The public notices carry the Tailwind v4.3.3 notice and the full AGPL text. The React-era browser graph (Vite, Rolldown, React Router, protobuf-es and Connect-ES) was retired with the React build; its immutable records and `eng/provenance/NOTICE.txt` document the history. The source summary does not replace full legal text. See [the provenance process](docs/provenance.md) and [retained documents](third-party/README.md).
