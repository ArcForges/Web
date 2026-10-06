// SPDX-License-Identifier: AGPL-3.0-only
// Explicit local opt-in browser run of the built Account and Chat profiles. Never CI.
//
//   AOT_HOST_EXE=<path to the Cloud Native AOT host> node scripts/local-run.ts
//
// What is real here: the production profile bytes (run `npm run build:profiles` first), the generated SDK
// inside them, a real Chromium, and the real Cloud Native AOT host process answering the Hello gRPC-Web
// calls (including its own typed refusals). What is not: there is no Cloudflare, Worker or Container, only a
// local same-origin server that serves the profile and forwards `/api/*` to the host with the prefix
// removed, as the deployed Worker route is documented to do. The Account profile has no real session
// source here (a session needs the Worker, D1 and an issued session), so its session routes are labelled
// fixtures answered by the browser test harness. Nothing here proves a deployed result.
import assert from "node:assert/strict";
import { type ChildProcess, spawn } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createServer, request as httpRequest, type Server } from "node:http";
import { dirname, extname, join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, type Page } from "@playwright/test";
import {
  serializeBrowserBootstrapResponseJson,
  serializeBrowserReceiptJson,
} from "@arcforges/api-client";
import { profileCsp } from "./bundle.ts";
import { failureText } from "../app/probe/failure.ts";
import { pageFile, servedFile } from "./measure.ts";

const app = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const hostPort = 8080;
const types: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".txt": "text/plain; charset=utf-8",
};

function serve(profile: string): Promise<{ server: Server; origin: string }> {
  const client = join(app, "build", profile, "client");
  assert(existsSync(join(client, pageFile(profile))), `Build the ${profile} profile first`);
  const policy = profileCsp(readFileSync(join(client, pageFile(profile)), "utf8"));
  const server = createServer((incoming, outgoing) => {
    const url = new URL(incoming.url ?? "/", "http://local");
    if (url.pathname.startsWith("/api/")) {
      const forwarded = httpRequest(
        {
          host: "127.0.0.1",
          port: hostPort,
          method: incoming.method,
          path: url.pathname.slice("/api".length) + url.search,
          headers: { ...incoming.headers, host: `127.0.0.1:${hostPort}` },
        },
        (reply) => {
          outgoing.writeHead(reply.statusCode ?? 502, reply.headers);
          reply.pipe(outgoing);
        },
      );
      forwarded.on("error", () => {
        outgoing.writeHead(503).end();
      });
      incoming.pipe(forwarded);
      return;
    }
    const name = servedFile(url.pathname);
    const file = resolve(client, name ?? "");
    if (name === undefined || !file.startsWith(`${client}${sep}`) || !existsSync(file)) {
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
  });
  return new Promise((done) =>
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      assert(address && typeof address === "object");
      // The page URL: each profile runs on its own path of the origin.
      done({ server, origin: `http://127.0.0.1:${address.port}/${profile}/` });
    }),
  );
}

async function waitForHealth(timeoutMs: number) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if ((await fetch(`http://127.0.0.1:${hostPort}/healthz`)).ok) return;
    } catch {
      // still starting
    }
    await new Promise((resolveWait) => setTimeout(resolveWait, 250));
  }
  throw new Error("The host did not become healthy.");
}

interface Observation {
  scenario: string;
  real: boolean;
  milliseconds: number | undefined;
  result: string;
}

async function timed<T>(task: () => Promise<T>): Promise<[T, number]> {
  const start = performance.now();
  const value = await task();
  return [value, Math.round(performance.now() - start)];
}

const authenticated = {
  csrfToken: "c".repeat(24),
  authenticated: true,
  session: {
    sessionId: "6d1d4c2a-62a0-4b86-9d3f-0c6a3d0b5c11",
    expiresAt: "2026-10-03T08:00:00.000000Z",
    idleExpiresAt: "2026-10-02T20:30:00.000000Z",
    userId: "0f0e6a30-5d1c-4c7e-8b53-5b6b7f9a4a10",
    deviceId: "8f2a1d77-1c1b-4b0a-9a55-2f1d0e5c7b21",
    workspaceIds: ["3f1d3b1e-2a8c-4c44-a1c8-77a1e8f0b9a1"],
    recoveryGeneration: "18446744073709551615",
    purpose: "authenticate",
  },
  profile: {
    displayName: "Ada Lovelace",
    locale: "en-GB",
    timezone: "Europe/London",
    revision: "9007199254740993",
  },
};

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

async function chat(origin: string, observations: Observation[]) {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const problems = watchPage(page);
  const apiCalls: string[] = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.startsWith("/api/")) apiCalls.push(request.url());
  });
  try {
    await page.goto(origin);
    await page.getByRole("button", { name: "Send" }).waitFor();
    assert.equal(apiCalls.length, 0, "The page must send nothing before the user acts");
    const send = async (name: string, expected: string, scenario: string) => {
      await page.getByLabel("Name").fill(name);
      const [, ms] = await timed(async () => {
        await page.getByRole("button", { name: "Send" }).click();
        await page
          .getByRole("listitem")
          .filter({ hasText: expected })
          .last()
          .waitFor({ timeout: 10_000 });
      });
      observations.push({ scenario, real: true, milliseconds: ms, result: expected });
    };
    await send("ArcForges", "Hello, ArcForges!", "chat: greeting answered by the real AOT host");
    await send("ArcForges 世界", "Hello, ArcForges 世界!", "chat: Unicode name round trip");
    await send("", failureText.rejected, "chat: empty name, real InvalidArgument refusal");
    await send(
      "x".repeat(257),
      failureText.limit,
      "chat: 257 code units, real ResourceExhausted refusal",
    );
    assert.equal(apiCalls.length, 4);
    for (const url of apiCalls)
      assert.equal(new URL(url).pathname, "/api/arcforges.hello.v1.HelloService/SayHello");
    // Cancellation: the request is held back by the harness, then cancelled by the user before the host sees it.
    await page.route("**/api/**", async (route) => {
      await new Promise((resolveWait) => setTimeout(resolveWait, 1500));
      await route.continue().catch(() => undefined);
    });
    await page.getByLabel("Name").fill("slow");
    await page.getByRole("button", { name: "Send" }).click();
    await page.getByRole("button", { name: "Cancel" }).click();
    await page.getByRole("listitem").filter({ hasText: failureText.cancelled }).waitFor();
    await new Promise((resolveWait) => setTimeout(resolveWait, 2000));
    assert.equal(await page.getByRole("listitem").filter({ hasText: "Hello, slow!" }).count(), 0);
    observations.push({
      scenario: "chat: user cancellation of a held request (real host never answers the page)",
      real: false,
      milliseconds: undefined,
      result: failureText.cancelled,
    });
    assert.deepEqual(problems, [], "Console errors or CSP violations");
  } finally {
    await browser.close();
  }
}

async function account(origin: string, observations: Observation[]) {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const problems = watchPage(page);
  const csrfSeen: (string | undefined)[] = [];
  let stage: "authenticated" | "expired" | "down" | "malformed" = "authenticated";
  await page.route("**/session/v1/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/session/v1/bootstrap") {
      if (stage === "down") return route.fulfill({ status: 503, body: "down" });
      if (stage === "malformed")
        return route.fulfill({
          status: 200,
          contentType: "application/json",
          body: '{"csrfToken":"x"}',
        });
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: Buffer.from(serializeBrowserBootstrapResponseJson(authenticated)),
      });
    }
    assert.equal(path, "/session/v1/logout");
    csrfSeen.push(request.headers()["x-af-csrf"]);
    if (stage === "expired") return route.fulfill({ status: 401, body: "{}" });
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: Buffer.from(
        serializeBrowserReceiptJson({
          commandId: "7c9e6679-7425-40de-944b-e07fc1f90ae7",
          effect: "happened",
        }),
      ),
    });
  });
  try {
    const [, readMs] = await timed(async () => {
      await page.goto(origin);
      await page.getByText("Ada Lovelace").waitFor();
    });
    await page.getByText("18446744073709551615").waitFor();
    observations.push({
      scenario: "account: authenticated presentation with exact uint64 (labelled session fixture)",
      real: false,
      milliseconds: readMs,
      result: "Ada Lovelace; 18446744073709551615",
    });
    const [, outMs] = await timed(async () => {
      await page.getByRole("button", { name: "Sign out" }).click();
      await page.getByText("You are signed out. Reload to check again.").waitFor();
    });
    assert.deepEqual(csrfSeen, [authenticated.csrfToken]);
    observations.push({
      scenario: "account: sign out sends the bootstrap CSRF token (labelled fixture)",
      real: false,
      milliseconds: outMs,
      result: "signed out",
    });
    stage = "expired";
    await page.goto(origin);
    await page.getByRole("button", { name: "Sign out" }).click();
    await page.getByText("You are signed out. Reload to check again.").waitFor();
    assert.equal(await page.getByRole("alert").count(), 0);
    observations.push({
      scenario: "account: session expiry (logout 401) shown as ended (labelled fixture)",
      real: false,
      milliseconds: undefined,
      result: "ended, no alert",
    });
    stage = "down";
    await page.goto(origin);
    await page.getByRole("alert").filter({ hasText: failureText.unavailable }).waitFor();
    stage = "malformed";
    await page.getByRole("button", { name: "Try again" }).click();
    await page.getByRole("alert").filter({ hasText: failureText.malformed }).waitFor();
    observations.push({
      scenario: "account: unavailable then malformed bootstrap (labelled fixture)",
      real: false,
      milliseconds: undefined,
      result: "typed alerts",
    });
    assert.deepEqual(
      problems.filter((problem) => !/Failed to load resource/u.test(problem)),
      [],
      "Console errors or CSP violations",
    );
  } finally {
    await browser.close();
  }
}

async function main() {
  assert.notEqual(process.env.CI, "true", "The local browser run is opt-in, never CI.");
  const hostExe = process.env.AOT_HOST_EXE ?? "";
  assert(
    hostExe && existsSync(hostExe),
    "Set AOT_HOST_EXE to the Cloud Native AOT host executable.",
  );
  let child: ChildProcess | undefined;
  const servers: Server[] = [];
  const observations: Observation[] = [];
  try {
    child = spawn(hostExe, [], { stdio: ["ignore", "ignore", "inherit"], windowsHide: true });
    await waitForHealth(60_000);
    const identity = await (await fetch(`http://127.0.0.1:${hostPort}/healthz`)).text();
    const chatServer = await serve("chat");
    const accountServer = await serve("account");
    servers.push(chatServer.server, accountServer.server);
    await chat(chatServer.origin, observations);
    await account(accountServer.origin, observations);
    const output = join(app, "build", "local-run.json");
    mkdirSync(dirname(output), { recursive: true });
    writeFileSync(
      output,
      `${JSON.stringify({ hostHealth: JSON.parse(identity), observations }, null, 2)}\n`,
    );
    for (const observation of observations)
      console.log(
        `${observation.real ? "REAL   " : "FIXTURE"} ${observation.scenario}: ${observation.result}${observation.milliseconds === undefined ? "" : ` (${observation.milliseconds} ms)`}`,
      );
  } finally {
    for (const server of servers) server.close();
    if (child && child.exitCode === null) {
      child.kill();
      await new Promise((resolveWait) => child?.once("exit", resolveWait));
    }
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
