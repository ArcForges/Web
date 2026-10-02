// SPDX-License-Identifier: AGPL-3.0-only
import {
  type BrowserBootstrapResponse,
  browserSessionRoutes,
  tryParseBrowserBootstrapResponseJson,
  tryParseBrowserReceiptJson,
} from "@arcforges/api-client";
import { readBounded, isJsonContentType } from "./bounded.ts";
import { tryExactUnsigned } from "./exact.ts";
import { failureFromStatus, ProbeFailure } from "./failure.ts";

/** The CSRF request header of cookie-authenticated unsafe operations (Design AU-08). */
export const csrfHeader = "X-AF-CSRF";

const route = (id: string) => {
  const found = browserSessionRoutes.find((candidate) => candidate.id === id);
  if (!found) throw new Error(`The published SDK has no browser session route ${id}`);
  return found;
};
const bootstrapRoute = route("browser.bootstrap");
const logoutRoute = route("browser.logout");

type BootstrapSession = NonNullable<BrowserBootstrapResponse["session"]>;
type BootstrapProfile = NonNullable<BrowserBootstrapResponse["profile"]>;

/** What the page may show about the current session; ids and the cookie are never exposed to the UI. */
export type SessionSnapshot =
  | { state: "anonymous"; csrfToken: string }
  | {
      state: "authenticated";
      csrfToken: string;
      displayName: string | undefined;
      locale: string | undefined;
      timezone: string | undefined;
      expiresAt: string;
      idleExpiresAt: string | undefined;
      workspaces: number;
      recoveryGeneration: string;
    };

interface CallOptions {
  origin: string;
  signal: AbortSignal;
  fetcher: typeof fetch;
}

async function send(options: CallOptions, path: string, init: RequestInit): Promise<Response> {
  try {
    return await options.fetcher(new URL(path, options.origin), {
      ...init,
      credentials: "same-origin",
      redirect: "error",
      cache: "no-store",
      signal: options.signal,
    });
  } catch (error) {
    if (options.signal.aborted) throw new ProbeFailure("cancelled", { cause: error });
    throw new ProbeFailure("unavailable", { cause: error });
  }
}

async function jsonBody(response: Response, limit: number): Promise<Uint8Array> {
  if (response.status !== 200) {
    await response.body?.cancel().catch(() => undefined);
    throw new ProbeFailure(failureFromStatus(response.status));
  }
  if (!isJsonContentType(response.headers.get("content-type"))) throw new ProbeFailure("malformed");
  return readBounded(response, limit);
}

function snapshot(body: BrowserBootstrapResponse): SessionSnapshot {
  if (!body.authenticated) {
    // An anonymous answer that still carries a session or profile is contradictory, not anonymous.
    if (body.session !== undefined || body.profile !== undefined)
      throw new ProbeFailure("malformed");
    return { state: "anonymous", csrfToken: body.csrfToken };
  }
  const session: BootstrapSession | undefined = body.session;
  if (session === undefined) throw new ProbeFailure("malformed");
  const generation = tryExactUnsigned(session.recoveryGeneration);
  if (generation === undefined) throw new ProbeFailure("malformed");
  const profile: BootstrapProfile | undefined = body.profile;
  return {
    state: "authenticated",
    csrfToken: body.csrfToken,
    displayName: profile?.displayName,
    locale: profile?.locale,
    timezone: profile?.timezone,
    expiresAt: session.expiresAt,
    idleExpiresAt: session.idleExpiresAt,
    workspaces: session.workspaceIds.length,
    recoveryGeneration: generation,
  };
}

/** GET the bootstrap route: the cookie rides along, the answer is parsed only by the generated codec. */
export async function readSession(options: CallOptions): Promise<SessionSnapshot> {
  const response = await send(options, bootstrapRoute.path, {
    method: bootstrapRoute.method,
    headers: { accept: "application/json" },
  });
  const bytes = await jsonBody(response, 16384);
  const parsed = tryParseBrowserBootstrapResponseJson(bytes);
  if (!parsed.ok) throw new ProbeFailure("malformed");
  return snapshot(parsed.value);
}

/**
 * POST the logout route with the CSRF token. Only the receipt effect `happened` ends the session on the
 * page; `didNotHappen` and `unknown` are failures, because the cookie may still be valid.
 */
export async function endSession(
  options: CallOptions & { csrfToken: string },
): Promise<"happened"> {
  const response = await send(options, logoutRoute.path, {
    method: logoutRoute.method,
    headers: { accept: "application/json", [csrfHeader]: options.csrfToken },
  });
  const bytes = await jsonBody(response, 4096);
  const parsed = tryParseBrowserReceiptJson(bytes);
  if (!parsed.ok) throw new ProbeFailure("malformed");
  if (parsed.value.effect === "happened") return "happened";
  throw new ProbeFailure(parsed.value.effect === "didNotHappen" ? "rejected" : "unexpected");
}
