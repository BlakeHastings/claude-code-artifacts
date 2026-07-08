---
name: matter-js-dom-physics
description: >-
  Driving DOM and text elements as Matter.js rigid bodies for scroll- and
  cursor-reactive web animations (drift-apart hero text, gravity-well / suck-into-element,
  drag-to-throw, funnel-through-a-point). Use when building physics-based UI motion
  with matter-js, mapping bodies to CSS transforms, debugging why bodies will not move
  or run too fast on a 120Hz/144Hz monitor or too slow on iOS Low Power Mode,
  tuning forces / mass / collisions / restitution, or adding cursor pull, grab-to-throw,
  or implode-into-a-target effects. Covers frame-rate independence across high-refresh
  and low-power displays, the deltaTime-squared force gotcha, prefers-reduced-motion
  (no-op vs reduced) and accessibility, the DOM-vs-Canvas-vs-WebGL choice,
  sticky-element coordinate alignment, a debug overlay, and building a force-directed
  node-link graph over DOM bodies (constraint springs, node repulsion, centering,
  directional flow, a traveling pulse, an SVG edge overlay, and responsive layout hooks).
  Also covers touch-scroll finger forces (tracking the finger via touch events because
  pointercancel kills pointermove during a scroll) and timed/sequenced "scene" effects
  scheduled on the physics frame clock instead of setTimeout.
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
---

# Matter.js as a DOM animation engine

Pattern for using Matter.js purely as a physics solver while the visuals are real
DOM elements (headings, glyphs, buttons). Matter owns positions/velocities; you copy
each body's position onto its element via a CSS transform every frame.

## Architecture that works

- Wrap it in a small framework-agnostic controller class (no Svelte/React inside).
  Expose `register(el)` (returns a cleanup fn, doubles as a Svelte action), plus
  lifecycle methods (`start`, `return`, `destroy`) and a config object of tunables.
- **Map body to element with a transform relative to a captured "home":**
  `el.style.transform = translate3d(body.position.x - homeX, body.position.y - homeY, 0) rotate(deg)`.
  The element stays in normal flow; only the transform moves it.
- **Capture home from `getBoundingClientRect()` only when the transform is identity**
  (i.e. at rest, before any drift). If the element is already transformed, derive home
  from the body instead. Getting this wrong makes everything jump on the second run.
- **RAF state machine that parks itself.** Use modes like `idle | active | returning`.
  When idle, set `rafId = 0` and return without rescheduling, so a static page burns
  zero CPU. Restart the loop on the next interaction.
- Build the Matter world lazily on first interaction; tear it down when it settles.
  Add off-screen static wall bodies sized to the viewport to keep bodies on screen.

## The single biggest gotcha: applyForce is scaled by deltaTime squared

`Body.applyForce` does NOT add velocity in intuitive units. Matter's integrator applies
`(force / mass) * deltaTime^2`, and with the usual `Engine.update(engine, 1000/60)` the
delta is ~16.67 (milliseconds), so `deltaTime^2 ~= 278`. A "small" force is multiplied
by ~278 and launches everything across the screen.

For predictable, tunable control, **nudge velocity directly** instead:

```js
// gentle pull toward a point, in honest px/step units
Body.setVelocity(body, {
  x: body.velocity.x + (dx / dist) * pullPerStep,
  y: body.velocity.y + (dy / dist) * pullPerStep
});
```

`pullPerStep` is now literally "px/step of velocity added per step" (an acceleration in
px/step^2), no hidden multiplier. Reserve `applyForce` for cases where you genuinely
want mass-scaled forces and have calibrated for the deltaTime^2 factor.

## Frame-rate independence: one Engine.update per rAF is a latent cross-platform bug

A hand-rolled loop that calls `Engine.update(engine, 1000/60)` exactly once per
`requestAnimationFrame` couples simulation speed to the display's refresh rate. rAF fires
~120x/s on a 120Hz screen and ~30x/s under iOS Low Power Mode, but each call still advances
a fixed 16.67ms of sim time, so the world runs ~2x **fast** on a high-refresh display and
~2x **slow** when throttled. (Reproduced in the wild: a body that travels ~232 px/s at 60Hz
hits ~575 px/s at 144Hz.) The fix is to make the loop consume *real* elapsed time, two ways:

- **Use Matter's own Runner (v0.20.0+, mid-2024).** It defaults to a fixed timestep that
  accumulates wall-clock time and runs N (or zero) `Engine.update`s per browser frame to
  match elapsed real time, with `maxFrameTime` / `maxUpdates` budgets and frame-delta
  smoothing for 59.94 vs 60Hz jitter. Simplest correct path if you do not need a custom loop.
- **Accumulator, if you own the loop.** Measure real `dt` from the rAF timestamp, accumulate
  it, and run as many fixed steps as fit: `while (acc >= STEP) { engine.update(STEP); acc -= STEP }`.
  **Cap the steps per frame** (e.g. bail after the equivalent of 1000/30 of catch-up) or a
  single slow frame triggers the *spiral of death*: more steps -> longer frame -> even more
  steps. Optionally interpolate the render by the leftover `acc` to avoid stutter.
  (gafferongames, "Fix Your Timestep".)

iOS **Low Power Mode throttles rAF to 30fps** (and all CSS animations) and is effectively
**undetectable from JS** (the Battery Status API is deprecated/removed on the open web), so
you cannot feature-detect it and downgrade. Delta-correct timing is the only defense: the
animation then finishes in correct wall-clock time, just at a lower, jerkier frame rate,
instead of running at half speed. Safari also throttles rAF inside cross-origin iframes,
uncapping only on click/tap.

**Springs and dampers have the identical bug.** A per-frame integrator like
`v = (v + force/mass) * damping; x += v` with no `dt` term settles ~2x fast on 120Hz. Scale
the integration by elapsed time (or run it on the same fixed-step accumulator) so the feel
is identical across refresh rates.

## Mass is size, for free

Matter computes `mass = density * area`. Give every body the same `density` and a wide
glyph automatically outweighs a thin one, so collisions transfer momentum by size with
no extra code. Expose `density` as a tunable; the absolute value barely matters, only
the ratios between bodies.

## prefers-reduced-motion will silently kill everything

If `window.matchMedia('(prefers-reduced-motion: reduce)').matches`, gate the whole
system to a no-op. The trap: it fails silently with no error, so "nothing moves and I
get no console output" is almost always this, not a logic bug. On Windows it is set by
Settings > Accessibility > Visual effects > Animation effects = Off (registry:
`HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate = 0`, and the client-area-animation
bit `0x02` clear in `UserPreferencesMask`). Many dev machines have it off. Provide a dev
override and/or an on-page notice so reduced-motion users (and you) understand why it is
static. Listen for `change` on the media query to react live (read once at construction and
the animation will not respond when the user toggles the OS setting until reload).

**No-op vs. reduced is a genuine choice, not a settled one.** MDN, web.dev, and Sara
Soueidan read the `reduce` keyword as "remove, reduce, *or replace*" and recommend swapping
a vestibular-triggering transform for a calm **opacity cross-fade** so motion-sensitive
users still get a finished-looking hero instead of a static jump; other practitioners argue
a full no-op (`immediate: true`) is fine for JS physics specifically. Both are defensible: a
full disable is the safe floor, an opacity fade-in is the premium upgrade. This matters
because ~35% of adults experience some vestibular dysfunction by age 40, so it is harm
prevention, not polish (WCAG 2.3.3 Animation from Interactions). For SSR/prerendered output,
**assume reduced (return `true`) on the server** and resolve the real value on the client
before the first frame, so nothing animates pre-hydration.

## When DOM-as-physics is the right choice (vs Canvas / WebGL)

This whole technique drives **retained-mode DOM**: the browser tracks and redraws your
nodes, so you keep text selection, find-in-page, screen-reader access, dark mode, and CSS
easing for free. That is the low-maintenance, accessible default and the correct choice at
low element counts (dozens of glyphs/cards). **Canvas** is immediate mode (your code does
all drawing, redraw cost scales with painted pixels); it wins only when element counts get
large or you need pixel effects, and it costs you every browser-native feature above (teams
end up shipping bespoke text-layout engines to claw them back). The experimental
HTML-in-Canvas API would restore them but is Chrome-only behind an origin trial, so it is
not a production cross-platform option. Choose Canvas/WebGL by element count and pixel work,
never by default.

## Coordinate alignment with position: sticky

Bodies live in viewport coordinates (homes captured from `getBoundingClientRect`). If the
animated container is `position: sticky` and stays pinned, each element's on-screen
position equals its body position in viewport space. That means **raw `clientX`/`clientY`
already equal world coordinates** for hit-testing and cursor forces, with no scroll-offset
math and no `Matter.Mouse` element binding. Prefer manual `window` pointer listeners over
`MouseConstraint`, which otherwise forces you to reconcile its element-relative,
scroll-dependent coordinate mapping.

## Cursor pull and grab-to-throw (manual, not MouseConstraint)

- **Radius-limited cursor pull:** skip bodies beyond a radius; inside it scale strength by
  `(1 - dist/radius)^2` (quadratic falloff) so only nearby elements react meaningfully.
- **Grab-to-throw with a click threshold:** on `pointerdown` record a candidate body
  (`Matter.Query.point(bodies, {x,y})`) but do NOT start dragging. Only promote to a drag
  once the pointer moves past ~4px. This preserves plain clicks, so a draggable button
  still fires its `onclick`. While dragging, set the body's velocity toward the cursor each
  step (so it shoves others); on release, set velocity from recent pointer movement to fling.

## Touch: a finger plowing through bodies during a scroll

To make a touch *scroll* shove bodies aside (the finger parts the elements as it drags past),
you cannot read the finger from pointer events: once a touch becomes a scroll (with
`touch-action: pan-y`), the browser fires **`pointercancel`** and stops sending `pointermove`,
so the pointer position goes stale exactly during the gesture you care about. **Track the finger
from `touch*` events instead** — `touchstart`/`touchmove`/`touchend` (passive) keep firing
through a scroll. Accumulate the finger's per-frame travel (`vx`/`vy` from successive
`touchmove` client positions) and zero it once a solver step consumes it, so a paused finger
stops exerting force. Then apply a radial shove away from the finger, scaled by its speed
(`strength * (1 - dist/radius)^2 * fingerSpeed`), to nearby bodies — a still finger does
nothing, a brisk swipe parts them. Keep this separate from the desktop cursor force (which uses
pointer events): pick one per frame by whether a finger is currently down.

## Timed sequences on the frame clock (not setTimeout)

Effects that play out *over time* ("the element arrives, then a beat later a second effect
fires, then a third") tempt one `setTimeout` per step. Don't: `setTimeout` runs on a different
clock from the physics — it keeps counting while the tab is backgrounded (where rAF is paused),
ignores `prefers-reduced-motion`, and scatters timing across the codebase. Instead give the
controller one seam: `schedule(delayMs, cb)` that records `fireAt = performance.now() + delayMs`
and, inside the existing rAF loop, fires (and removes) any cue whose `fireAt` has passed. It then
shares the physics clock for free — pauses when the loop parks, advances at wall-clock rate
across refresh rates, and is inert under reduced motion (have `schedule` no-op there). Return a
cancel function; keep the loop awake while any cue is pending; snapshot due cues before running
them so a callback can safely schedule or cancel more. A "scene" is then a few `schedule` calls
whose cancel handles are cleared as a unit if the trigger reverses — declarative sequencing in
one place instead of stray timers.

## Gravity-well / suck-into-a-point

To make bodies rush into a single point, collide on the way, and arc rather than beeline:

- Each step add a strong velocity nudge toward the point, then multiply velocity by a
  **damping factor < 1** (e.g. 0.93). A pure central force conserves angular momentum and
  bodies orbit forever; the damping bleeds energy so they spiral in.
- Give each body an initial **tangential** kick (perpendicular to the radius) so the pull
  curves their momentum into an arc.
- Make the target body a **sensor** (`body.isSensor = true`) so others pass through it
  instead of piling on its back.
- **Remove absorbed bodies** (`Composite.remove(world, body)`) once they reach the point,
  and flip a body to `isSensor` as it enters the final approach, so a pile-up at the sink
  clears and the bodies behind can get through. Otherwise bouncy collisions jam the
  entrance and they never fully arrive.

## Force-directed relationship graph over DOM bodies

To turn a scatter of DOM bodies into a node-link graph (letters into words, words into a
constellation), lay the graph on top of bodies that already exist rather than building a
parallel system. This "additive effect" borrows the bodies by reference, adds Matter
constraints for the edges, draws an SVG overlay for the lines and labels, and never writes
the bodies' transforms (their owning code already does that each frame). Constraints are a
second kind of world membership alongside bodies, so funnel them through the controller (an
`addConstraint` wrapper) and the one `Engine.update` resolves them for free. Because the
effect does not touch transforms, it needs no ownership handoff with whatever else drives
those elements. Trigger it from the composition root via a callback (a state-change callback
on whatever spawns it), not by having the moving systems know about each other.

A force-directed layout is four forces. Express each as a velocity nudge per fixed step (see
the deltaTime-squared note) or as constraints the solver runs:

- **Attraction** is Matter `Constraint`s (springs) between connected bodies. The solver does
  the pulling; you only add and remove them.
- **Repulsion** is pairwise, with a linear or inverse falloff. Measure the gap from each
  body's *surface* (its half-extents), not its center, so a big node (a card) pushes
  neighbours over a wide range while small nodes only nudge each other up close. Split the
  push inversely by mass so a heavy node barely drifts while the light ones do the fanning.
  O(n^2), but fine into the tens of nodes.
- **Centering / containment** is a spring toward a target point whose force grows with
  displacement (near-zero in the middle, strongest at the edges).
- **Directional flow** is a uniform push in one direction emanating from an origin (a hub)
  and fading with distance: "repulsion in a direction", for streaming the cloud off to one side.

Gotchas that cost real time:

- **Rest length comes from node size, not current distance.** Deriving an edge's rest length
  from the bodies' current (scattered) separation freezes them apart. Compute it from the
  nodes' widths (times a spread factor) so the spring actually gathers them.
- **An add-only velocity spring oscillates.** Adding `(target - pos) * k` to velocity every
  step with no damping is an undamped oscillator: it overshoots and rings. Multiply the
  velocity by a damping factor `< 1` after adding the pull so it settles smoothly. This is the
  fix for "the pinned node just bounces around trying to reach its spot". Verify by sampling
  the position over time: it should converge monotonically, not swing.
- **A center spring cannot reposition a wall-bound cloud.** The equilibrium centroid sits at
  the target only if the cloud is not pressed against the viewport walls; a cloud wide enough
  to touch both side walls has its bias cancelled by the wall normals. Gather it firmer (raise
  the spring) so it is compact enough to actually sit where you aim it.
- **Pin a node to a responsive anchor with a damped spring**, subtle stiffness plus damping,
  so it glides in from wherever it spawned and stops without a bounce.

The SVG edge overlay:

- A `position: fixed; inset: 0` SVG with no `viewBox` makes one SVG user unit equal one
  viewport pixel, which equals the bodies' coordinates, so you write each line's endpoints
  straight from `body.position` every frame.
- Stack it between the background and the text. A fixed SVG at `z-index: -1` sits behind
  normal-flow content (so behind glyphs whose `will-change: transform` gives them their own
  stacking contexts) but in front of a `z-index: -10` background.
- Fade the whole layer in with one group `opacity`, not per-edge.
- To make an edge meet a card's border instead of its center, clip that endpoint to the
  body's box: cast a ray from the body center toward the other endpoint and stop at the box
  edge (`t = 1 / max(|dx|/halfW, |dy|/halfH)`); if the other point is already inside the box,
  leave it.

A traveling pulse along the graph (a wave that visits nodes and jostles them):

- Build a path as an ordered list of bodies (e.g. hub, then spine anchors, then a cluster's
  nodes) so the pulse follows real edges. Walk it by arc length over the bodies' *live*
  positions each step, segment by segment, so the path bends as the bodies move.
- As the pulse point passes, push nearby nodes away from it. The push direction flips as it
  approaches then leaves a node, which reads as a wiggle that the springs settle behind it.
- Ride a visible dot on the pulse point; append it last in the SVG group so it draws over the
  edges. Cycle which route each pulse takes for variety.

Responsive layout: keep the per-viewport policy (which way to flow, where to pin, how short
the links) in the *consumer* as small hook functions, not baked into the generic effect.
Evaluate structural values (constraint rest length and stiffness) once when the graph builds,
and continuous forces (anchor target, flow direction) each frame from the live viewport size,
so the layout follows resizes and rotation.

## Debug overlay you can toggle from the console

Attach a `Matter.Render` to a `position: fixed; inset: 0; pointer-events: none;
opacity: 0.75` canvas at a high z-index. Toggle it from a `window.someDebug()` function so
you can inspect bodies, velocities, and collisions live. Bodies only exist while the world
is simulating, so the overlay is empty at rest. Note it only shows the physics, not
transform-driven (non-Matter) animation phases.

## Misc

- Guard all `window`/`document` access for SSR; construct the controller lazily or behind
  a `typeof window !== 'undefined'` check.
- `restitution` controls bounciness on collision, not whether momentum transfers. Even at
  restitution 0 bodies shove each other; raise it (0.6-0.8) so collisions visibly separate.
- With `frictionAir: 0` (space feel) there is no damping, so whatever initial velocity you
  seed is kept until a collision. A gentle launch stays gentle.
- Re-roll randomness (direction, speed, spin) with `Math.random()` per run rather than
  index modulo (`i % 5`, `i % 2`), which reads as a visible repeating pattern.
- **Stick to `transform` and `opacity`.** Only those two animate on the compositor alone
  (no layout, no paint), which is why the transform-per-frame mapping is cheap. Animating
  `top`/`left`/`width`/`height` instead forces layout *and* feeds Cumulative Layout Shift
  (CLS); transforms do not shift layout. Keep the animated elements present in the
  prerendered HTML so there is no inject-on-mount shift either.
- **Do not blanket-promote layers.** `will-change: transform` / `translateZ(0)` promotes an
  element to its own GPU layer, but each layer costs memory; promoting dozens ("layer
  explosion") backfires on memory-constrained mobile. Promote only what actually animates.
