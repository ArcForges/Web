# Third-party notices

First-party Web code retains this repository's **AGPL-3.0-only** license. Dependencies keep their own terms; this document does not relicense them.

- `@arcforges/proto` and `@arcforges/api-client` 1.0.0-ci.25.1 are published Apache-2.0 packages from [ArcForges Contracts](https://github.com/ArcForges/Contracts). Their original LICENSE, NOTICE, source manifest and SBOM are copied into the candidate.
- The protobuf runtime is Apache-2.0 AND BSD-3-Clause. Connect runtimes are Apache-2.0. The build preserves the Buf/Connect copyright headers, Apache license and complete Google varint BSD notice; see [supplementary upstream licenses](third-party/README.md).
- React, React DOM, Scheduler, React Router and related runtime dependencies carry their upstream license texts. Those texts and the other production dependency notices are collected from the installed lockfile closure into public `/third-party-notices.txt`.
- Development/build dependencies are recorded in the full candidate CycloneDX SBOM; a separate runtime dependency SBOM describes the production closure. They may list code removed by tree-shaking or used only during prerendering.

No external fonts, photographs, GPL media library source or reference-product assets are copied into this Hello site. The simple mark and visual elements are first-party SVG/CSS.
