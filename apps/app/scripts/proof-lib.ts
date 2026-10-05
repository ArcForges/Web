// SPDX-License-Identifier: AGPL-3.0-only
// Pure helpers of the opt-in live run against the deployed proof environment (`proof-run.ts`). Nothing here
// reads a file, starts a browser or contacts a network, so the unit tests exercise it directly.
import { createHash, randomBytes, sign, type KeyObject } from "node:crypto";

/** The exact text the proof Worker verifies (Cloud `worker/foundation/operator-signature.ts`, `AF-OPERATOR-V2`). */
export function operatorMessage(
  method: string,
  host: string,
  pathname: string,
  time: string,
  nonce: string,
  bodySha256Hex: string,
): string {
  return `AF-OPERATOR-V2\n${method}\n${host}\n${pathname}\n${time}\n${nonce}\n${bodySha256Hex}`;
}

/** The Authorization header value of one operator request, signed with an Ed25519 private key. */
export function signOperatorRequest(
  key: KeyObject,
  method: string,
  host: string,
  pathname: string,
  body: Uint8Array,
  options: { nowSeconds?: number; nonce?: string } = {},
): string {
  const time = String(options.nowSeconds ?? Math.floor(Date.now() / 1000));
  const nonce = options.nonce ?? randomBytes(16).toString("base64url");
  const digest = createHash("sha256").update(body).digest("hex");
  const message = operatorMessage(method, host, pathname, time, nonce, digest);
  const signature = sign(null, Buffer.from(message, "utf8"), key).toString("base64url");
  return `AF-Operator t=${time},n=${nonce},s=${signature}`;
}

export const sessionCookieName = "__Host-af_session";

/**
 * Request headers the forwarder passes on to the deployed origin. The page's own `Origin` is replaced by the
 * deployed one (the proof Worker checks the exact configured value) and the cookie comes from the harness jar,
 * because a page served from a local origin can neither send nor store a `__Host-` cookie of another host.
 */
export function forwardedHeaders(
  incoming: Record<string, string | string[] | undefined>,
  deployedOrigin: string,
  cookie: string | undefined,
): Record<string, string> {
  const allowed = [
    "accept",
    "content-type",
    "x-af-csrf",
    "x-grpc-web",
    "x-user-agent",
    "grpc-timeout",
  ];
  const headers: Record<string, string> = { origin: deployedOrigin };
  for (const name of allowed) {
    const value = incoming[name];
    if (typeof value === "string") headers[name] = value;
  }
  if (cookie !== undefined) headers.cookie = `${sessionCookieName}=${cookie}`;
  return headers;
}

/** What a browser would do with the session cookie of a response: store a value, clear it, or keep what it has. */
export function nextCookie(
  current: string | undefined,
  setCookies: readonly string[],
): string | undefined {
  let value = current;
  for (const header of setCookies) {
    const match = /^__Host-af_session=([^;]*)/u.exec(header);
    if (!match) continue;
    const text = match[1] ?? "";
    // An empty value, or one that is already expired, deletes the cookie.
    const expired =
      /(?:^|;\s*)max-age=0(?:;|$)/iu.test(header) || /(?:^|;\s*)expires=.*1970/iu.test(header);
    value = text === "" || expired ? undefined : text;
  }
  return value;
}

export interface Summary {
  samples: number;
  minMs: number;
  medianMs: number;
  maxMs: number;
}

export function summarize(samples: readonly number[]): Summary {
  if (samples.length === 0) throw new Error("No samples to summarize");
  const sorted = [...samples].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  const median =
    sorted.length % 2 === 1
      ? (sorted[middle] as number)
      : ((sorted[middle - 1] as number) + (sorted[middle] as number)) / 2;
  return {
    samples: sorted.length,
    minMs: sorted[0] as number,
    medianMs: Math.round(median),
    maxMs: sorted[sorted.length - 1] as number,
  };
}

export interface InteractionBudgets {
  schema: 1;
  /** Where the numbers come from; Design defines no absolute interaction ceiling. */
  basis: string;
  minimumSamples: number;
  interactions: Record<string, { medianCeilingMs: number; maxCeilingMs: number }>;
}

/** Problems of a live measurement against the recorded ceilings; empty when it is within them. */
export function checkInteractionBudgets(
  measured: Record<string, Summary>,
  budgets: InteractionBudgets,
): string[] {
  const problems: string[] = [];
  for (const [name, ceiling] of Object.entries(budgets.interactions)) {
    const summary = measured[name];
    if (!summary) {
      problems.push(`${name}: not measured`);
      continue;
    }
    if (summary.samples < budgets.minimumSamples)
      problems.push(`${name}: ${summary.samples} samples, ${budgets.minimumSamples} required`);
    if (summary.medianMs > ceiling.medianCeilingMs)
      problems.push(`${name}: median ${summary.medianMs} ms over ${ceiling.medianCeilingMs} ms`);
    if (summary.maxMs > ceiling.maxCeilingMs)
      problems.push(`${name}: max ${summary.maxMs} ms over ${ceiling.maxCeilingMs} ms`);
  }
  for (const name of Object.keys(measured))
    if (!(name in budgets.interactions)) problems.push(`${name}: measured but has no budget`);
  return problems;
}
