// SPDX-License-Identifier: AGPL-3.0-only
import { createHash, generateKeyPairSync, verify } from "node:crypto";
import { expect, test } from "vitest";
import {
  assertProofOrigin,
  checkInteractionBudgets,
  forwardTarget,
  isOwnLoopbackRequest,
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

test("only the three exact routes are forwarded, to fixed targets", () => {
  expect(forwardTarget("/session/v1/bootstrap")).toBe("/session/v1/bootstrap");
  expect(forwardTarget("/session/v1/logout")).toBe("/session/v1/logout");
  expect(forwardTarget("/api/arcforges.hello.v1.HelloService/SayHello")).toBe(
    "/api/arcforges.hello.v1.HelloService/SayHello",
  );
  for (const other of [
    "/api/",
    "/api/healthz",
    "/api/arcforges.hello.v1.HelloService/SayHello/",
    "/api/arcforges.hello.v1.HelloService/SayHello2",
    "/session/v1/",
    "/session/v1/bootstrap/",
    "/session/v1/bootstrap/../logout",
    "/proof/v1/session/issue",
    "/assets",
    "/",
    "",
  ])
    expect(forwardTarget(other)).toBeUndefined();
});

test("a live run targets the proof origin unless another https origin is opted in", () => {
  expect(assertProofOrigin(undefined, undefined)).toBe("https://proof.arcforges.com");
  expect(assertProofOrigin("https://proof.arcforges.com", undefined)).toBe(
    "https://proof.arcforges.com",
  );
  expect(() => assertProofOrigin("https://arcforges.com", undefined)).toThrow(
    /unless PROOF_ALLOW_OTHER_ORIGIN=1/u,
  );
  expect(() => assertProofOrigin("https://arcforges.com", "yes")).toThrow();
  expect(assertProofOrigin("https://other.example.test", "1")).toBe("https://other.example.test");
  expect(() => assertProofOrigin("http://proof.arcforges.com", "1")).toThrow(/https origin/u);
  expect(() => assertProofOrigin("https://proof.arcforges.com/x", "1")).toThrow(/https origin/u);
});

test("the loopback server answers only its own Host and own Origin", () => {
  expect(isOwnLoopbackRequest({ host: "127.0.0.1:5000" }, 5000)).toBe(true);
  expect(
    isOwnLoopbackRequest({ host: "127.0.0.1:5000", origin: "http://127.0.0.1:5000" }, 5000),
  ).toBe(true);
  expect(
    isOwnLoopbackRequest({ host: "127.0.0.1:5000", origin: "https://evil.example" }, 5000),
  ).toBe(false);
  expect(isOwnLoopbackRequest({ host: "rebound.example:5000" }, 5000)).toBe(false);
  expect(isOwnLoopbackRequest({ host: "127.0.0.1:5001" }, 5000)).toBe(false);
  expect(isOwnLoopbackRequest({}, 5000)).toBe(false);
});

test("every allow-listed header is forwarded, including the gRPC-Web deadline", () => {
  const headers = forwardedHeaders(
    { "grpc-timeout": "10000m", "x-user-agent": "ua" },
    "https://p.test",
    undefined,
  );
  expect(headers["grpc-timeout"]).toBe("10000m");
  expect(headers["x-user-agent"]).toBe("ua");
});

test("an empty value or Max-Age=0 clears the cookie on its own", () => {
  expect(nextCookie("abc", ["__Host-af_session=; Path=/"])).toBeUndefined();
  expect(nextCookie("abc", ["__Host-af_session=zzz; Path=/; Max-Age=0"])).toBeUndefined();
  expect(nextCookie("abc", ["__Host-af_session=zzz; Path=/; max-age=0; Secure"])).toBeUndefined();
  expect(nextCookie("abc", ["__Host-af_session=zzz; Path=/; Max-Age=01"])).toBe("zzz");
});
