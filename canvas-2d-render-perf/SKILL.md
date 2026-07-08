---
name: canvas-2d-render-perf
description: >-
  Performance techniques for canvas 2D render loops that draw thousands of small elements
  (star fields, particle systems, point clouds, scatter plots) and are CPU-bound or janky,
  especially on low-end devices. Use when a requestAnimationFrame canvas loop is slow, when
  optimizing a per-frame draw over many points, or when deciding what is actually expensive
  (per-element trig/projection vs canvas draw calls). Covers caching a slowly-changing
  projection and reprojecting at a low rate, sprite blitting (drawImage) instead of
  per-element beginPath/arc/fill and the per-element fillStyle color-string parse it avoids,
  color-bucket sprite atlases, sine/periodic lookup tables with bitwise-mask indexing, and
  decomposing cost before optimizing.
---

# Canvas 2D render-loop performance for many small elements

For a `requestAnimationFrame` loop that draws thousands of tiny elements (stars, particles,
points) onto a `<canvas>` 2D context and is the thing eating CPU — particularly the loop that
runs continuously (a background that never parks), which dominates idle cost on a low-end
device.

## Decompose the cost before optimizing

Per-frame cost splits into two buckets that need different fixes:

- **Per-element math** — projection, trig, distance, layout per element per frame.
- **Canvas draw calls** — `beginPath`/`arc`/`fill`/`drawImage` and context state changes.

Find which dominates before touching code. Don't guess; the two fixes are unrelated.

- DevTools Performance, **CPU throttle 4-6x** to emulate a low-end browser, then read
  **self-time** inside the draw function: arithmetic-heavy self-time → math-bound; time inside
  `CanvasRenderingContext2D.fill`/`arc` → draw-bound. The GPU track shows raster separately.
- Or decompose by **toggling**: skip the draw calls to isolate the math cost; freeze the math
  (reuse last frame's positions) to isolate the draw cost. The subtraction is more trustworthy
  than instrumenting with `performance.now()` markers, which perturb the measurement and miss
  GPU raster anyway.

## Technique 1 — cache a slowly-changing projection

If each element's screen position depends on inputs that change **far slower than the frame
rate** (a celestial drift of ~0.04 px/sec, a slow camera pan), recomputing the full projection
every frame is wasted work. Compute it at a **low rate** and reuse it.

- Split the loop into `project()` (the heavy per-element trig → store each element's base
  screen position) and `draw()` (cheap per-frame work). Call `project()` only every ~100ms,
  or when a dirty flag is set; reuse the cached positions on the frames in between.
- Apply genuinely-per-frame inputs that are **uniform translations/transforms** (mouse
  parallax offset, a scroll offset) at draw time against the cached positions — they don't
  require reprojection: `sx = baseX + offsetX`. This keeps parallax smooth at full refresh
  while the expensive projection runs at 10Hz.
- Build a **`visible` subset** during reprojection (cull below-horizon / off-screen elements
  once) so the draw loop iterates only what's on screen, not the full catalog.
- Invalidate with a `projectDirty` flag on resize, viewport change, or any input that moves
  the projection (a new vantage/camera). Cull on the cached **pre-offset** position with a
  margin larger than the max per-frame offset, so nothing pops at the edges between reprojects.

The sky/camera drift being imperceptible at 10Hz is what makes this safe; verify your slow
input really is slow relative to a ~100ms refresh before relying on it.

## Technique 2 — blit a sprite, don't path-fill per element

`ctx.drawImage(sprite, x, y, d, d)` of a small pre-rendered bitmap beats
`beginPath()`+`arc()`+`fill()` per element. Two wins:

- **Fewer/cheaper draw ops** — one `drawImage` vs path construction (arc tessellation) + fill.
- **No per-element color-string parse.** `ctx.fillStyle = "rgb(...)"` re-parses the CSS color
  string on **every assignment**; doing it per element per frame is pure overhead. A sprite
  carries its color baked in, so `fillStyle` is never touched in the loop.

Implementation:

- **Quantize element color into N buckets** (~16-24 is imperceptible for sub-pixel elements)
  and pre-render one sprite per bucket once at setup. Store each element's bucket index.
- Carry per-element brightness/alpha on **`globalAlpha`** (a cheap property set, not a string
  parse) so the sprite bitmap itself never changes within a bucket.
- **Bake soft glow into the sprite** (a radial gradient) instead of a separate per-element
  bloom pass. A solid core out to ~0.8 of the sprite radius with a thin AA edge reads as a
  crisp dot; a soft falloff reads as a glow. Scaling the unified sprite by element size makes
  small elements' halos vanish sub-pixel and only big ones show glow — reproducing a
  size-gated bloom for free.
- Render the sprite at a **native ~64px** and downscale per element with `drawImage`'s size
  args; downscaling a 64px sprite to 1-12px stays crisp. Build sprites with
  `createRadialGradient` once — never in the hot path.

Tradeoff to disclose: color is quantized to buckets and the glow becomes a soft gradient
rather than a hard-edged disc. On tiny background elements this is imperceptible (and often
nicer), but it is a visual change, tunable via bucket count and gradient stops.

## Technique 3 — periodic value via a lookup table

Replace `Math.sin(...)` (or any periodic function) called **per element per frame** (twinkle,
oscillation, shimmer) with a **power-of-two lookup table** indexed by a bitwise mask:

```js
const N = 1024, MASK = N - 1, SCALE = N / (Math.PI * 2);
const SIN = new Float32Array(N);
for (let i = 0; i < N; i++) SIN[i] = Math.sin((i / N) * Math.PI * 2);
// per element: replaces Math.sin(phase)
const v = SIN[(phase * SCALE) & MASK];
```

- The `& MASK` does a `ToInt32` coercion and keeps the index in `[0, MASK]` **even after the
  phase exceeds 2^31** — a negative int32 `& MASK` is still a valid non-negative index — so
  long-session overflow is cosmetic (a phase discontinuity), never an out-of-bounds read.
- `phase` here is monotonically increasing (e.g. `now * 0.001 * speed + offset`), so it's
  always ≥ 0; the mask path is the fast one. Build the table once at setup.

## Where these came from

First applied to a real naked-sky star field (~9k HYG stars) rendered on a full-viewport
canvas behind a site — the one RAF loop running even at rest. Projection caching cut the
per-frame trig (the dominant idle cost) by ~6-10x; sprite blitting removed the path build and
per-star `fillStyle` parse; the LUT removed thousands of `Math.sin` calls per frame.

For physics-motion rendering choices (DOM vs canvas vs WebGL, frame-rate independence of the
sim itself) that is a different topic — see the matter-js DOM physics skill if installed.
