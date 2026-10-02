// SPDX-License-Identifier: AGPL-3.0-only
import type { Config } from "@react-router/dev/config";
import { activeProfile } from "./profile.ts";

export default {
  ssr: false,
  prerender: ["/"],
  buildDirectory: `build/${activeProfile()}`,
} satisfies Config;
