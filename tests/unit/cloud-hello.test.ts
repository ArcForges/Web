// SPDX-License-Identifier: AGPL-3.0-only
import { expect, test } from "vitest";
import { checkServerConnection, helloApiPath } from "../../apps/site/app/cloud-hello";
import { decodeHelloRequest, helloResponse } from "../fixtures/grpc-web";

test("connection uses the published binary client, same-origin API and no credentials", async () => {
  const fetcher: typeof fetch = async (input, init) => {
    const request = new Request(input, init);
    expect(request.url).toBe(`https://arcforges.com${helloApiPath}`);
    expect(request.method).toBe("POST");
    expect(request.headers.get("content-type")).toContain("application/grpc-web+proto");
    expect(request.credentials).toBe("omit");
    expect(request.redirect).toBe("error");
    expect(request.headers.has("authorization")).toBe(false);
    expect(decodeHelloRequest(new Uint8Array(await request.arrayBuffer())).name).toBe("ArcForges");
    return new Response(helloResponse(), {
      headers: { "content-type": "application/grpc-web+proto" },
    });
  };
  await expect(
    checkServerConnection("https://arcforges.com", new AbortController().signal, fetcher),
  ).resolves.toBe("Hello, ArcForges!");
});

test("missing API and unexpected server output never become a local successful greeting", async () => {
  for (const response of [
    new Response("Not found", { status: 404 }),
    new Response("Method not allowed", { status: 405 }),
    new Response("<html>Not an API</html>", { headers: { "content-type": "text/html" } }),
    new Response(helloResponse("different service"), {
      headers: { "content-type": "application/grpc-web+proto" },
    }),
  ])
    await expect(
      checkServerConnection(
        "https://arcforges.com",
        new AbortController().signal,
        async () => response,
      ),
    ).rejects.toThrow();
});
