// SPDX-License-Identifier: AGPL-3.0-only
import { index, type RouteConfig } from "@react-router/dev/routes";
import { activeProfile } from "../profile.ts";

// One profile per build: only that profile's route module enters the bundle graph.
export default [index(`routes/${activeProfile()}.tsx`)] satisfies RouteConfig;
