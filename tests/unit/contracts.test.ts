// SPDX-License-Identifier: AGPL-3.0-only
import { createHelloClient } from "@arcforges/api-client";
import { SayHelloRequestSchema, SayHelloResponseSchema } from "@arcforges/proto";
import { create, fromBinary, toBinary } from "@bufbuild/protobuf";
import { expect, test } from "vitest";
function frame(payload: Uint8Array, flag: number) {
  const bytes = new Uint8Array(payload.length + 5);
  bytes[0] = flag;
  new DataView(bytes.buffer).setUint32(1, payload.length);
  bytes.set(payload, 5);
  return bytes;
}
// Explicit wire fixture; this does not assert that the C# Cloud service exists.
test("published SDK sends protobuf gRPC-Web and decodes the framed response", async () => {
  const client = createHelloClient({
    baseUrl: "https://cloud.example.test",
    useBinaryFormat: true,
    fetch: async (input, init) => {
      const request = new Request(input, init);
      expect(new URL(request.url).pathname).toBe("/arcforges.hello.v1.HelloService/SayHello");
      expect(request.headers.get("content-type")).toContain("application/grpc-web+proto");
      const bytes = new Uint8Array(await request.arrayBuffer());
      expect(bytes[0]).toBe(0);
      const decoded = fromBinary(SayHelloRequestSchema, bytes.slice(5));
      expect(decoded.name).toBe("ArcForges 世界");
      const message = frame(
        toBinary(
          SayHelloResponseSchema,
          create(SayHelloResponseSchema, { message: `Hello, ${decoded.name}!` }),
        ),
        0,
      );
      const trailer = frame(new TextEncoder().encode("grpc-status: 0\r\n"), 0x80);
      return new Response(new Blob([message, trailer]), {
        headers: { "content-type": "application/grpc-web+proto" },
      });
    },
  });
  await expect(client.sayHello({ name: "ArcForges 世界" })).resolves.toMatchObject({
    message: "Hello, ArcForges 世界!",
  });
});
test("SDK surfaces server failures instead of inventing a successful greeting", async () => {
  const client = createHelloClient({
    baseUrl: "https://cloud.example.test",
    fetch: async () =>
      new Response(null, {
        headers: {
          "content-type": "application/grpc-web+proto",
          "grpc-status": "16",
          "grpc-message": "Unauthenticated",
        },
      }),
  });
  await expect(client.sayHello({ name: "World" })).rejects.toThrow(/Unauthenticated/i);
});
