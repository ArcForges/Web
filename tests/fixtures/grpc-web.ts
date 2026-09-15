// SPDX-License-Identifier: AGPL-3.0-only
// Test-only gRPC-Web fixture. Never included in the deployed static candidate.
import { SayHelloRequestSchema, SayHelloResponseSchema } from "@arcforges/proto";
import { create, fromBinary, toBinary } from "@bufbuild/protobuf";

export function frame(payload: Uint8Array, flag: number) {
  const bytes = new Uint8Array(payload.length + 5);
  bytes[0] = flag;
  new DataView(bytes.buffer).setUint32(1, payload.length);
  bytes.set(payload, 5);
  return bytes;
}

export function decodeHelloRequest(bytes: Uint8Array) {
  if (
    bytes.length < 5 ||
    bytes[0] !== 0 ||
    new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint32(1) !== bytes.length - 5
  )
    throw new Error("Expected one uncompressed protobuf request frame");
  return fromBinary(SayHelloRequestSchema, bytes.slice(5));
}

export function helloResponse(message = "Hello, ArcForges!") {
  const data = frame(
    toBinary(SayHelloResponseSchema, create(SayHelloResponseSchema, { message })),
    0,
  );
  const trailers = frame(new TextEncoder().encode("grpc-status: 0\r\n"), 0x80);
  const body = new Uint8Array(data.length + trailers.length);
  body.set(data);
  body.set(trailers, data.length);
  return body;
}
