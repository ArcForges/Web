// SPDX-License-Identifier: AGPL-3.0-only
import { defineConfig } from "vitest/config";
export default defineConfig({
  test: { include: ["tests/unit/**/*.test.{ts,tsx}"], environment: "node", restoreMocks: true },
});
