// SPDX-License-Identifier: AGPL-3.0-only
import { createHelloClient } from "@arcforges/api-client";

export const helloApiPath = "/api/arcforges.hello.v1.HelloService/SayHello";

export async function checkServerConnection(
  origin: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
) {
  const client = createHelloClient({
    baseUrl: new URL("/api", origin).href,
    useBinaryFormat: true,
    defaultTimeoutMs: 10000,
    fetch: (input, init) => fetcher(input, { ...init, credentials: "omit", redirect: "error" }),
  });
  const response = await client.sayHello({ name: "ArcForges" }, { signal });
  if (response.message !== "Hello, ArcForges!") throw new Error("Unexpected Hello response");
  return response.message;
}
