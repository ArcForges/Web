// SPDX-License-Identifier: AGPL-3.0-only
import { createHash, generateKeyPairSync, verify } from "node:crypto";
import { expect, test } from "vitest";
import {
  checkInteractionBudgets,
  forwardedHeaders,
  type InteractionBudgets,
  nextCookie,
  operatorMessage,
  signOperatorRequest,
  summarize,
} from "../../apps/app/scripts/proof-lib.ts";

test("an operator request is signed over the exact message the Worker verifies", () => {
  const { privateKey, publicKey } = generateKeyPairSync("ed25519");
  const body = new TextEncoder().encode('{"a":1}');
  const header = signOperatorRequest(
    privateKey,
    "POST",
    "proof.example.test",
    "/proof/v1/session/issue",
    body,
    { nowSeconds: 1_790_000_000, nonce: "AAAAAAAAAAAAAAAAAAAAAA" },
  );
  const match = /^AF-Operator t=(\d{10}),n=([A-Za-z0-9_-]{22}),s=([A-Za-z0-9_-]{86})$/u.exec(
    header,
  );
  expect(match).not.toBeNull();
  const [, time = "", nonce = "", signature = ""] = match ?? [];
  const digest = createHash("sha256").update(body).digest("hex");
  const message = operatorMessage(
    "POST",
    "proof.example.test",
    "/proof/v1/session/issue",
    time,
    nonce,
    digest,
  );
  expect(message).toBe(
    `AF-OPERATOR-V2\nPOST\nproof.example.test\n/proof/v1/session/issue\n1790000000\nAAAAAAAAAAAAAAAAAAAAAA\n${digest}`,
  );
  expect(verify(null, Buffer.from(message), publicKey, Buffer.from(signature, "base64url"))).toBe(
    true,
  );
  // A different host, path or body does not verify.
  for (const other of [
    operatorMessage("POST", "other.example.test", "/proof/v1/session/issue", time, nonce, digest),
    operatorMessage("POST", "proof.example.test", "/proof/v1/exact", time, nonce, digest),
    operatorMessage(
      "POST",
      "proof.example.test",
      "/proof/v1/session/issue",
      time,
      nonce,
      "0".repeat(64),
    ),
  ])
    expect(verify(null, Buffer.from(other), publicKey, Buffer.from(signature, "base64url"))).toBe(
      false,
    );
});

test("the forwarder replaces Origin, injects only the session cookie and drops every other header", () => {
  const headers = forwardedHeaders(
    {
      origin: "http://127.0.0.1:5000",
      cookie: "other=1; __Host-af_session=page",
      authorization: "Bearer leak",
      accept: "application/json",
      "x-af-csrf": "token",
      "content-type": "application/grpc-web+proto",
      "x-grpc-web": "1",
      host: "127.0.0.1:5000",
    },
    "https://proof.example.test",
    "handle",
  );
  expect(headers).toEqual({
    origin: "https://proof.example.test",
    accept: "application/json",
    "x-af-csrf": "token",
    "content-type": "application/grpc-web+proto",
    "x-grpc-web": "1",
    cookie: "__Host-af_session=handle",
  });
  expect(
    forwardedHeaders({ cookie: "__Host-af_session=page" }, "https://p.test", undefined),
  ).toEqual({
    origin: "https://p.test",
  });
});

test("the cookie jar follows the server's Set-Cookie like a browser", () => {
  const set = "__Host-af_session=abc; Path=/; Secure; HttpOnly; SameSite=Strict";
  expect(nextCookie(undefined, [set])).toBe("abc");
  expect(nextCookie("abc", [])).toBe("abc");
  expect(nextCookie("abc", ["other=1; Path=/"])).toBe("abc");
  expect(nextCookie("abc", ["__Host-af_session=; Path=/; Secure; Max-Age=0"])).toBeUndefined();
  expect(
    nextCookie("abc", ["__Host-af_session=zzz; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT"]),
  ).toBeUndefined();
  expect(nextCookie("abc", ["__Host-af_session=def; Path=/; Max-Age=60"])).toBe("def");
});

test("summaries are exact for odd and even sample counts", () => {
  expect(summarize([30, 10, 20])).toEqual({ samples: 3, minMs: 10, medianMs: 20, maxMs: 30 });
  expect(summarize([10, 20, 30, 41])).toEqual({ samples: 4, minMs: 10, medianMs: 25, maxMs: 41 });
  expect(() => summarize([])).toThrow();
});

const budgets: InteractionBudgets = {
  schema: 1,
  basis: "test",
  minimumSamples: 3,
  interactions: { a: { medianCeilingMs: 100, maxCeilingMs: 200 } },
};

test("interaction budgets pass at the ceiling and fail one millisecond above it", () => {
  const ok = { a: { samples: 3, minMs: 1, medianMs: 100, maxMs: 200 } };
  expect(checkInteractionBudgets(ok, budgets)).toEqual([]);
  expect(checkInteractionBudgets({ a: { ...ok.a, medianMs: 101 } }, budgets)).toEqual([
    "a: median 101 ms over 100 ms",
  ]);
  expect(checkInteractionBudgets({ a: { ...ok.a, maxMs: 201 } }, budgets)).toEqual([
    "a: max 201 ms over 200 ms",
  ]);
  expect(checkInteractionBudgets({ a: { ...ok.a, samples: 2 } }, budgets)).toEqual([
    "a: 2 samples, 3 required",
  ]);
  expect(checkInteractionBudgets({}, budgets)).toEqual(["a: not measured"]);
  expect(checkInteractionBudgets({ ...ok, b: ok.a }, budgets)).toEqual([
    "b: measured but has no budget",
  ]);
});
