// SPDX-License-Identifier: AGPL-3.0-only

/** The closed set of reasons a probe call ends without a usable answer. */
export const failureKinds = [
  "cancelled",
  "timeout",
  "unavailable",
  "unauthenticated",
  "forbidden",
  "rejected",
  "limit",
  "malformed",
  "unexpected",
] as const;
export type FailureKind = (typeof failureKinds)[number];

/** A typed failure. The message is fixed per kind: it never echoes server text or request content. */
export class ProbeFailure extends Error {
  readonly kind: FailureKind;
  constructor(kind: FailureKind, options?: { cause?: unknown }) {
    super(failureText[kind], options);
    this.name = "ProbeFailure";
    this.kind = kind;
  }
}

export const failureText: Record<FailureKind, string> = {
  cancelled: "The request was cancelled.",
  timeout: "The server did not answer in time.",
  unavailable: "The server is unavailable. Try again later.",
  unauthenticated: "Your session has ended. Reload to check it again.",
  forbidden: "The server refused this request.",
  rejected: "The server rejected the request.",
  limit: "The request exceeded a server limit.",
  malformed: "The server answered with something this page cannot read.",
  unexpected: "The server failed unexpectedly. Try again later.",
};

export function isProbeFailure(value: unknown): value is ProbeFailure {
  return value instanceof ProbeFailure;
}

/** HTTP statuses of the same-origin session exception routes, mapped to a failure kind. */
export function failureFromStatus(status: number): FailureKind {
  if (status === 401) return "unauthenticated";
  if (status === 403) return "forbidden";
  if (status === 408 || status === 504) return "timeout";
  if (status === 413 || status === 429) return "limit";
  if (status === 502 || status === 503) return "unavailable";
  if (status >= 500) return "unexpected";
  if (status >= 400) return "rejected";
  return "malformed";
}
