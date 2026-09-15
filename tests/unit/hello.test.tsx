// SPDX-License-Identifier: AGPL-3.0-only
// @vitest-environment jsdom
import "@testing-library/jest-dom/vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { greet } from "../../apps/site/app/hello";
import Hello from "../../apps/site/app/routes/hello";
afterEach(cleanup);
test("published protobuf round-trip preserves Unicode names", () => {
  expect(greet("  世界 🌍  ")).toBe("Hello, 世界 🌍!");
  expect(greet("🌍".repeat(80))).toBe(`Hello, ${"🌍".repeat(80)}!`);
  for (const invalid of [" ", "x".repeat(81), "hi\u0000"]) expect(() => greet(invalid)).toThrow();
});
test("form validates, recovers and renders untrusted names as text", () => {
  render(<Hello />);
  const input = screen.getByLabelText("Your name");
  fireEvent.change(input, { target: { value: "" } });
  fireEvent.click(screen.getByRole("button", { name: "Say hello" }));
  expect(screen.getByRole("alert")).toBeVisible();
  fireEvent.change(input, { target: { value: "<img src=x onerror=alert(1)>" } });
  fireEvent.click(screen.getByRole("button", { name: "Say hello" }));
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("Hello, <img src=x onerror=alert(1)>!");
  expect(document.querySelector("img")).toBeNull();
});
