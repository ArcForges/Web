# Build identity

WP02.04 adds the sealed public `/__build-info.json` asset. It reports full Git source and dirty state, local or actual CI run/attempt/build ID, pipeline URL and source commit UTC timestamp, plus nine independent version axes. The existing `/__build.json` propagation endpoint remains compatible. Both identity paths use no-store/no-transform caching.

`eng/version-sources.json` names each axis's real source. AppVersion uses the allocated Web release; ContractSet uses the exact installed Contracts schema/descriptor provenance; PackageVersion inventories external npm coordinates from the committed lock, excluding owned workspace releases and links. Future capability, storage, policy and extension producers remain explicit. This static bootstrap owns no portable native format or C ABI. No compatibility axis is copied from an application/package release.

The candidate verifier reconstructs the report from independent source/lock/CI inputs and rejects altered build/axis values even when both outer receipts are resealed. CI source/attempt guards reject dirty, unrelated or incomplete identities. CI seals the asset without browser execution or public-file downloads. Optional local diagnostics may compare it only when relevant to an affected behavior. No runtime reads a verifier's environment to fabricate its own identity.

Cloud-derived build tooling is an explicitly reviewed AGPL adaptation. The successor browser provenance profile preserves the existing graph, executable templates, dependencies and full licences while binding the new metadata generator/catalog. This foundation report does not claim production accounts, storage migrations or a complete commercial application.

Runtime and publication checks follow [validation policy](validation-policy.md).
