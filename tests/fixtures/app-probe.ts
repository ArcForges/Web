// SPDX-License-Identifier: AGPL-3.0-only
// Test-only fixtures for the Account/Chat profile probes. Never part of a release graph.
// They produce wire shapes with the published generated codecs; they do not call a C# server.
import {
  serializeBrowserBootstrapResponseJson,
  serializeBrowserReceiptJson,
} from "@arcforges/api-client";

export const origin = "https://account.example.test";

export const authenticated = {
  csrfToken: "c".repeat(24),
  authenticated: true,
  session: {
    sessionId: "6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11",
    expiresAt: "2026-10-03T08:00:00.000000Z",
    idleExpiresAt: "2026-10-02T20:30:00.000000Z",
    userId: "0f0e6a30-5d1c-4c7e-8b53-5b6b7f9a4a10",
    deviceId: "8f2a1d77-1c1b-4b0a-9a55-2f1d0e5c7b21",
    workspaceIds: ["3f1d3b1e-2a8c-4c44-a1c8-77a1e8f0b9a1", "a2b84f55-0c2d-4b0d-8e6a-5d6e44a6c7d3"],
    recoveryGeneration: "18446744073709551615",
    purpose: "authenticate",
  },
  profile: {
    displayName: "Ada Lovelace",
    locale: "en-GB",
    timezone: "Europe/London",
    revision: "9007199254740993",
  },
} as const;

export const anonymous = {
  csrfToken: "a".repeat(24),
  authenticated: false,
} as const;

export function bootstrapBody(value: unknown): Uint8Array {
  return serializeBrowserBootstrapResponseJson(
    value as Parameters<typeof serializeBrowserBootstrapResponseJson>[0],
  );
}

export function receiptBody(effect = "happened"): Uint8Array {
  return serializeBrowserReceiptJson({
    commandId: "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    effect,
  });
}

export function jsonResponse(body: Uint8Array | string, init: ResponseInit = {}): Response {
  return new Response(body as BodyInit, {
    status: 200,
    ...init,
    headers: { "content-type": "application/json", ...(init.headers as object | undefined) },
  });
}

/** A response whose body is delivered in the given chunks, then optionally fails. */
export function streamed(
  chunks: Uint8Array[],
  failWith?: Error,
  init: ResponseInit = {},
): Response {
  let index = 0;
  const body = new ReadableStream<Uint8Array>({
    pull(controller) {
      const chunk = chunks[index++];
      if (chunk) controller.enqueue(chunk);
      else if (failWith) controller.error(failWith);
      else controller.close();
    },
  });
  return new Response(body, {
    status: 200,
    ...init,
    headers: { "content-type": "application/json", ...(init.headers as object | undefined) },
  });
}
