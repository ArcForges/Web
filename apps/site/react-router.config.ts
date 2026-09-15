// SPDX-License-Identifier: AGPL-3.0-only
import type { Config } from "@react-router/dev/config";

export default { ssr: false, prerender: ["/", "/hello", "/cloud-hello"] } satisfies Config;
