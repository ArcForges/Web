// SPDX-License-Identifier: AGPL-3.0-only
import { createHelloClient } from "@arcforges/api-client";
import { Code, ConnectError } from "@connectrpc/connect";
import { type FailureKind, failureFromStatus, ProbeFailure } from "./failure.ts";

export const helloApiPath = "/api/arcforges.hello.v1.HelloService/SayHello";
export const helloTimeoutMs = 10_000;

/**
 * The server's gRPC status, mapped to the page's closed failure kinds. A `Canceled` status the user did not
 * ask for is an unexpected server answer; the user's own cancellation is detected from the signal.
 */
export function failureFromCode(code: Code): FailureKind {
  switch (code) {
    case Code.DeadlineExceeded:
      return "timeout";
    case Code.Unavailable:
    case Code.Aborted:
      return "unavailable";
    case Code.Unauthenticated:
      return "unauthenticated";
    case Code.PermissionDenied:
      return "forbidden";
    case Code.ResourceExhausted:
      return "limit";
    case Code.InvalidArgument:
    case Code.NotFound:
    case Code.AlreadyExists:
    case Code.FailedPrecondition:
    case Code.OutOfRange:
    case Code.Unimplemented:
      return "rejected";
    default:
      return "unexpected";
  }
}

/** The client library's own protocol-error texts; a server status carries the server's text instead. */
const protocolText = /^(?:protocol error:|missing trailer|missing message|invalid grpc-status)/u;

/** Sends one anonymous greeting over binary gRPC-Web to same-origin `/api`; the caller may cancel it. */
export async function sayHello(
  origin: string,
  name: string,
  signal: AbortSignal,
  fetcher: typeof fetch,
): Promise<string> {
  // What the transport saw before the library mapped it, so a lossy library code never decides the kind.
  const seen: { network: boolean; status: number | undefined; foreign: boolean } = {
    network: false,
    status: undefined,
    foreign: false,
  };
  const client = createHelloClient({
    baseUrl: new URL("/api", origin).href,
    defaultTimeoutMs: helloTimeoutMs,
    // The greeting is anonymous: no cookie, no redirect.
    fetch: async (input, init) => {
      let response: Response;
      try {
        response = await fetcher(input, { ...init, credentials: "omit", redirect: "error" });
      } catch (error) {
        seen.network = true;
        throw error;
      }
      seen.status = response.status;
      if (
        response.status === 200 &&
        !/^application\/grpc-web/iu.test(response.headers.get("content-type") ?? "")
      ) {
        seen.foreign = true;
        await response.body?.cancel().catch(() => undefined);
        throw new Error("The answer is not gRPC-Web");
      }
      return response;
    },
  });
  let message: string;
  try {
    ({ message } = await client.sayHello({ name }, { signal }));
  } catch (error) {
    if (signal.aborted) throw new ProbeFailure("cancelled", { cause: error });
    if (seen.foreign) throw new ProbeFailure("malformed", { cause: error });
    if (seen.status !== undefined && seen.status !== 200)
      throw new ProbeFailure(failureFromStatus(seen.status), { cause: error });
    if (seen.network) {
      // A deadline abort surfaces as DeadlineExceeded; any other transport failure is unavailability.
      const code = error instanceof ConnectError ? error.code : undefined;
      throw new ProbeFailure(code === Code.DeadlineExceeded ? "timeout" : "unavailable", {
        cause: error,
      });
    }
    if (error instanceof ConnectError)
      throw new ProbeFailure(
        protocolText.test(error.rawMessage) ? "malformed" : failureFromCode(error.code),
        { cause: error },
      );
    throw new ProbeFailure("unexpected", { cause: error });
  }
  if (message !== `Hello, ${name}!`) throw new ProbeFailure("malformed");
  return message;
}
