// SPDX-License-Identifier: AGPL-3.0-only
import { index, route, type RouteConfig } from "@react-router/dev/routes";
export default [
  index("routes/home.tsx"),
  route("hello", "routes/hello.tsx"),
  route("cloud-hello", "routes/cloud-hello.tsx"),
] satisfies RouteConfig;
