# Supplementary upstream licenses

The published protobuf and Connect runtime tarballs contain copyright/license headers but omit their repository-level license files. Preserve the upstream files here, and include them alongside the headers in generated public notices:

- `protobuf-es-LICENSE.txt`: https://github.com/bufbuild/protobuf-es/blob/v2.15.0/LICENSE (verified identical to v2.14.1, which remains in the published Contracts dependency closure)
- `connect-es-LICENSE.txt`: https://github.com/connectrpc/connect-es/blob/v2.2.0/LICENSE

The protobuf runtime also contains Google's BSD-3-Clause varint implementation. The build copies its complete original notice from `dist/esm/wire/varint.js`, plus the Buf copyright header. Connect copyright headers are copied from the installed packages. Review these sources when updating the corresponding dependency family. These files remain under their upstream licenses.
