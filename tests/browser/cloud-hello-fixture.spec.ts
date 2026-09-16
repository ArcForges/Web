// SPDX-License-Identifier: AGPL-3.0-only
// Explicit browser wire fixtures; these do not call or validate a C# container.
import { expect, test } from "@playwright/test";
import { helloApiPath } from "../../apps/site/app/cloud-hello";
import { decodeHelloRequest, helloResponse } from "../fixtures/grpc-web";

test("published client reports unavailable API, then accepts a real protobuf response fixture", async ({
  page,
  request,
}) => {
  // Candidate-only: before Cloud owns /api/*, static assets must not fake an API response.
  const missingApi = await request.get(helloApiPath);
  expect(missingApi.status()).toBe(404);
  expect(await missingApi.text()).toContain("Page not found");
  expect((await request.post(helloApiPath)).status()).toBe(405);
  let requests = 0;
  let unavailable = true;
  await page.route("**/api/**", async (route) => {
    requests++;
    const request = route.request();
    expect(new URL(request.url()).pathname).toBe(helloApiPath);
    expect(request.method()).toBe("POST");
    expect(request.headers()["content-type"]).toContain("application/grpc-web+proto");
    expect(request.headers().authorization).toBeUndefined();
    expect(decodeHelloRequest(request.postDataBuffer() ?? new Uint8Array()).name).toBe("ArcForges");
    if (unavailable) await route.fulfill({ status: 503, body: "Unavailable" });
    else
      await route.fulfill({
        status: 200,
        contentType: "application/grpc-web+proto",
        body: Buffer.from(helloResponse()),
      });
  });
  await page.goto("/cloud-hello/");
  const button = page.getByRole("button", { name: "Check connection" });
  await expect(button).toBeEnabled();
  expect(requests).toBe(0);
  await button.click();
  await expect(page.getByRole("alert")).toContainText("server is unavailable");
  expect(requests).toBe(1);
  unavailable = false;
  await button.click();
  await expect(page.getByRole("status")).toContainText("Hello, ArcForges!");
  expect(requests).toBe(2);
});
