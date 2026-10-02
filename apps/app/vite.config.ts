// SPDX-License-Identifier: AGPL-3.0-only
import { reactRouter } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

// Unlike the site candidate, the profile builds are measured, not sealed or deployed, so they do not
// carry the candidate's browser-graph observer (see docs/prf-08-profile-proof.md).
export default defineConfig({
  plugins: [tailwindcss(), reactRouter()],
  build: { sourcemap: false },
});
