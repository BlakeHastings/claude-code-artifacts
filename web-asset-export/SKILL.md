---
name: web-asset-export
description: Generate pixel-perfect image assets (social/channel banners, avatars, OG images, brand art) by rendering them in a web page and screenshotting with a headless browser. Use when you need exact-dimension PNGs from HTML/CSS/SVG/Svelte, hi-DPI / 2x exports, a manifest-driven asset gallery with a raw export route, safe-zone overlays for platform crops, or an in-page download button. Covers Playwright element.screenshot at deviceScaleFactor, a raw-mode route plus a JSON manifest endpoint shared by the exporter and the preview UI, canvas devicePixelRatio for crisp 2x, html-to-image for in-browser downloads, and reading the exported PNG back to verify it visually.
---

# Rendering web pages to pixel-perfect image assets

Design an asset in HTML/CSS/SVG/Svelte, then screenshot it with a real headless browser
for a pixel-exact PNG. A browser handles fonts, `oklch`, gradients, and live `<canvas>`
natively — which is why DOM-to-image libraries fight you, and the headless-browser path
is the authoritative one.

## The pipeline

1. **Manifest** — one array of asset specs (`id`, `width`, `height`, `format`, optional
   `scale`, safe zone). The single source of truth for both the UI and the exporter.
2. **A route with two modes:**
   - *Gallery* (no params): every asset previewed, scaled to fit, with labels + safe-zone
     overlays. The surface you iterate on.
   - *Raw* (`?asset=<id>&raw=1`): that one asset alone, at exact pixel size, pinned
     top-left, zero page chrome. Its root carries `data-brand-asset={id}`. This is what
     the exporter screenshots.
3. **Exporter** — a Node + Playwright script that boots the dev server, reads the
   manifest, and screenshots each raw URL.

## Exporter script (Playwright)

```js
const dsf = asset.scale ?? 1;                       // pixel-density multiplier
const page = await browser.newPage({
  viewport: { width: asset.width, height: asset.height },
  deviceScaleFactor: dsf,                            // output = width*dsf × height*dsf
});
await page.goto(`${BASE}/brand?asset=${asset.id}&raw=1`, { waitUntil: 'networkidle' });
const el = page.locator('[data-brand-asset]');
await el.waitFor({ state: 'visible' });
await page.evaluate(() => document.fonts.ready);    // fonts painted
await page.waitForTimeout(350);                      // let any <canvas> paint its frame
await el.screenshot({ path, type: asset.format === 'jpg' ? 'jpeg' : 'png' });
```

- `deviceScaleFactor: 1` gives exactly `width × height`. Set `scale > 1` per asset for
  platforms that upscale a 1x upload on hi-DPI displays (a LinkedIn cover uploaded at
  1128×191 blurs; export 2× → 2256×382, same aspect, crisp).
- Element screenshot of a `position: fixed` top-left node, with the viewport equal to the
  asset size, yields an exact capture.
- Boot the dev server from the script: `spawn('npm',['run','dev','--','--port',P,
  '--strictPort','--host','localhost'])`, poll a URL until it 200s, and kill the process
  tree on exit (Windows: `taskkill /pid <pid> /T /F`).

## The manifest bridge (don't duplicate the dimension list)

The Node exporter and the Svelte UI must share one manifest. Expose it from the app as a
JSON endpoint the script fetches, instead of importing the TS manifest into Node:

```ts
// src/routes/brand/assets.json/+server.ts
import { json } from '@sveltejs/kit';
import { ASSETS } from '$lib/brand/manifest.js';
export const prerender = true;
export function GET() { return json(ASSETS.map(a => ({ ...a, filename: `${a.id}.png` }))); }
```

The script does `await (await fetch(`${BASE}/brand/assets.json`)).json()`. One source of
truth; change a dimension once.

## Preview scaling without distorting the capture

Render the artwork at **true px** and shrink only a wrapper:

```svelte
<div style="transform:scale({displayW/spec.width});transform-origin:top left;
            width:{spec.width}px;height:{spec.height}px">
  <Artwork {spec} />   <!-- the data-brand-asset node stays untransformed -->
</div>
```

The captured node itself carries no transform, so a full-size capture (Playwright or
html-to-image) is unaffected by the preview scale.

## Crisp `<canvas>` at 2x

An artwork `<canvas>` upscales blurry when exported at `deviceScaleFactor > 1` unless it
renders at device pixels: `canvas.width = w * dpr; canvas.height = h * dpr;
ctx.setTransform(dpr,0,0,dpr,0,0)` and draw in logical units. Use
`dpr = Math.min(window.devicePixelRatio || 1, 3)` — in the headless export the
devicePixelRatio equals the page's `deviceScaleFactor`.

## In-page download (convenience, not the source of truth)

A "Download" button via `html-to-image` grabs the same node client-side:

```js
const { toPng } = await import('html-to-image');
await toPng(node, { width, height, canvasWidth: width*scale, canvasHeight: height*scale,
                    pixelRatio: scale, backgroundColor: bg, cacheBust: true });
```

- Render artwork as inline DOM / `<img>` (not `{@html}` — see the generative-svg-marks
  skill). html-to-image embeds fonts and images itself.
- It's a **convenience**; the Playwright CLI is authoritative. html-to-image can diverge —
  e.g. it 404s trying to fetch an internal SVG filter ref (`url(#n)`) while cloning.

## Verify by reading the PNG back

After exporting, **Read the PNG file** (the agent Read tool renders images) to judge it
visually, and check dimensions (`System.Drawing.Image` on Windows, or ImageMagick
`identify`). For the whole gallery/concept sheet, screenshot the page to a scratchpad
PNG and Read that. This tight "render → read back → adjust" loop is the core of getting
visual output right; don't judge a generated/exported asset without looking at it.
