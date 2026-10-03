// SPDX-License-Identifier: AGPL-3.0-only
import { expect, test } from "vitest";
import { exactUnsigned, tryExactUnsigned } from "../../apps/app/app/probe/exact.ts";
import { type FailureKind, ProbeFailure } from "../../apps/app/app/probe/failure.ts";
import { csrfHeader, endSession, readSession } from "../../apps/app/app/probe/session.ts";
import {
  anonymous,
  authenticated,
  bootstrapBody,
  jsonResponse,
  origin,
  receiptBody,
  streamed,
} from "../fixtures/app-probe.ts";

const signal = () => new AbortController().signal;

async function failureOf(task: Promise<unknown>): Promise<FailureKind> {
  const error = await task.then(
    () => undefined,
    (reason: unknown) => reason,
  );
  expect(error).toBeInstanceOf(ProbeFailure);
  return (error as ProbeFailure).kind;
}

test("bootstrap is a same-origin cookie GET driven by the generated route catalogue", async () => {
  let seen: Request | undefined;
  const session = await readSession({
    origin,
    signal: signal(),
    fetcher: async (input, init) => {
      seen = new Request(input, init);
      return jsonResponse(bootstrapBody(authenticated));
    },
  });
  expect(seen?.url).toBe(`${origin}/session/v1/bootstrap`);
  expect(seen?.method).toBe("GET");
  expect(seen?.credentials).toBe("same-origin");
  expect(seen?.redirect).toBe("error");
  expect(seen?.cache).toBe("no-store");
  expect(seen?.headers.has(csrfHeader)).toBe(false);
  expect(seen?.headers.has("authorization")).toBe(false);
  expect(session).toEqual({
    state: "authenticated",
    csrfToken: authenticated.csrfToken,
    displayName: "Ada Lovelace",
    locale: "en-GB",
    timezone: "Europe/London",
    expiresAt: authenticated.session.expiresAt,
    idleExpiresAt: authenticated.session.idleExpiresAt,
    workspaces: 2,
    recoveryGeneration: "18446744073709551615",
  });
});

test("an anonymous answer carries only the CSRF token", async () => {
  const session = await readSession({
    origin,
    signal: signal(),
    fetcher: async () => jsonResponse(bootstrapBody(anonymous)),
  });
  expect(session).toEqual({ state: "anonymous", csrfToken: anonymous.csrfToken });
});

test("an authenticated session without a profile still presents", async () => {
  const { profile: _profile, ...withoutProfile } = authenticated;
  const session = await readSession({
    origin,
    signal: signal(),
    fetcher: async () => jsonResponse(bootstrapBody(withoutProfile)),
  });
  expect(session).toMatchObject({
    state: "authenticated",
    displayName: undefined,
    locale: undefined,
  });
});

test("contradictory or inexact bootstrap answers are malformed, never a session", async () => {
  const { session: _session, ...noSession } = authenticated;
  const cases: [string, unknown][] = [
    ["authenticated without a session", noSession],
    ["anonymous with a session", { ...anonymous, session: authenticated.session }],
    ["anonymous with a profile", { ...anonymous, profile: authenticated.profile }],
    [
      "leading zero generation",
      { ...authenticated, session: { ...authenticated.session, recoveryGeneration: "01" } },
    ],
    [
      "generation above uint64",
      {
        ...authenticated,
        session: { ...authenticated.session, recoveryGeneration: "18446744073709551616" },
      },
    ],
  ];
  for (const [name, value] of cases) {
    let bytes: Uint8Array;
    try {
      bytes = bootstrapBody(value);
    } catch {
      // The generated codec already refuses this shape on the server side; send raw text instead.
      bytes = new TextEncoder().encode(JSON.stringify(value));
    }
    const kind = await failureOf(
      readSession({ origin, signal: signal(), fetcher: async () => jsonResponse(bytes) }),
    );
    expect(kind, name).toBe("malformed");
  }
});

test("the generated codec refuses malformed, unknown-field and wrong-type documents", async () => {
  const bodies = [
    "",
    "{",
    "[]",
    "null",
    '{"csrfToken":"x"}',
    '{"csrfToken":"x","authenticated":"yes"}',
    '{"csrfToken":"x","authenticated":false,"extra":1}',
    '{"csrfToken":"x","authenticated":false,"authenticated":true}',
    `{"csrfToken":"${"x".repeat(20)}","authenticated":false`,
  ];
  for (const body of bodies)
    expect(
      await failureOf(
        readSession({ origin, signal: signal(), fetcher: async () => jsonResponse(body) }),
      ),
      body,
    ).toBe("malformed");
});

test("only a JSON 200 is read; every other status is a typed failure with a fixed message", async () => {
  const statuses: [number, FailureKind][] = [
    [301, "malformed"],
    [400, "rejected"],
    [401, "unauthenticated"],
    [403, "forbidden"],
    [404, "rejected"],
    [408, "timeout"],
    [413, "limit"],
    [429, "limit"],
    [500, "unexpected"],
    [502, "unavailable"],
    [503, "unavailable"],
    [504, "timeout"],
  ];
  for (const [status, kind] of statuses) {
    const error = await readSession({
      origin,
      signal: signal(),
      // A body that would parse as a session proves the status is checked first.
      fetcher: async () => jsonResponse(bootstrapBody(authenticated), { status }),
    }).then(
      () => undefined,
      (reason: unknown) => reason as ProbeFailure,
    );
    expect(error?.kind, String(status)).toBe(kind);
    expect(error?.message).not.toContain(authenticated.csrfToken);
  }
  for (const type of ["text/html", "application/x-ndjson", "", "application/jsonp"])
    expect(
      await failureOf(
        readSession({
          origin,
          signal: signal(),
          fetcher: async () =>
            new Response(bootstrapBody(authenticated) as BodyInit, {
              headers: type ? { "content-type": type } : {},
            }),
        }),
      ),
      type,
    ).toBe("malformed");
  const parameters = await readSession({
    origin,
    signal: signal(),
    fetcher: async () =>
      new Response(bootstrapBody(anonymous) as BodyInit, {
        headers: { "content-type": "application/json; charset=utf-8" },
      }),
  });
  expect(parameters.state).toBe("anonymous");
});

test("the body is bounded before it is parsed", async () => {
  const exact = bootstrapBody(anonymous);
  const padded = new Uint8Array(16384 + 1).fill(0x20);
  padded.set(exact);
  // At the schema bound a document is still read (padding is JSON whitespace).
  const atBound = new Uint8Array(16384).fill(0x20);
  atBound.set(exact);
  expect(
    (
      await readSession({
        origin,
        signal: signal(),
        fetcher: async () => jsonResponse(atBound),
      })
    ).state,
  ).toBe("anonymous");
  expect(
    await failureOf(
      readSession({ origin, signal: signal(), fetcher: async () => jsonResponse(padded) }),
    ),
  ).toBe("malformed");
  // A declared length above the bound is refused without reading, and a stream above it is cut off
  // after at most one chunk beyond the bound, for the session and the receipt alike.
  for (const [bound, call] of [
    [16384, (fetcher: typeof fetch) => readSession({ origin, signal: signal(), fetcher })],
    [
      4096,
      (fetcher: typeof fetch) => endSession({ origin, signal: signal(), csrfToken: "t", fetcher }),
    ],
  ] as const) {
    let cancelled = false;
    let pulled = 0;
    const endless = new Response(
      new ReadableStream<Uint8Array>({
        pull(controller) {
          pulled += 1024;
          controller.enqueue(new Uint8Array(1024).fill(0x20));
        },
        cancel() {
          cancelled = true;
        },
      }),
      { headers: { "content-type": "application/json" } },
    );
    expect(await failureOf(call(async () => endless)), String(bound)).toBe("malformed");
    expect(cancelled).toBe(true);
    expect(pulled).toBeGreaterThan(bound);
    expect(pulled).toBeLessThanOrEqual(bound + 4096);
  }
  expect(
    await failureOf(
      readSession({
        origin,
        signal: signal(),
        fetcher: async () => jsonResponse(exact, { headers: { "content-length": "16385" } }),
      }),
    ),
  ).toBe("malformed");
  expect(
    await failureOf(
      readSession({
        origin,
        signal: signal(),
        fetcher: async () => jsonResponse(exact, { headers: { "content-length": "12e3" } }),
      }),
    ),
  ).toBe("malformed");
});

test("the stream bound is exact: one byte over it is the first byte refused", async () => {
  for (const [bound, call] of [
    [16384, (fetcher: typeof fetch) => readSession({ origin, signal: signal(), fetcher })],
    [
      4096,
      (fetcher: typeof fetch) => endSession({ origin, signal: signal(), csrfToken: "t", fetcher }),
    ],
  ] as const) {
    let pulled = 0;
    const body = new ReadableStream<Uint8Array>(
      {
        pull(controller) {
          pulled += 1;
          controller.enqueue(Uint8Array.of(0x20));
        },
      },
      { highWaterMark: 0 },
    );
    const answer = new Response(body, { headers: { "content-type": "application/json" } });
    expect(await failureOf(call(async () => answer)), String(bound)).toBe("malformed");
    expect(pulled, String(bound)).toBe(bound + 1);
  }
});

test("a body split across chunks reads as one document; a failing stream is unavailable", async () => {
  const bytes = bootstrapBody(authenticated);
  const chunks = [bytes.slice(0, 7), bytes.slice(7, 90), bytes.slice(90)];
  expect(
    (
      await readSession({
        origin,
        signal: signal(),
        fetcher: async () => streamed(chunks),
      })
    ).state,
  ).toBe("authenticated");
  expect(
    await failureOf(
      readSession({
        origin,
        signal: signal(),
        fetcher: async () => streamed([bytes.slice(0, 20)], new TypeError("connection reset")),
      }),
    ),
  ).toBe("unavailable");
});

test("network failure is unavailable and cancellation is distinct from it", async () => {
  expect(
    await failureOf(
      readSession({
        origin,
        signal: signal(),
        fetcher: async () => {
          throw new TypeError("Failed to fetch");
        },
      }),
    ),
  ).toBe("unavailable");
  const controller = new AbortController();
  const pending = readSession({
    origin,
    signal: controller.signal,
    fetcher: (_input, init) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener("abort", () =>
          reject(new DOMException("aborted", "AbortError")),
        );
      }),
  });
  controller.abort();
  expect(await failureOf(pending)).toBe("cancelled");
  const already = new AbortController();
  already.abort();
  expect(
    await failureOf(
      readSession({
        origin,
        signal: already.signal,
        fetcher: async (_input, init) => {
          if (init?.signal?.aborted) throw new DOMException("aborted", "AbortError");
          return jsonResponse(bootstrapBody(anonymous));
        },
      }),
    ),
  ).toBe("cancelled");
});

test("logout sends the CSRF token as the only credential header, with no body", async () => {
  let seen: Request | undefined;
  const effect = await endSession({
    origin,
    signal: signal(),
    csrfToken: authenticated.csrfToken,
    fetcher: async (input, init) => {
      seen = new Request(input, init);
      expect(init?.body).toBeUndefined();
      return jsonResponse(receiptBody("happened"));
    },
  });
  expect(effect).toBe("happened");
  expect(seen?.url).toBe(`${origin}/session/v1/logout`);
  expect(seen?.method).toBe("POST");
  expect(seen?.credentials).toBe("same-origin");
  expect(seen?.redirect).toBe("error");
  // The literal wire name is pinned here, independent of the module constant.
  expect(seen?.headers.get("x-af-csrf")).toBe(authenticated.csrfToken);
  expect(csrfHeader.toLowerCase()).toBe("x-af-csrf");
  expect(seen?.headers.get(csrfHeader)).toBe(authenticated.csrfToken);
  expect(seen?.headers.has("authorization")).toBe(false);
  expect(await seen?.text()).toBe("");
});

test("logout reports an ended session, a refused token and a malformed receipt distinctly", async () => {
  const run = (response: () => Response) =>
    endSession({ origin, signal: signal(), csrfToken: "t", fetcher: async () => response() });
  expect(await failureOf(run(() => new Response("{}", { status: 401 })))).toBe("unauthenticated");
  expect(await failureOf(run(() => new Response("{}", { status: 403 })))).toBe("forbidden");
  expect(await failureOf(run(() => new Response("{}", { status: 400 })))).toBe("rejected");
  expect(await failureOf(run(() => new Response("{}", { status: 503 })))).toBe("unavailable");
  expect(await failureOf(run(() => jsonResponse(receiptBody("didNotHappen"))))).toBe("rejected");
  expect(await failureOf(run(() => jsonResponse(receiptBody("unknown"))))).toBe("unexpected");
  expect(await failureOf(run(() => jsonResponse("{}")))).toBe("malformed");
  expect(await failureOf(run(() => jsonResponse('{"commandId":"x"}')))).toBe("malformed");
  expect(await failureOf(run(() => jsonResponse(new Uint8Array(4097).fill(0x20))))).toBe(
    "malformed",
  );
});

test("64-bit values stay exact: canonical text in, the same text out, nothing through a number", () => {
  for (const value of [
    "0",
    "9007199254740991",
    "9007199254740993",
    "9223372036854775807",
    "18446744073709551615",
  ]) {
    const exact = exactUnsigned(value);
    expect(exact.text).toBe(value);
    expect(exact.value).toBe(BigInt(value));
  }
  // A JavaScript number cannot hold these two values apart; the exact path can.
  expect(Number("9007199254740993")).toBe(Number("9007199254740992"));
  expect(exactUnsigned("9007199254740993").value).not.toBe(exactUnsigned("9007199254740992").value);
  for (const value of [
    "",
    "01",
    "-1",
    "+1",
    "1.0",
    "1e3",
    " 1",
    "1 ",
    "0x10",
    "18446744073709551616",
  ])
    expect(tryExactUnsigned(value), value).toBeUndefined();
});
