# Supplementary upstream licenses

The published protobuf and Connect runtime tarballs contain copyright/license headers but omit their repository-level license files. Preserve the upstream files here, and include them alongside the headers in generated public notices:

- `protobuf-es-LICENSE.txt`: https://github.com/bufbuild/protobuf-es/blob/v2.15.0/LICENSE (verified identical to v2.14.1, which remains in the published Contracts dependency closure)
- `connect-es-LICENSE.txt`: https://github.com/connectrpc/connect-es/blob/v2.2.0/LICENSE

The protobuf runtime also contains Google's BSD-3-Clause varint implementation. The build copies its complete original notice from `dist/esm/wire/varint.js`, plus the Buf copyright header. Connect copyright headers are copied from the installed packages. Review these sources when updating the corresponding dependency family. These files remain under their upstream licenses.

The actual browser bundle also contains generated Vite/Rolldown helpers, Tailwind stylesheet material, Router's vendored turbo-stream and original Google protobuf schemas. Their complete legal documents are retained here with immutable records under `eng/provenance/records`; Rolldown's referenced MIT companions are included in full. turbo-stream's original licence retains devalue contributors' attribution. The punycode legal document is retained for a parsed route-pattern input; the current bundle does not emit that implementation. These documents supplement the full existing package notices. See [the source and candidate process](../docs/provenance.md).
