// SPDX-License-Identifier: AGPL-3.0-only
import type { Config } from "@react-router/dev/config";
import { allPrerenderPaths } from "./app/site-manifest";

export default { ssr: false, prerender: allPrerenderPaths() } satisfies Config;
