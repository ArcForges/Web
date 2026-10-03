// SPDX-License-Identifier: AGPL-3.0-only
import { createPublicGrpcWebTransport } from "@arcforges/api-client";
import {
  EventService,
  EventServicePollRequestSchema,
  EventServicePollResponseSchema,
  EventSchema,
  EventServicePollValueSchema,
  ResponseMetaSchema,
  RevisionSchema,
} from "@arcforges/proto";
import { create, fromBinary, toBinary } from "@bufbuild/protobuf";
import { Code, createClient } from "@connectrpc/connect";
import { afterEach, expect, test, vi } from "vitest";
import { failureFromCode, helloApiPath, sayHello } from "../../apps/app/app/probe/hello.ts";
import { type FailureKind, ProbeFailure } from "../../apps/app/app/probe/failure.ts";
import { decodeHelloRequest, frame, helloResponse } from "../fixtures/grpc-web.ts";

const origin = "https://chat.example.test";
const grpcWeb = { "content-type": "application/grpc-web+proto" };
const signal = () => new AbortController().signal;
const trailers = (text: string) => frame(new TextEncoder().encode(text), 0x80);
const concat = (...parts: Uint8Array[]) => {
  const bytes = new Uint8Array(parts.reduce((total, part) => total + part.length, 0));
  let offset = 0;
  for (const part of parts) {
    bytes.set(part, offset);
    offset += part.length;
  }
  return bytes;
};

afterEach(() => {
  vi.useRealTimers();
});

async function kindOf(task: Promise<unknown>): Promise<FailureKind> {
  const error = await task.then(
    () => undefined,
    (reason: unknown) => reason,
  );
  expect(error).toBeInstanceOf(ProbeFailure);
  return (error as ProbeFailure).kind;
}

test("the greeting is one anonymous binary gRPC-Web call to same-origin /api", async () => {
  let seen: Request | undefined;
  const reply = await sayHello(origin, "ArcForges 世界", signal(), async (input, init) => {
    seen = new Request(input, init);
    return new Response(helloResponse("Hello, ArcForges 世界!") as BodyInit, { headers: grpcWeb });
  });
  expect(reply).toBe("Hello, ArcForges 世界!");
  expect(seen?.url).toBe(`${origin}${helloApiPath}`);
  expect(seen?.method).toBe("POST");
  expect(seen?.headers.get("content-type")).toContain("application/grpc-web+proto");
  expect(seen?.credentials).toBe("omit");
  expect(seen?.redirect).toBe("error");
  expect(seen?.headers.has("authorization")).toBe(false);
  expect(seen?.headers.has("cookie")).toBe(false);
  expect(decodeHelloRequest(new Uint8Array(await (seen as Request).arrayBuffer())).name).toBe(
    "ArcForges 世界",
  );
});

test("every gRPC status maps to a closed failure kind and none is a success", async () => {
  const expected: Record<number, FailureKind> = {
    [Code.Canceled]: "unexpected",
    [Code.Unknown]: "unexpected",
    [Code.InvalidArgument]: "rejected",
    [Code.DeadlineExceeded]: "timeout",
    [Code.NotFound]: "rejected",
    [Code.AlreadyExists]: "rejected",
    [Code.PermissionDenied]: "forbidden",
    [Code.ResourceExhausted]: "limit",
    [Code.FailedPrecondition]: "rejected",
    [Code.Aborted]: "unavailable",
    [Code.OutOfRange]: "rejected",
    [Code.Unimplemented]: "rejected",
    [Code.Internal]: "unexpected",
    [Code.Unavailable]: "unavailable",
    [Code.DataLoss]: "unexpected",
    [Code.Unauthenticated]: "unauthenticated",
  };
  for (const [code, kind] of Object.entries(expected)) {
    expect(failureFromCode(Number(code) as Code), `code ${code}`).toBe(kind);
    // The same status arriving as a real trailers-only gRPC-Web answer through the generated client.
    const actual = await kindOf(
      sayHello(origin, "x", signal(), async () => {
        return new Response(
          concat(trailers(`grpc-status: ${code}\r\ngrpc-message: Server%20text\r\n`)) as BodyInit,
          { headers: grpcWeb },
        );
      }),
    );
    expect(actual, `status ${code}`).toBe(kind);
  }
});

test("a server message is never echoed into the failure", async () => {
  const error = await sayHello(
    origin,
    "x",
    signal(),
    async () =>
      new Response(trailers("grpc-status: 3\r\ngrpc-message: secret-token-123\r\n") as BodyInit, {
        headers: grpcWeb,
      }),
  ).then(
    () => undefined,
    (reason: unknown) => reason as ProbeFailure,
  );
  expect(error?.kind).toBe("rejected");
  expect(error?.message).not.toContain("secret-token-123");
});

test("HTTP statuses and non-gRPC answers are typed failures, never a local greeting", async () => {
  const answers: [string, () => Response, FailureKind][] = [
    ["404", () => new Response("Not found", { status: 404 }), "rejected"],
    ["405", () => new Response("Method not allowed", { status: 405 }), "rejected"],
    ["429", () => new Response("Slow down", { status: 429 }), "limit"],
    ["500", () => new Response("Broken", { status: 500 }), "unexpected"],
    ["503", () => new Response(null, { status: 503 }), "unavailable"],
    ["504", () => new Response(null, { status: 504 }), "timeout"],
    [
      "html",
      () => new Response("<html>Not an API</html>", { headers: { "content-type": "text/html" } }),
      "malformed",
    ],
    [
      "valid frames under another content type",
      () =>
        new Response(helloResponse() as BodyInit, {
          headers: { "content-type": "application/json" },
        }),
      "malformed",
    ],
    ["no content type", () => new Response(helloResponse() as BodyInit), "malformed"],
  ];
  for (const [name, answer, kind] of answers)
    expect(await kindOf(sayHello(origin, "ArcForges", signal(), async () => answer())), name).toBe(
      kind,
    );
  expect(
    await kindOf(
      sayHello(origin, "ArcForges", signal(), async () => {
        throw new TypeError("Failed to fetch");
      }),
    ),
  ).toBe("unavailable");
});

test("malformed frames and status never resolve", async () => {
  const data = helloResponse("Hello, ArcForges!");
  const messageFrameLength = 5 + new DataView(data.buffer).getUint32(1);
  const messageFrame = data.slice(0, messageFrameLength);
  const bodies: [string, Uint8Array][] = [
    ["empty body", new Uint8Array()],
    ["truncated header", messageFrame.slice(0, 3)],
    ["truncated message", messageFrame.slice(0, messageFrameLength - 2)],
    ["no trailers", messageFrame],
    ["trailers without a status", concat(messageFrame, trailers("x-other: 1\r\n"))],
    ["unparsable status", concat(messageFrame, trailers("grpc-status: ok\r\n"))],
    [
      "compressed frame without negotiated compression",
      concat(Uint8Array.of(1, ...messageFrame.slice(1)), trailers("grpc-status: 0\r\n")),
    ],
    [
      "frame length beyond the body",
      concat(Uint8Array.of(0, 0, 0, 1, 0, 1), trailers("grpc-status: 0\r\n")),
    ],
    ["trailers only, status ok, no message", trailers("grpc-status: 0\r\n")],
    ["random bytes", Uint8Array.from({ length: 64 }, (_, index) => (index * 37 + 11) & 0xff)],
  ];
  for (const [name, bytes] of bodies)
    expect(
      await kindOf(
        sayHello(
          origin,
          "ArcForges",
          signal(),
          async () => new Response(bytes as BodyInit, { headers: grpcWeb }),
        ),
      ),
      name,
    ).toBe("malformed");
});

test("a reply that is not the greeting for this name is malformed", async () => {
  expect(
    await kindOf(
      sayHello(
        origin,
        "ArcForges",
        signal(),
        async () =>
          new Response(helloResponse("Hello, someone else!") as BodyInit, { headers: grpcWeb }),
      ),
    ),
  ).toBe("malformed");
});

test("the user's cancellation aborts the request and is reported as cancelled", async () => {
  const controller = new AbortController();
  let transportSignal: AbortSignal | undefined;
  const pending = sayHello(
    origin,
    "ArcForges",
    controller.signal,
    (_input, init) =>
      new Promise((_resolve, reject) => {
        transportSignal = init?.signal ?? undefined;
        init?.signal?.addEventListener("abort", () =>
          reject(new DOMException("aborted", "AbortError")),
        );
      }),
  );
  await vi.waitFor(() => expect(transportSignal).toBeDefined());
  expect(transportSignal?.aborted).toBe(false);
  controller.abort();
  expect(await kindOf(pending)).toBe("cancelled");
  expect(transportSignal?.aborted).toBe(true);
});

test("a call that has already been cancelled never reaches a success", async () => {
  const controller = new AbortController();
  controller.abort();
  expect(
    await kindOf(
      sayHello(origin, "ArcForges", controller.signal, async (_input, init) => {
        if (init?.signal?.aborted) throw new DOMException("aborted", "AbortError");
        return new Response(helloResponse() as BodyInit, { headers: grpcWeb });
      }),
    ),
  ).toBe("cancelled");
});

test("a server that never answers ends at the ten second deadline as a timeout", async () => {
  vi.useFakeTimers();
  const pending = kindOf(
    sayHello(
      origin,
      "ArcForges",
      signal(),
      (_input, init) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener("abort", () =>
            reject(new DOMException("aborted", "AbortError")),
          );
        }),
    ),
  );
  await vi.advanceTimersByTimeAsync(9_999);
  let settled = false;
  void pending.then(() => {
    settled = true;
  });
  await vi.advanceTimersByTimeAsync(0);
  expect(settled).toBe(false);
  await vi.advanceTimersByTimeAsync(2);
  expect(await pending).toBe("timeout");
});

test("the generated EventService client keeps 64-bit values exact over binary gRPC-Web", async () => {
  // Fixture-labelled: the Cloud probe does not serve EventService. This exercises the generated client,
  // transport and bigint mapping with values a JavaScript number cannot hold.
  const seq = 9007199254740993n;
  const generation = 18446744073709551615n;
  const entitlement = 9223372036854775807n;
  const revision = 9007199254740995n;
  const response = create(EventServicePollResponseSchema, {
    meta: create(ResponseMetaSchema, {
      resultRev: create(RevisionSchema, { value: revision }),
      entitlementVersion: entitlement,
      recoveryGeneration: generation,
    }),
    outcome: {
      case: "value",
      value: create(EventServicePollValueSchema, {
        events: [create(EventSchema, { subscriptionKey: "k", seq })],
      }),
    },
  });
  let request: Request | undefined;
  const transport = createPublicGrpcWebTransport({
    baseUrl: `${origin}/api`,
    fetch: async (input, init) => {
      request = new Request(input, init);
      return new Response(
        concat(
          frame(toBinary(EventServicePollResponseSchema, response), 0),
          trailers("grpc-status: 0\r\n"),
        ) as BodyInit,
        { headers: grpcWeb },
      );
    },
  });
  const reply = await createClient(EventService, transport).poll({
    cursor: "c",
    limit: 4294967295,
  });
  expect(request?.url).toBe(`${origin}/api/arcforges.events.v1.EventService/Poll`);
  const sent = fromBinary(
    EventServicePollRequestSchema,
    new Uint8Array(await (request as Request).arrayBuffer()).slice(5),
  );
  expect(sent.cursor).toBe("c");
  expect(sent.limit).toBe(4294967295);
  expect(reply.meta?.recoveryGeneration).toBe(generation);
  expect(reply.meta?.entitlementVersion).toBe(entitlement);
  expect(reply.meta?.resultRev?.value).toBe(revision);
  expect(reply.outcome.case).toBe("value");
  const events = reply.outcome.case === "value" ? reply.outcome.value.events : [];
  expect(events[0]?.seq).toBe(seq);
  expect(typeof events[0]?.seq).toBe("bigint");
  expect(String(events[0]?.seq)).toBe("9007199254740993");
});
