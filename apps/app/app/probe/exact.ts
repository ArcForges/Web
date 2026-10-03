// SPDX-License-Identifier: AGPL-3.0-only
import { parseUInt64 } from "@arcforges/proto";

/**
 * A uint64 that crossed the wire as canonical decimal text (JSON exception records carry 64-bit values
 * as strings). It is validated by the generated package, held as a bigint and shown as the same
 * canonical text: no value passes through a JavaScript number.
 */
export function exactUnsigned(wire: string): { value: bigint; text: string } {
  const value = parseUInt64(wire);
  return { value, text: value.toString(10) };
}

/** Returns the canonical text, or `undefined` when the generated parser refuses the value. */
export function tryExactUnsigned(wire: string): string | undefined {
  try {
    return exactUnsigned(wire).text;
  } catch {
    return undefined;
  }
}
