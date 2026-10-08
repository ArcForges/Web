// SPDX-License-Identifier: AGPL-3.0-only
// Canonical-host adapter. Its JavaScript form is emitted into the candidate as worker/index.js
// by tooling/project.ts (Node type stripping, no bundling); the behaviour below is the reviewed source.
interface Env {
  ASSETS: { fetch(request: Request): Promise<Response> };
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.hostname === "www.arcforges.com") {
      url.protocol = "https:";
      url.hostname = "arcforges.com";
      url.port = "";
      return Response.redirect(url.href, 308);
    }
    return env.ASSETS.fetch(request);
  },
};
