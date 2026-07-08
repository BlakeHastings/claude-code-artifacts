---
name: generative-svg-marks
description: Build seeded, reproducible generative SVG logo/mark generators — brute-force "gallery" tools that roll many candidate marks you can lock, tweak, and export. Use when creating a generative logo lab, procedural emblem/icon generator, parametric mark explorer, constellation/orbit/spirograph mark tool, or any UI that turns parameters into SVG and needs reproducible seeds, a per-item editor, rotational symmetry, or PNG/SVG export in the browser. Covers seeded PRNG determinism (mulberry32, deriveSpec/renderSpec split, preserving RNG draw order), rotational/mirror symmetry via SVG defs+use, client-side SVG→PNG rasterization, in-page color controls that avoid OS-dialog freezes, and a Svelte-5 localStorage autosave pattern. Also rendering a generated mark outside the lab (as an <img> or data-URI, where page var()/currentColor don't resolve so colors must be baked), coloring a mark from a CSS design token via an ink override, and building a generative favicon that rolls a fresh mark each page load.
---

# Generative SVG mark generators

Patterns for tools that roll a wall of candidate marks from a seed, let you lock the
keepers, edit one in detail, and export SVG/PNG. Framework-agnostic core; Svelte-5
notes flagged where relevant.

## 1. Seeded determinism (reproducible galleries)

Use a tiny seeded PRNG so a seed always reproduces a mark. `mulberry32`:

```js
function mulberry32(seed) {
  let a = seed | 0;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
```

Derive a whole gallery from one **base seed**: `seedFor(base, i)` gives each tile a
stable seed; show it in base-36 so a user can type a base back in and reproduce the
wall. Regenerate = new random base; lock = keep that tile's seed while others reroll.

**Split derive from render.** `deriveSpec(seed, globals) → spec` rolls every knob from
the seed into an explicit object; `renderSpec(spec) → svg` draws a spec with no
randomness. The gallery does `renderSpec(deriveSpec(...))`; a per-mark editor hands an
edited spec straight to `renderSpec`. This is what makes marks editable without losing
reproducibility.

**Preserve RNG draw order when refactoring** the derivation. The sequence of `rnd()`
calls *is* the mapping from seed to mark. Reordering calls, or changing a
short-circuited draw like `order <= 4 && rnd() < 0.35` (which only draws when the left
side is true), silently changes what every existing seed produces. Add new fields as
constants that consume **no** draw, and keep the old call order intact, so previously
liked seeds still render identically.

## 2. Rotational / mirror symmetry (data → emblem)

The cheapest way to turn one path into a balanced emblem: define the path once in
`<defs>` and replicate it with `<use>` + a `rotate` transform. Use `currentColor` so
each copy can be recolored via the `<use>`'s `style="color:…"`.

```js
const fid = `a${uniquePerMark}`;               // unique id — see gotcha below
let uses = '';
for (let k = 0; k < order; k++) {
  const deg = (360 / order) * k;
  uses += `<use href="#${fid}" transform="rotate(${deg} ${cx} ${cy})" style="color:${ink}"/>`;
}
// mirror copy: rotate(...) translate(VB 0) scale(-1 1)  reflects about x = VB/2 then rotates
const svg = `<svg viewBox="0 0 ${VB} ${VB}"><defs><g id="${fid}">${path}</g></defs>${uses}</svg>`;
```

**Unique-id gotcha:** many inline SVGs on one page share the DOM, so `<use href="#id">`
with a duplicate `id` resolves to the first match. Derive the id from the mark
(seed/params), not a constant, or every tile shows tile #1's shape.

**Rounding:** round coordinates (`Math.round(n*100)/100`) to keep the SVG small.
For smooth curves through sampled points use a Catmull-Rom spline (it interpolates, so
it still passes through every point); expose the tension as a "smoothing" control.

## 3. Client-side SVG → PNG export

Rasterize the exact SVG string via a canvas — no library, fully offline:

```js
async function svgToPng(svg, size) {
  const sized = svg.replace('<svg ', `<svg width="${size}" height="${size}" `); // give it intrinsic size
  const url = URL.createObjectURL(new Blob([sized], { type: 'image/svg+xml;charset=utf-8' }));
  try {
    const img = new Image();
    await new Promise((ok, no) => { img.onload = ok; img.onerror = no; img.src = url; });
    const c = document.createElement('canvas'); c.width = c.height = size;
    c.getContext('2d').drawImage(img, 0, 0, size, size);
    return await new Promise((ok, no) => c.toBlob(b => b ? ok(b) : no(Error('encode')), 'image/png'));
  } finally { URL.revokeObjectURL(url); }
}
```

- The SVG **must** carry width/height (or the Image renders at 0×0). Inject them.
- Keep the SVG self-contained (no external fonts/images). Referencing an external
  resource taints the canvas and `toBlob` throws. Internal `<defs>`/`<use>` are fine.
- A transparent background is preserved automatically (great for logos); a background
  `<rect fill>` inside the SVG bakes in.
- `<use href="#id">` referencing internal `<defs>` rasterizes correctly.

## 4. Color controls: avoid OS dialogs

**Negative learning.** The native `<input type="color">` and the `EyeDropper` API both
open an OS-level modal. On Windows this can leave the browser stuck — clicks do nothing
and produce the system error "ding" — after picking. The failure is at the OS level, so
`try/catch` in JS cannot recover it. Do **not** rely on them for a smooth in-app color
UX.

Reliable alternative, entirely in-page: a row of **preset swatches** (buttons) plus a
**hex text field** that applies once the value parses as a complete hex. This covers
"pick any color" and "reuse an exact color" without any OS dialog. If you need a visual
picker, build an inline HSV/hue widget in the DOM rather than calling the native one.

## 5. Svelte 5 lab plumbing

- **Deterministic first paint.** Initialize state from a fixed constant seed (roll
  randomness only inside event handlers), so SSR/prerender and hydration match. A
  `Math.random()` in a `$state` initializer causes a hydration mismatch.
- **localStorage autosave.** Load in `onMount`; save in an `$effect`. Gate the save
  behind a `loaded` flag so the effect's first run does not clobber saved data before
  `onMount` reads it — but build the snapshot (reading every field, incl.
  `$state.snapshot(deepObject)` for deep tracking) **before** the `if (!loaded) return`,
  or the effect won't subscribe to those fields.

```js
let loaded = false;
onMount(() => { /* read localStorage, hydrate state */ loaded = true; });
$effect(() => {
  const snap = { seeds, locked, overrides: $state.snapshot(overrides), /* … */ };
  if (!browser || !loaded) return;          // deps already read above → still tracked
  localStorage.setItem(KEY, JSON.stringify(snap));
});
```

- **JSON import** of an array-of-tuples widens to `number[]`; cast `raw as unknown as T`.
- Binding to a nested `$state` proxy member (`bind:value={overrides[i].bg.size}`) works
  via deep reactivity, so a lightbox editor can mutate a spec and the gallery tile
  updates live.

## 6. Rendering a generated mark outside the lab

To drop a generated SVG string into a normal page, prefer an `<img>` over inline HTML:

- `{@html svgString}` in Svelte trips eslint `svelte/no-at-html-tags` (an **error**, not
  a warning). Use `<img src={url}>` instead — either import the file as a URL
  (`import mark from './mark.svg'`, which Vite gives you as a URL) or build a data URI:
  `` `data:image/svg+xml,${encodeURIComponent(svg)}` ``. An `<img>` also rasterizes
  cleanly for headless-browser (Playwright) and `html-to-image` capture.

- **Isolation gotcha.** An SVG rendered via `<img>` or a data URI (this includes
  `<link rel="icon">`) renders in an **isolated document**: CSS custom properties
  (`var(--x)`) and any `currentColor` inherited from the page do **not** resolve there.
  A mark styled `color: var(--brand)` shows as nothing. Only *inline* SVG in the DOM
  inherits page vars/currentColor. So for any img/data-URI use, **bake a concrete color**
  at generation time.

- **Color the mark from a design token, without hardcoding it.** Give the generator an
  optional `ink` override and resolve the token at runtime:
  `getComputedStyle(document.documentElement).getPropertyValue('--color-primary').trim()`.
  Pass `ink: brandColor() || undefined` so it falls back to the palette default during
  SSR / non-browser. Current browsers render `oklch()` fine inside an isolated data-URI
  SVG, so you can bake the raw oklch token value straight in — no hex conversion needed
  (verify in the target browsers if you must support old ones).

## 7. Generative favicon (a fresh mark per page load)

A per-load random favicon is a nice fit for a generative-brand site. Mechanics:

- **Generate client-side in `onMount`.** A prerendered/SSR page bakes one seed at build
  time (same icon for everyone); rolling on the client gives a new mark each refresh and
  sidesteps a hydration mismatch. Lazy-import the generator so its code + any data stay
  out of the initial bundle.
- **Bake the color** (§6 isolation) — the `<link rel="icon">` data URI is isolated.
- **Bold stroke.** Favicons render ~16px, so use a much heavier stroke than a normal
  mark (well past a lab's UI max) or the fine lines disappear. The function accepts any
  stroke value even if the UI slider caps it.
- **Set it** as `` `data:image/svg+xml,${encodeURIComponent(svg)}` `` on a reactive
  `<link rel="icon" href>`; updating the href swaps the tab icon.
- **Two rough edges to avoid:** (a) render *no* `<link rel="icon">` until the mark is
  ready (`{#if faviconHref}`), or a static fallback flashes first (its own dark
  background reads as a "black box"); (b) run the generation inside `requestIdleCallback`
  (with a `setTimeout` fallback) so the import + render don't jank the page's opening
  animation frames.
- **Verify** in a headless browser: read `link[rel=icon]`'s href (assert it became a
  `data:image/svg+xml` and no longer contains the old hardcoded color), then render that
  href as a large `<img>` and screenshot it to eyeball the actual mark.
