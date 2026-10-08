# WEB.40 public Site parity with the React prerender

Status: recorded 2026-10-08 at source 80900a1 (WEB.40 unit U5, before the React sources were removed).

This record shows that the C# static Site (`ArcForges.Web.Site`, first-party Razor `HtmlRenderer`) publishes the same
public pages, texts, metadata, element structure, stylesheet bytes, public files and security header values as the React
prerender it replaces. It is the evidence that allowed `apps/site`, `apps/app` and `packages/ui` to be removed.

## Method

- React reference: `npm run build --workspace @arcforges/web-site` with `VITE_SOURCE_REF=80900a1430c0fbb483e4a220e20980e3c18728b3`
  (React Router prerender, Vite 8.3.0, Tailwind v4.3.3). The published client directory was copied before removal.
- C# output: `dotnet tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll site build --out <new-directory> --source-ref 80900a1430c0fbb483e4a220e20980e3c18728b3`,
  built twice with identical trees, after the stylesheet change below.
- Comparison: HTML parsed with the Python standard library. Compared per page: title, `lang`, every `meta`, every element
  with its sorted attributes in document order (excluding `<script>` and `modulepreload` links; the stylesheet `href` is
  compared as a placeholder because it is content-hashed), visible text nodes, `noscript` text, anchors, form controls,
  landmark elements and SVG count. The CSS was compared byte for byte and, separately, rule by rule after normalising
  number, quote and pseudo-element spelling.

## Results

| Item | React prerender | C# Site | Result |
| --- | --- | --- | --- |
| `/` (index.html) | 99 elements, 17 text nodes | 99 elements, 17 text nodes | Element skeleton equal; text equal; metadata equal |
| `/hello/` | 112 elements, 19 text nodes | 112 elements, 19 text nodes | Element skeleton equal; text equal; metadata equal |
| `/cloud-hello/` | 94 elements, 16 text nodes | 94 elements, 16 text nodes | Element skeleton equal; text equal; metadata equal |
| `404.html`, `404.css`, `robots.txt`, `favicon.svg` | published | published | Byte identical |
| Stylesheet | `assets/root-BPtceTrQ.css` (8973 bytes) | `assets/site.d4912dbd6dc5e22b.css` (8973 bytes) | Byte identical content (name is content-hashed) |
| `_headers` security and cache lines | 17 lines (template in `tooling/project.ts`) | 17 lines (`SiteSecurityHeaders.cs`) | Identical lines; only the CSP differs (below) |

The title strings are `Hello, world. — ArcForges`, `Your hello — ArcForges` and `Server connection — ArcForges`.

## Stylesheet change made for parity

The first C# stylesheet was a hand-written approximation of the Tailwind preflight. It differed from the React output in the
system font stack, the placeholder colour (`color-mix`), button transitions and the rule set (78 selectors against 93).
Those differences change rendering, so the C# Site now embeds the React build's Tailwind output bytes unchanged
(`src/ArcForges.Web.Site/Assets/site.css`, SHA-256 `d4912dbd6dc5e22b…`). Its source is `packages/ui/src/styles.css` at
80900a1, which is removed with `packages/ui`. Regeneration needs the Tailwind licence notice (MIT, already published in
`third-party-notices.txt`). The bytes are otherwise unchanged; no source is edited.

## Accepted differences (by design, TB-01)

- **Hydration scripts.** The React pages carried 5 inline hydration scripts and 7 or 8 `modulepreload` links each, and
  their CSP `script-src` listed one SHA-256 for each inline script body (seven hashes in total). The C# pages carry no
  script element, so the CSP is `script-src 'self'`. Every other CSP directive and every other header line is identical.
  The public pages contain no JavaScript (TB-01, P2-021 item 2).
- **Interactive controls.** On the React pages the `/hello` input and button, and the `/cloud-hello` check button, were
  rendered disabled and enabled only after hydration. The static Site keeps the same disabled initial markup and cannot
  enable it. The live greeting and connection checks have no static-page equivalent. Where they move is an open decision
  recorded in the WEB.40 unit report; the coordinator must decide between a Blazor route in `ArcForges.Web.App` and
  retirement. The public pages do not regress in content.
- **Output files not published.** The React build also emitted `__spa-fallback.html`, which the candidate never published.
  It has no C# counterpart.

## Consequence

After this record, `apps/site`, `apps/app` and `packages/ui` may be removed. The C# Site output is the only public
output. Byte parity with the React output is not claimed for the HTML pages, because their markup differs by the
hydration nodes listed above. Content parity is claimed for every public page as described.
