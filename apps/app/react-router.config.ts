// SPDX-License-Identifier: AGPL-3.0-only
import type { Config } from "@react-router/dev/config";
import { activeProfile } from "./profile.ts";

// Each profile runs on its own path of one origin (CLOUD.71 serves both from the proof origin), so its router
// base is that path. Code, chunk names and stylesheet are unchanged by the base (it is not applied to Vite's
// asset base: references stay /assets/...); the prerendered page is written to `<profile>/index.html` and carries
// the base. A profile whose base is `/` answers "Page not found" on any other path.
export default {
  ssr: false,
  prerender: ["/"],
  basename: `/${activeProfile()}/`,
  buildDirectory: `build/${activeProfile()}`,
} satisfies Config;
