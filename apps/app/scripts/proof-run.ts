// SPDX-License-Identifier: AGPL-3.0-only
// Explicit local opt-in run of the built Account and Chat profiles against the DEPLOYED proof environment
// (Cloud PRF.07, `https://proof.arcforges.com`). Never CI. The holder of this run must hold the shared lease
// RES-cloud-deployment (python tools/delivery.py claim RES-cloud-deployment --worker W --task PRF.08) only for
// the run and release it immediately afterwards.
//
//   npm run build:profiles && node scripts/proof-run.ts
//
// What is real: the production profile bytes, the generated SDK inside them, a real Chromium, and the
// deployed Worker, Container, D1 and session code behind `/api` and `/session/v1`. The session comes from the
// proof-only operator operation `session/issue`, signed by the local operator key (read and used only here, never
// printed or written; only the public key is trusted by the Worker).
//
// Where the pages come from (PROOF_PAGES):
//   served (default)  the browser loads `https://proof.arcforges.com/account/` and `/chat/` from the deployed proof
//                     Worker itself (CLOUD.71): page, /api and /session/v1 share one origin, cookie and CSRF boundary.
//                     The harness only issues the session (operator-signed) and puts its cookie in the browser
//                     context, as a login would; the page's own requests go straight to the deployed origin.
//   forwarded         the earlier substitute: a local server serves the local builds and forwards three exact routes
//                     to the deployed origin, with the page's `Origin` replaced by the deployed one and the session
//                     cookie kept in a harness jar. It is a substitute for same-origin routing, not the deployed
//                     same-origin ingress, and the evidence says so.
// Nothing secret is recorded in the evidence file.
import assert from "node:assert/strict";
import { createHash, createPrivateKey, type KeyObject } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { createServer, type Server } from "node:http";
import { homedir } from "node:os";
import { dirname, extname, join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { type BrowserContext, chromium, type Page } from "@playwright/test";
import { failureText, isProbeFailure } from "../app/probe/failure.ts";
import { sayHello } from "../app/probe/hello.ts";
import { endSession, readSession } from "../app/probe/session.ts";
import { profileCsp } from "./bundle.ts";
import { initialResources, pageFile, servedFile } from "./measure.ts";
import type { Profile } from "../profile.ts";
import {
  checkInteractionBudgets,
  forwardedHeaders,
  type InteractionBudgets,
  assertProofOrigin,
  forwardTarget,
  isOwnLoopbackRequest,
  nextCookie,
  sessionCookieName,
  signOperatorRequest,
  summarize,
  type Summary,
} from "./proof-lib.ts";

const isFile = (file: string) => existsSync(file) && statSync(file).isFile();
const app = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repetitions = 5;
const types: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".txt": "text/plain; charset=utf-8",
};

interface Observation {
  scenario: string;
  /** `deployed` hit the deployed Worker; `harness` is decided by the harness only. */
  path: "deployed" | "harness";
  result: string;
}
interface Hop {
  method: string;
  path: string;
  status: number;
}

function record(samples: Record<string, number[]>, name: string, value: number) {
  const list = samples[name] ?? [];
  list.push(value);
  samples[name] = list;
}

const sleep = (ms: number) => new Promise((resolveWait) => setTimeout(resolveWait, ms));

async function timed(task: () => Promise<void>): Promise<number> {
  const start = performance.now();
  await task();
  return Math.round(performance.now() - start);
}

function loadKey(): KeyObject {
  const file =
    process.env.PROOF_OPERATOR_KEY_FILE ??
    join(homedir(), ".arcforges", "proof-operator", "ed25519-private.pem");
  assert(
    existsSync(file),
    "No operator key file; create it with the Cloud `operator:proof` script.",
  );
  const key = createPrivateKey(readFileSync(file));
  assert.equal(key.asymmetricKeyType, "ed25519", "The operator key must be Ed25519.");
  return key;
}

interface Issued {
  handle: string;
  csrfToken: string;
}

class Proof {
  readonly origin: string;
  readonly host: string;
  private readonly key: KeyObject;
  constructor(origin: string, key: KeyObject) {
    this.origin = origin;
    this.key = key;
    this.host = new URL(origin).host;
  }

  async issue(options: { idleSeconds?: number; absoluteSeconds?: number } = {}): Promise<Issued> {
    const pathname = "/proof/v1/session/issue";
    const body = JSON.stringify({
      userId: crypto.randomUUID(),
      deviceId: crypto.randomUUID(),
      workspaceIds: [crypto.randomUUID()],
      ...options,
    });
    const bytes = new TextEncoder().encode(body);
    const response = await fetch(new URL(pathname, this.origin), {
      method: "POST",
      headers: {
        authorization: signOperatorRequest(this.key, "POST", this.host, pathname, bytes),
        "content-type": "application/json",
      },
      body,
      redirect: "error",
      signal: AbortSignal.timeout(60_000),
    });
    assert.equal(response.status, 200, `session/issue answered ${response.status}`);
    const json = (await response.json()) as Record<string, unknown>;
    assert.equal(typeof json.handle, "string");
    assert.equal(typeof json.csrfToken, "string");
    return { handle: json.handle as string, csrfToken: json.csrfToken as string };
  }

  /** A fetcher for the profile's own session client, carrying `Origin` and the cookie a browser would. */
  fetcher(cookie: string | undefined, origin = this.origin): typeof fetch {
    return (input, init) => {
      const headers = new Headers(init?.headers);
      headers.set("origin", origin);
      if (cookie !== undefined) headers.set("cookie", `${sessionCookieName}=${cookie}`);
      return fetch(input, { ...init, headers });
    };
  }
}

/** The earlier substitute: serves one local build and forwards its three exact routes to the deployed origin. */
function forwardedServer(profile: Profile, proof: Proof) {
  const client = join(app, "build", profile, "client");
  assert(existsSync(join(client, pageFile(profile))), `Build the ${profile} profile first`);
  const policy = profileCsp(readFileSync(join(client, pageFile(profile)), "utf8"));
  const state = {
    cookie: undefined as string | undefined,
    /** Holds the answer of the next forwarded `/api` call, after the deployed host has answered it. */
    delayApiResponseMs: 0,
    hops: [] as Hop[],
    forwardedApi: 0,
  };
  const server = createServer((incoming, outgoing) => {
    void (async () => {
      const address = server.address();
      const port = address && typeof address === "object" ? address.port : 0;
      // A page or a DNS-rebound name must not drive the session jar of a run: loopback Host and own Origin only.
      if (!isOwnLoopbackRequest(incoming.headers, port)) {
        outgoing.writeHead(403, { "content-type": "text/plain" }).end("Forbidden");
        return;
      }
      const url = new URL(incoming.url ?? "/", "http://local");
      // Only these exact routes are forwarded, to fixed targets: nothing the page sends chooses a URL.
      const target = forwardTarget(url.pathname);
      const forwarded = target !== undefined;
      if (forwarded) {
        const chunks: Buffer[] = [];
        for await (const chunk of incoming) chunks.push(chunk as Buffer);
        const aborted = new AbortController();
        outgoing.on("close", () => {
          if (!outgoing.writableFinished) aborted.abort();
        });
        let reply: Response;
        try {
          reply = await fetch(new URL(target ?? "/", proof.origin), {
            method: incoming.method ?? "GET",
            headers: forwardedHeaders(incoming.headers, proof.origin, state.cookie),
            ...(chunks.length > 0 ? { body: Buffer.concat(chunks) } : {}),
            redirect: "manual",
            signal: AbortSignal.any([aborted.signal, AbortSignal.timeout(60_000)]),
          });
        } catch {
          if (!outgoing.destroyed) outgoing.writeHead(503).end();
          return;
        }
        state.cookie = nextCookie(state.cookie, reply.headers.getSetCookie());
        const bytes = Buffer.from(await reply.arrayBuffer());
        state.hops.push({
          method: incoming.method ?? "GET",
          path: url.pathname.startsWith("/api/") ? "/api/<service>/<method>" : url.pathname,
          status: reply.status,
        });
        if (url.pathname.startsWith("/api/")) {
          state.forwardedApi += 1;
          if (state.delayApiResponseMs > 0) await sleep(state.delayApiResponseMs);
        }
        if (outgoing.destroyed) return;
        const headers: Record<string, string> = {};
        for (const name of ["content-type", "cache-control"]) {
          const value = reply.headers.get(name);
          if (value !== null) headers[name] = value;
        }
        outgoing.writeHead(reply.status, headers).end(bytes);
        return;
      }
      const name = servedFile(url.pathname);
      const file = resolve(client, name ?? "");
      if (name === undefined || !file.startsWith(`${client}${sep}`) || !isFile(file)) {
        outgoing.writeHead(404, { "content-type": "text/plain" }).end("Not found");
        return;
      }
      outgoing.writeHead(200, {
        "content-type": types[extname(file)] ?? "application/octet-stream",
        "content-security-policy": policy,
        "x-content-type-options": "nosniff",
        "cache-control": "no-store",
      });
      outgoing.end(readFileSync(file));
    })();
  });
  return new Promise<{ server: Server; origin: string; state: typeof state }>((done) =>
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      assert(address && typeof address === "object");
      done({ server, origin: `http://127.0.0.1:${address.port}`, state });
    }),
  );
}

/** What a stage needs of the place a profile page comes from; the stages are the same in both modes. */
interface Site {
  /** The URL of the profile page. */
  url: string;
  state: { hops: Hop[]; forwardedApi: number };
  /** Called once with the browser context and page the stage drives. */
  attach(context: BrowserContext, page: Page): void;
  /** Gives the browser the session cookie, as a login would, or removes it. */
  setSession(handle: string | undefined): Promise<void>;
  hasSession(): Promise<boolean>;
  /** Holds every `/api` answer back from the page for this long, after the deployed host has answered it. */
  delayApi(milliseconds: number): Promise<void>;
  close(): void;
}

async function forwardedSite(profile: Profile, proof: Proof): Promise<Site> {
  const served = await forwardedServer(profile, proof);
  return {
    url: `${served.origin}/${profile}/`,
    state: served.state,
    attach: () => undefined,
    setSession: async (handle) => {
      served.state.cookie = handle;
    },
    hasSession: async () => served.state.cookie !== undefined,
    delayApi: async (milliseconds) => {
      served.state.delayApiResponseMs = milliseconds;
    },
    close: () => {
      served.server.close();
    },
  };
}

const kindOfPath = (pathname: string): string | undefined =>
  pathname === "/session/v1/bootstrap" || pathname === "/session/v1/logout"
    ? pathname
    : pathname.startsWith("/api/")
      ? "/api/<service>/<method>"
      : undefined;

/** The deployed origin serves the page: the browser's own requests go straight to it, nothing is forwarded. */
function servedSite(profile: Profile, proof: Proof): Site {
  const state = { hops: [] as Hop[], forwardedApi: 0 };
  const held = new WeakSet<object>();
  let context: BrowserContext | undefined;
  let page: Page | undefined;
  const note = (method: string, pathname: string, status: number) => {
    const path = kindOfPath(pathname);
    if (path === undefined) return;
    state.hops.push({ method, path, status });
    if (path.startsWith("/api/")) state.forwardedApi += 1;
  };
  return {
    url: `${proof.origin}/${profile}/`,
    state,
    attach(browserContext, browserPage) {
      context = browserContext;
      page = browserPage;
      browserPage.on("response", (response) => {
        const url = new URL(response.url());
        if (url.origin !== proof.origin || held.has(response.request())) return;
        note(response.request().method(), url.pathname, response.status());
      });
    },
    async setSession(handle) {
      assert(context, "attach first");
      await context.clearCookies({ name: sessionCookieName });
      if (handle !== undefined)
        await context.addCookies([
          {
            name: sessionCookieName,
            value: handle,
            url: proof.origin,
            httpOnly: true,
            secure: true,
          },
        ]);
    },
    async hasSession() {
      assert(context, "attach first");
      return (await context.cookies(proof.origin)).some(
        (cookie) => cookie.name === sessionCookieName,
      );
    },
    async delayApi(milliseconds) {
      assert(page, "attach first");
      await page.unroute("**/api/**");
      if (milliseconds === 0) return;
      await page.route("**/api/**", async (route) => {
        held.add(route.request());
        // The deployed host answers for real; only the answer's arrival at the page is held back.
        const response = await route.fetch();
        note(route.request().method(), new URL(route.request().url()).pathname, response.status());
        await sleep(milliseconds);
        await route.fulfill({ response }).catch(() => undefined);
      });
    },
    close: () => undefined,
  };
}

const pageMode = (): "served" | "forwarded" => {
  const mode = process.env.PROOF_PAGES ?? "served";
  assert(["served", "forwarded"].includes(mode), "PROOF_PAGES must be served or forwarded.");
  return mode as "served" | "forwarded";
};

async function openSite(profile: Profile, proof: Proof): Promise<Site> {
  return pageMode() === "served" ? servedSite(profile, proof) : forwardedSite(profile, proof);
}

const sha256 = (value: Uint8Array) => createHash("sha256").update(value).digest("hex");

/**
 * Opens the profile page. In served mode the page's own response is part of the observation: the profile's
 * Content-Security-Policy and security headers must arrive with it from the deployed origin, and, when this
 * checkout holds the local build, the served page and the resources it loads first must be that build's bytes.
 */
async function openPage(page: Page, site: Site, profile: Profile, observations: Observation[]) {
  const response = await page.goto(site.url);
  assert(response, `no navigation response from ${site.url}`);
  assert.equal(response.status(), 200, `${site.url} answered ${response.status()}`);
  if (pageMode() !== "served") return;
  const headers = response.headers();
  const policy = headers["content-security-policy"] ?? "";
  assert(policy.includes("connect-src 'self'"), "the page arrived without its profile policy");
  assert(!policy.includes("unsafe-inline"), "the profile policy allows unsafe inline content");
  assert.equal(headers["x-content-type-options"], "nosniff");
  assert.equal(headers["x-frame-options"], "DENY");
  const client = join(app, "build", profile, "client");
  const local = join(client, pageFile(profile));
  let comparison = "no local build in this checkout, so the bytes were not compared";
  if (isFile(local)) {
    const html = readFileSync(local, "utf8").replaceAll("\r\n", "\n");
    assert.equal(
      policy,
      profileCsp(html),
      "the served policy is not the one derived from the build",
    );
    assert.equal(
      sha256(await response.body()),
      sha256(Buffer.from(html)),
      "the served page is not the build's page",
    );
    const initial = initialResources(html);
    const origin = new URL(site.url).origin;
    for (const reference of [...initial.js, ...initial.css]) {
      const reply = await fetch(new URL(reference, origin), {
        signal: AbortSignal.timeout(30_000),
      });
      assert.equal(reply.status, 200, `${reference} answered ${reply.status}`);
      assert.equal(
        sha256(new Uint8Array(await reply.arrayBuffer())),
        sha256(readFileSync(join(client, reference.slice(1)))),
        `${reference} is not the build's file`,
      );
    }
    comparison = `the page and its ${initial.js.length + initial.css.length} initial resources equal the local build's bytes`;
  }
  observations.push({
    scenario: `${profile} (browser): page served by the deployed origin itself with its profile policy`,
    path: "deployed",
    result: `HTTP 200 on ${new URL(site.url).pathname}; policy with connect-src 'self' and no unsafe-inline, nosniff, frame denied; ${comparison}`,
  });
}

function watchPage(page: Page) {
  const problems: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") problems.push(`console: ${message.text()}`);
  });
  page.on("pageerror", (error) => problems.push(`pageerror: ${error.message}`));
  void page.addInitScript(() => {
    document.addEventListener("securitypolicyviolation", (event) =>
      console.error(`CSP violation: ${event.violatedDirective} ${event.blockedURI}`),
    );
  });
  return problems;
}

/**
 * The proof Containers sleep when idle and a cold start takes longer than the profile's ten second deadline
 * (observed: the first calls answer 503 or 504 for one to two minutes). Warm both routes with the profile's own
 * clients, bounded as the Cloud live scenarios are, so the measured interactions are warm ones.
 */
async function warm(proof: Proof, observations: Observation[]) {
  const routes: [string, () => Promise<unknown>][] = [
    [
      "session route (foundation Container)",
      () =>
        readSession({
          origin: proof.origin,
          signal: new AbortController().signal,
          fetcher: proof.fetcher(undefined),
        }),
    ],
    [
      "chat route (Hello Container)",
      () =>
        sayHello(proof.origin, "warm-up", new AbortController().signal, proof.fetcher(undefined)),
    ],
  ];
  for (const [name, call] of routes) {
    const start = performance.now();
    for (;;) {
      try {
        await call();
        break;
      } catch (error) {
        assert(isProbeFailure(error), "an untyped error escaped the profile client");
        assert(
          performance.now() - start < 240_000,
          `the ${name} did not warm up in 240 s (${error.kind})`,
        );
        await sleep(3000);
      }
    }
    observations.push({
      scenario: `warm-up of the ${name}, not budgeted`,
      path: "deployed",
      result: `first success after ${Math.round(performance.now() - start)} ms`,
    });
  }
}

/** The profile's own clients, from Node, against the deployed origin: status and failure kinds exactly as the page maps them. */
async function nodeScenarios(proof: Proof, observations: Observation[]) {
  const options = (cookie: string | undefined, signal = new AbortController().signal) => ({
    origin: proof.origin,
    signal,
    fetcher: proof.fetcher(cookie),
  });
  const kindOf = async (task: Promise<unknown>) => {
    try {
      await task;
      return "no failure";
    } catch (error) {
      assert(isProbeFailure(error), "an untyped error escaped the profile client");
      return error.kind;
    }
  };
  await warm(proof, observations);
  const anonymous = await readSession(options(undefined));
  assert.equal(anonymous.state, "anonymous");
  observations.push({
    scenario: "session: anonymous bootstrap parsed by the generated codec",
    path: "deployed",
    result: "anonymous with a CSRF token",
  });

  const issued = await proof.issue();
  const session = await readSession(options(issued.handle));
  assert.equal(session.state, "authenticated");
  // Compared without printing either value: a failed assert.equal would show both tokens.
  assert(
    session.csrfToken === issued.csrfToken,
    "the bootstrap token must equal the issued one (values withheld)",
  );
  assert.equal(session.state === "authenticated" && session.workspaces, 1);
  assert.equal(session.state === "authenticated" && session.recoveryGeneration, "0");
  observations.push({
    scenario: "session: real C# bootstrap body parsed by the generated TypeScript codec",
    path: "deployed",
    result:
      "authenticated; one workspace; exact uint64 recovery generation 0; CSRF token equals the issued one",
  });

  assert.equal(
    await kindOf(endSession({ ...options(issued.handle), csrfToken: "x".repeat(43) })),
    "forbidden",
  );
  assert.equal(
    await kindOf(
      endSession({
        origin: proof.origin,
        signal: new AbortController().signal,
        fetcher: proof.fetcher(issued.handle, "https://evil.example"),
        csrfToken: issued.csrfToken,
      }),
    ),
    "forbidden",
  );
  assert.equal(
    await kindOf(endSession({ ...options(undefined), csrfToken: issued.csrfToken })),
    "unauthenticated",
  );
  assert.equal((await readSession(options(issued.handle))).state, "authenticated");
  observations.push({
    scenario:
      "session: wrong CSRF token, wrong Origin and missing cookie are refused and leave the session alive",
    path: "deployed",
    result: "forbidden, forbidden, unauthenticated; session still authenticated",
  });

  assert.equal(
    await endSession({ ...options(issued.handle), csrfToken: issued.csrfToken }),
    "happened",
  );
  assert.equal(
    await kindOf(endSession({ ...options(issued.handle), csrfToken: issued.csrfToken })),
    "unauthenticated",
  );
  assert.equal((await readSession(options(issued.handle))).state, "anonymous");
  observations.push({
    scenario: "session: logout with the bootstrap CSRF token, receipt happened, revocation durable",
    path: "deployed",
    result: "happened; second logout unauthenticated; bootstrap anonymous",
  });

  const controller = new AbortController();
  const pending = kindOf(readSession(options(undefined, controller.signal)));
  controller.abort();
  assert.equal(await pending, "cancelled");
  observations.push({
    scenario: "session: user cancellation of an in-flight bootstrap",
    path: "deployed",
    result: "cancelled",
  });

  const hello = (name: string) =>
    sayHello(proof.origin, name, new AbortController().signal, proof.fetcher(undefined));
  assert.equal(await hello("ArcForges"), "Hello, ArcForges!");
  assert.equal(await hello("ArcForges 世界"), "Hello, ArcForges 世界!");
  assert.equal(await kindOf(hello("")), "rejected");
  assert.equal(await kindOf(hello("x".repeat(257))), "limit");
  observations.push({
    scenario: "chat route: generated Hello client through the deployed /api route",
    path: "deployed",
    result: "greeting, Unicode, empty name rejected, 257 code units limit",
  });
}

async function chat(proof: Proof, samples: Record<string, number[]>, observations: Observation[]) {
  const served = await openSite("chat", proof);
  const browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  served.attach(context, page);
  const problems = watchPage(page);
  try {
    await openPage(page, served, "chat", observations);
    await page.getByRole("button", { name: "Send" }).waitFor();
    assert.equal(served.state.forwardedApi, 0, "The page must send nothing before the user acts");
    const send = async (name: string, expected: string) => {
      await page.getByLabel("Name").fill(name);
      return timed(async () => {
        await page.getByRole("button", { name: "Send" }).click();
        await page
          .getByRole("listitem")
          .filter({ hasText: expected })
          .last()
          .waitFor({ timeout: 15_000 });
      });
    };
    for (let index = 0; index < repetitions; index++)
      record(
        samples,
        "chat.greeting",
        await send(`ArcForges ${index}`, `Hello, ArcForges ${index}!`),
      );
    await send("ArcForges 世界", "Hello, ArcForges 世界!");
    await send("", failureText.rejected);
    await send("x".repeat(257), failureText.limit);
    assert.equal(served.state.forwardedApi, repetitions + 3);
    assert(served.state.hops.every((hop) => hop.status === 200));
    observations.push({
      scenario:
        "chat (browser): greetings, Unicode, rejected and limit refusals through the deployed route",
      path: "deployed",
      result: `${repetitions} greetings, Unicode, rejected, limit; every gRPC-Web call answered HTTP 200`,
    });
    // Cancellation on the real path: the deployed host answers, the page's user cancels first and shows nothing.
    await served.delayApi(2000);
    const before = served.state.forwardedApi;
    await page.getByLabel("Name").fill("slow");
    await page.getByRole("button", { name: "Send" }).click();
    await page.getByRole("button", { name: "Cancel" }).click();
    await page.getByRole("listitem").filter({ hasText: failureText.cancelled }).waitFor();
    await sleep(3000);
    await served.delayApi(0);
    assert.equal(await page.getByRole("listitem").filter({ hasText: "Hello, slow!" }).count(), 0);
    assert.equal(
      served.state.forwardedApi,
      before + 1,
      "the request must have reached the deployed host",
    );
    observations.push({
      scenario:
        "chat (browser): user cancellation of a request the deployed host was still answering",
      path: "deployed",
      result: `${failureText.cancelled} The answer arriving later was not shown.`,
    });
    assert.deepEqual(problems, [], "Console errors or CSP violations");
  } finally {
    await browser.close();
    served.close();
  }
}

async function account(
  proof: Proof,
  samples: Record<string, number[]>,
  observations: Observation[],
) {
  const served = await openSite("account", proof);
  const browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  served.attach(context, page);
  const problems = watchPage(page);
  const signedOut = "You are signed out. Reload to check again.";
  try {
    await served.setSession(undefined);
    await openPage(page, served, "account", observations);
    try {
      await page.getByText("You are not signed in.").waitFor({ timeout: 15_000 });
    } catch (error) {
      console.error(
        "page text:",
        await page.locator("body").innerText(),
        served.state.hops,
        problems,
      );
      throw error;
    }
    observations.push({
      scenario: "account (browser): anonymous presentation from the deployed bootstrap",
      path: "deployed",
      result: "You are not signed in.",
    });

    for (let index = 0; index < repetitions; index++) {
      const issued = await proof.issue();
      await served.setSession(issued.handle);
      record(
        samples,
        "account.read",
        await timed(async () => {
          await page.goto(served.url);
          await page.getByText("Recovery generation").waitFor({ timeout: 15_000 });
        }),
      );
      if (index === 0) {
        await page.locator("dd.exact").filter({ hasText: /^0$/u }).waitFor();
        observations.push({
          scenario: "account (browser): authenticated presentation from a real issued session",
          path: "deployed",
          result: "display name absent, one workspace, exact uint64 recovery generation 0",
        });
      }
      const hopsBefore = served.state.hops.length;
      record(
        samples,
        "account.signout",
        await timed(async () => {
          await page.getByRole("button", { name: "Sign out" }).click();
          await page.getByText(signedOut).waitFor({ timeout: 15_000 });
        }),
      );
      const logout = served.state.hops
        .slice(hopsBefore)
        .find((hop) => hop.path === "/session/v1/logout");
      assert.equal(logout?.status, 200);
      assert.equal(await served.hasSession(), false, "the server must clear the session cookie");
      assert.equal(
        (
          await readSession({
            origin: proof.origin,
            signal: new AbortController().signal,
            fetcher: proof.fetcher(issued.handle),
          })
        ).state,
        "anonymous",
        "the signed-out session must be revoked on the deployed host",
      );
    }
    observations.push({
      scenario:
        "account (browser): sign out with the bootstrap CSRF token, cookie cleared by the server, session revoked",
      path: "deployed",
      result: `${repetitions} times: logout 200, Set-Cookie cleared the cookie, bootstrap then anonymous`,
    });

    // Session expiry on the real path: an idle session that is not used ends; its sign-out is a 401.
    const idleSeconds = 8;
    const short = await proof.issue({ idleSeconds, absoluteSeconds: 120 });
    await served.setSession(short.handle);
    await page.goto(served.url);
    await page.getByText("Recovery generation").waitFor();
    await sleep((idleSeconds + 3) * 1000);
    const hopsBefore = served.state.hops.length;
    await page.getByRole("button", { name: "Sign out" }).click();
    await page.getByText(signedOut).waitFor();
    assert.equal(await page.getByRole("alert").count(), 0);
    const expired = served.state.hops
      .slice(hopsBefore)
      .find((hop) => hop.path === "/session/v1/logout");
    assert.equal(expired?.status, 401, "an expired session must be refused with 401");
    observations.push({
      scenario: "account (browser): idle session expiry, then sign out",
      path: "deployed",
      result: "deployed logout answered 401; shown as ended with no alert",
    });
    assert.deepEqual(
      problems.filter((problem) => !/Failed to load resource/u.test(problem)),
      [],
    );
  } finally {
    await browser.close();
    served.close();
  }
}

/**
 * Served mode: the Worker still answers its own route families first, and the assets answer only their own
 * paths. Status and content type only; nothing here carries a credential.
 */
async function routeChecks(proof: Proof, observations: Observation[]) {
  const status = async (pathname: string) => {
    const reply = await fetch(new URL(pathname, proof.origin), {
      redirect: "manual",
      signal: AbortSignal.timeout(30_000),
    });
    await reply.arrayBuffer();
    return { status: reply.status, type: reply.headers.get("content-type") ?? "" };
  };
  const root = await status("/");
  assert.notEqual(root.status, 200, "the origin root is not a profile page");
  const unsigned = await status("/proof/v1/readiness");
  assert(
    unsigned.status >= 400 && unsigned.status < 500 && !unsigned.type.startsWith("text/html"),
    "an unsigned proof request must be refused by the Worker, not answered by an asset",
  );
  const missing = await status("/chat/not-a-file.js");
  assert.notEqual(missing.status, 200);
  observations.push({
    scenario: "origin routes: the root, an unsigned /proof/v1 request and an unknown profile path",
    path: "deployed",
    result: `root ${root.status}, unsigned proof request ${unsigned.status} (${unsigned.type || "no type"}), unknown path ${missing.status}`,
  });
}

async function main() {
  assert.notEqual(process.env.CI, "true", "The live run is local opt-in, never CI.");
  const origin = assertProofOrigin(
    process.env.PROOF_BASE_URL,
    process.env.PROOF_ALLOW_OTHER_ORIGIN,
  );
  const proof = new Proof(origin, loadKey());
  const observations: Observation[] = [];
  const samples: Record<string, number[]> = {};
  const startedAt = new Date().toISOString();
  if (pageMode() === "served") await routeChecks(proof, observations);
  await nodeScenarios(proof, observations);
  await account(proof, samples, observations);
  await chat(proof, samples, observations);
  const measured: Record<string, Summary> = {};
  for (const [name, values] of Object.entries(samples)) measured[name] = summarize(values);
  const budgetsFile = join(app, "interaction-budgets.json");
  const budgets = existsSync(budgetsFile)
    ? (JSON.parse(readFileSync(budgetsFile, "utf8")) as InteractionBudgets)
    : undefined;
  const budgetProblems = budgets
    ? checkInteractionBudgets(measured, budgets)
    : ["no interaction-budgets.json"];
  const output = join(app, "build", "proof-run.json");
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(
    output,
    `${JSON.stringify({ startedAt, finishedAt: new Date().toISOString(), host: new URL(origin).host, pages: pageMode(), observations, measured, budgetProblems }, null, 2)}\n`,
  );
  for (const observation of observations)
    console.log(`${observation.path.toUpperCase()} ${observation.scenario}: ${observation.result}`);
  for (const [name, summary] of Object.entries(measured))
    console.log(`${name}: ${JSON.stringify(summary)}`);
  if (!budgets) {
    console.log(
      "PARTIAL: every scenario passed and the measurements are written, but apps/app/interaction-budgets.json does not exist, so no interaction budget was checked. The run is not a pass.",
    );
    process.exitCode = 3;
    return;
  }
  if (budgetProblems.length > 0) {
    console.log(`FAILED interaction budgets:\n${budgetProblems.join("\n")}`);
    process.exitCode = 1;
    return;
  }
  console.log("Live profile run passed.");
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
