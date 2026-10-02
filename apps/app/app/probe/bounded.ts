// SPDX-License-Identifier: AGPL-3.0-only
import { ProbeFailure } from "./failure.ts";

/** Reads a response body without ever holding more than `limit` bytes; a longer body is malformed. */
export async function readBounded(response: Response, limit: number): Promise<Uint8Array> {
  const declared = response.headers.get("content-length");
  if (declared !== null && (!/^\d+$/u.test(declared) || Number(declared) > limit))
    throw new ProbeFailure("malformed");
  if (!response.body) return new Uint8Array();
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;
  try {
    for (;;) {
      const next = await reader.read();
      if (next.done) break;
      length += next.value.byteLength;
      if (length > limit) {
        await reader.cancel();
        throw new ProbeFailure("malformed");
      }
      chunks.push(next.value);
    }
  } catch (error) {
    if (error instanceof ProbeFailure) throw error;
    throw new ProbeFailure("unavailable", { cause: error });
  }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return bytes;
}

export function isJsonContentType(value: string | null): boolean {
  return value !== null && /^application\/json\s*(?:;|$)/iu.test(value);
}
