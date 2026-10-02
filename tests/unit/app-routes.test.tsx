// SPDX-License-Identifier: AGPL-3.0-only
// @vitest-environment jsdom
import "@testing-library/jest-dom/vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import Account from "../../apps/app/app/routes/account";
import Chat from "../../apps/app/app/routes/chat";
import { failureText } from "../../apps/app/app/probe/failure.ts";
import { csrfHeader } from "../../apps/app/app/probe/session.ts";
import { frame, helloResponse } from "../fixtures/grpc-web.ts";
import {
  anonymous,
  authenticated,
  bootstrapBody,
  jsonResponse,
  receiptBody,
} from "../fixtures/app-probe.ts";

const grpcWeb = { "content-type": "application/grpc-web+proto" };
let fetcher: ReturnType<typeof vi.fn<typeof fetch>>;

beforeEach(() => {
  fetcher = vi.fn<typeof fetch>();
  window.fetch = fetcher as unknown as typeof fetch;
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

const requestOf = (call: number) => {
  const [input, init] = fetcher.mock.calls[call] as [RequestInfo | URL, RequestInit | undefined];
  return new Request(input, init);
};

test("account reads the session once on open and presents it as text with the exact generation", async () => {
  fetcher.mockResolvedValue(jsonResponse(bootstrapBody(authenticated)));
  render(<Account />);
  expect(screen.getByText("Checking your session…")).toBeVisible();
  await screen.findByText("Ada Lovelace");
  expect(fetcher).toHaveBeenCalledTimes(1);
  expect(requestOf(0).url).toBe(`${window.location.origin}/session/v1/bootstrap`);
  expect(screen.getByText("18446744073709551615")).toHaveClass("exact");
  expect(screen.getByText("en-GB")).toBeVisible();
  expect(screen.getByText("Europe/London")).toBeVisible();
  expect(screen.getByText(authenticated.session.expiresAt)).toBeVisible();
  const text = document.body.textContent ?? "";
  for (const secret of [
    authenticated.csrfToken,
    authenticated.session.sessionId,
    authenticated.session.userId,
    authenticated.session.deviceId,
  ])
    expect(text).not.toContain(secret);
});

test("account renders an anonymous visitor with no sign-out control", async () => {
  fetcher.mockResolvedValue(jsonResponse(bootstrapBody(anonymous)));
  render(<Account />);
  await screen.findByText("You are not signed in.");
  expect(screen.queryByRole("button")).toBeNull();
});

test("account renders display text from the server as text, never markup", async () => {
  const hostile = {
    ...authenticated,
    profile: { ...authenticated.profile, displayName: "<img src=x onerror=alert(1)>" },
  };
  fetcher.mockResolvedValue(jsonResponse(bootstrapBody(hostile)));
  render(<Account />);
  await screen.findByText("<img src=x onerror=alert(1)>");
  expect(document.querySelector("img")).toBeNull();
});

test("sign out posts the CSRF token from this page's bootstrap and then shows the ended state", async () => {
  fetcher
    .mockResolvedValueOnce(jsonResponse(bootstrapBody(authenticated)))
    .mockResolvedValueOnce(jsonResponse(receiptBody("happened")));
  render(<Account />);
  fireEvent.click(await screen.findByRole("button", { name: "Sign out" }));
  await screen.findByText("You are signed out. Reload to check again.");
  expect(fetcher).toHaveBeenCalledTimes(2);
  const logout = requestOf(1);
  expect(logout.method).toBe("POST");
  expect(logout.url).toBe(`${window.location.origin}/session/v1/logout`);
  expect(logout.headers.get(csrfHeader)).toBe(authenticated.csrfToken);
  expect(screen.queryByText("Ada Lovelace")).toBeNull();
});

test("a session that ended on the server is shown as ended, not as a transient fault", async () => {
  fetcher
    .mockResolvedValueOnce(jsonResponse(bootstrapBody(authenticated)))
    .mockResolvedValueOnce(new Response("{}", { status: 401 }));
  render(<Account />);
  fireEvent.click(await screen.findByRole("button", { name: "Sign out" }));
  await screen.findByText("You are signed out. Reload to check again.");
  expect(screen.queryByRole("alert")).toBeNull();
});

test("a refused or unconfirmed sign out keeps the session visible and says so", async () => {
  for (const [answer, text] of [
    [() => new Response("{}", { status: 403 }), failureText.forbidden],
    [() => jsonResponse(receiptBody("didNotHappen")), failureText.rejected],
    [() => jsonResponse(receiptBody("unknown")), failureText.unexpected],
  ] as const) {
    cleanup();
    fetcher.mockReset();
    fetcher.mockResolvedValueOnce(jsonResponse(bootstrapBody(authenticated)));
    fetcher.mockResolvedValueOnce(answer());
    render(<Account />);
    fireEvent.click(await screen.findByRole("button", { name: "Sign out" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(text);
    expect(screen.queryByText("You are signed out. Reload to check again.")).toBeNull();
  }
});

test("a failed read is announced and can be retried", async () => {
  fetcher
    .mockResolvedValueOnce(new Response("down", { status: 503 }))
    .mockResolvedValueOnce(jsonResponse(bootstrapBody(anonymous)));
  render(<Account />);
  expect(await screen.findByRole("alert")).toHaveTextContent(failureText.unavailable);
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  await screen.findByText("You are not signed in.");
  expect(fetcher).toHaveBeenCalledTimes(2);
});

test("an unreadable answer is shown as malformed and leaves no session", async () => {
  fetcher.mockResolvedValue(jsonResponse('{"csrfToken":"x"}'));
  render(<Account />);
  expect(await screen.findByRole("alert")).toHaveTextContent(failureText.malformed);
});

test("leaving the page cancels the read that is still in flight", async () => {
  let signal: AbortSignal | undefined;
  fetcher.mockImplementation(
    (_input, init) =>
      new Promise<Response>((_resolve, reject) => {
        signal = init?.signal ?? undefined;
        init?.signal?.addEventListener("abort", () => reject(new DOMException("a", "AbortError")));
      }),
  );
  const view = render(<Account />);
  await waitFor(() => expect(signal).toBeDefined());
  view.unmount();
  expect(signal?.aborted).toBe(true);
});

test("chat sends nothing until asked, then shows both sides of one anonymous exchange", async () => {
  fetcher.mockImplementation(
    async () => new Response(helloResponse("Hello, ArcForges!") as BodyInit, { headers: grpcWeb }),
  );
  render(<Chat />);
  expect(fetcher).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Send" }));
  await screen.findByText("Hello, ArcForges!");
  expect(fetcher).toHaveBeenCalledTimes(1);
  expect(requestOf(0).url).toBe(
    `${window.location.origin}/api/arcforges.hello.v1.HelloService/SayHello`,
  );
  expect(requestOf(0).credentials).toBe("omit");
  const items = screen.getAllByRole("listitem").map((item) => item.textContent);
  expect(items).toEqual(["youArcForges", "serverHello, ArcForges!"]);
});

test("chat shows the server's typed refusal as a fixed notice and keeps working", async () => {
  fetcher
    .mockResolvedValueOnce(
      new Response(
        frame(
          new TextEncoder().encode(
            "grpc-status: 3\r\ngrpc-message: Name%20must%20not%20be%20empty.\r\n",
          ),
          0x80,
        ) as BodyInit,
        { headers: grpcWeb },
      ),
    )
    .mockResolvedValueOnce(
      new Response(helloResponse("Hello, again!") as BodyInit, { headers: grpcWeb }),
    );
  render(<Chat />);
  const input = screen.getByLabelText("Name");
  fireEvent.change(input, { target: { value: "" } });
  fireEvent.click(screen.getByRole("button", { name: "Send" }));
  await screen.findByText(failureText.rejected);
  expect(document.body.textContent).not.toContain("must not be empty");
  fireEvent.change(input, { target: { value: "again" } });
  fireEvent.click(screen.getByRole("button", { name: "Send" }));
  await screen.findByText("Hello, again!");
});

test("chat can cancel a pending message, reports it as cancelled and does not double-send", async () => {
  let signal: AbortSignal | undefined;
  fetcher.mockImplementation(
    (_input, init) =>
      new Promise<Response>((_resolve, reject) => {
        signal = init?.signal ?? undefined;
        init?.signal?.addEventListener("abort", () => reject(new DOMException("a", "AbortError")));
      }),
  );
  render(<Chat />);
  fireEvent.click(screen.getByRole("button", { name: "Send" }));
  const send = await screen.findByRole("button", { name: "Sending…" });
  expect(send).toBeDisabled();
  fireEvent.submit(send.closest("form") as HTMLFormElement);
  expect(fetcher).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  await screen.findByText(failureText.cancelled);
  expect(signal?.aborted).toBe(true);
  expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
  expect(screen.getByRole("button", { name: "Send" })).toBeEnabled();
});

test("chat renders untrusted names as text and keeps only the last twenty entries", async () => {
  fetcher.mockImplementation(async (input, init) => {
    const body = new Uint8Array(await new Request(input, init).arrayBuffer());
    const name = new TextDecoder().decode(body.slice(7));
    return new Response(helloResponse(`Hello, ${name}!`) as BodyInit, { headers: grpcWeb });
  });
  render(<Chat />);
  const input = screen.getByLabelText("Name");
  fireEvent.change(input, { target: { value: "n" } });
  for (let index = 0; index < 11; index++) {
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Send" })).toBeEnabled());
  }
  expect(screen.getAllByRole("listitem")).toHaveLength(20);
  expect(fetcher).toHaveBeenCalledTimes(11);
  fireEvent.change(input, { target: { value: "<img src=x onerror=alert(1)>" } });
  fireEvent.click(screen.getByRole("button", { name: "Send" }));
  await waitFor(() =>
    expect(screen.getAllByText("<img src=x onerror=alert(1)>").length).toBeGreaterThan(0),
  );
  expect(document.querySelector("img")).toBeNull();
});
