// SPDX-License-Identifier: AGPL-3.0-only
import { SayHelloRequestSchema, SayHelloResponseSchema } from "@arcforges/proto";
import { create, fromBinary, toBinary } from "@bufbuild/protobuf";

// A local example using real published messages, not a substitute for a Cloud RPC.
export function greet(name: string): string {
  const containsControl = [...name].some(
    (char) => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127,
  );
  if (!name.trim() || [...name].length > 80 || containsControl) {
    throw new Error("Enter a name between 1 and 80 characters, without control characters.");
  }
  const request = create(SayHelloRequestSchema, { name: name.trim() });
  const decoded = fromBinary(SayHelloRequestSchema, toBinary(SayHelloRequestSchema, request));
  const response = create(SayHelloResponseSchema, { message: `Hello, ${decoded.name}!` });
  return fromBinary(SayHelloResponseSchema, toBinary(SayHelloResponseSchema, response)).message;
}
