// SPDX-License-Identifier: AGPL-3.0-only
export default {
  /**
   * @param {Request} request
   * @param {{ ASSETS: { fetch(request: Request): Promise<Response> } }} env
   * @returns {Promise<Response>}
   */
  async fetch(request, env) {
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
