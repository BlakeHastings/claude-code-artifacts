---
name: browser-verify
description: See and drive a locally-running web app in a real browser to verify a change — navigate, screenshot, read console errors, and poke page state via eval. Use when verifying a visual or behavioral change to any web project (UI tweak, new component, animation, form, bug fix), comparing the rendered page against intent, or reproducing a runtime error. Also covers responsive/viewport testing with resize and verifying animations by sampling element state over time (not from a single screenshot), and the gotcha that an eval reading state right after a click returns stale values because the framework re-renders asynchronously. Project-agnostic; uses the global @playwright/cli (playwright-cli).
allowed-tools: Bash(playwright-cli:*), Bash(npx:*), Bash(npm:*), Bash(npm run dev:*), Bash(pnpm:*), Bash(yarn:*)
---

# browser-verify

Give yourself eyes on a running web app so you can verify your own work instead of
guessing. Backed by `playwright-cli` (Microsoft's `@playwright/cli`): one-shot commands
that share a **persistent** browser session between invocations, so each call is cheap.

Core idea: **start the dev server → open the page → screenshot / read console / eval →
compare against intent → fix → repeat.** Never claim a UI change works without looking.

## 0. Bootstrap (only if the command is missing)

```bash
playwright-cli --version          # if this fails, install globally:
npm install -g @playwright/cli@latest
# the browser is bundled; if a run complains a browser is missing:
playwright-cli install-browser
```

The CLI ships its own complete command reference as a skill. For the full verb list
beyond the cheat-sheet below, run `playwright-cli --help` (it prints the path to the
bundled `SKILL.md`) or `playwright-cli --help <command>`.

## 1. Find and start the dev server (project-agnostic)

Do not assume a framework or port. Detect it:

1. Read `package.json` `scripts` for the dev entry: usually `dev`, else `start` / `serve`.
2. Pick the package manager from the lockfile: `pnpm-lock.yaml`→pnpm, `yarn.lock`→yarn,
   `bun.lockb`→bun, `package-lock.json`→npm.
3. **Reuse before you start — this is the #1 leak source.** Check whether a dev server is
   already listening before spawning one. If the framework's default port (Vite 5173,
   Next 3000, etc.) already answers, reuse that URL and skip starting anything.

   ```bash
   # Bash: is something already serving on the likely port?
   curl -sf -o /dev/null http://localhost:5173/ && echo "reuse 5173"
   ```
   ```powershell
   # PowerShell: list dev servers already up for THIS project (and their ports)
   Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
     Where-Object { $_.CommandLine -match 'vite|next|run dev' -and $_.CommandLine -match 'YOUR-REPO-NAME' } |
     Select-Object ProcessId, CommandLine
   ```

   Why this matters: when you start `npm run dev` while a previous one still holds the
   port, Vite/Next **silently auto-increment** to the next free port (5174, 5175, …)
   instead of failing — so "reuse" never happens and a new server leaks every loop. One
   real session left **50 orphaned dev servers (~6 GB)** this way.

4. Only if nothing is serving: start it **in the background** (Bash tool
   `run_in_background: true`, or PowerShell `Start-Process`), **read the actual URL from
   its stdout** (do not hardcode the port), and **record the PID you started** so you can
   kill its whole process tree in teardown — see §5. Keep **one** long-lived server for the
   whole verify session; never start a fresh one per loop.

Example (npm + Vite): run `npm run dev` in the background, then scrape a line like
`Local:   http://localhost:5176/` from its output and use that exact URL.

## 2. The verify loop (command cheat-sheet)

```bash
playwright-cli open http://localhost:5176/     # opens browser + navigates (persistent session)
playwright-cli goto http://localhost:5176/about # navigate an already-open session

# LOOK — two complementary ways to "see":
playwright-cli snapshot                          # structured accessibility tree (DOM, refs, text)
playwright-cli screenshot --filename=/tmp/v.png  # pixels — REQUIRED for canvas/animation/visual diff
#   then Read the PNG to compare it against the intended design.

# READ runtime health:
playwright-cli console                           # all console messages (Errors/Warnings counted)
playwright-cli console error                     # filter by min level
playwright-cli requests                          # network requests; `request <n>` for details

# INTERACT (use refs from `snapshot`, or css/role/testid selectors):
playwright-cli click e15
playwright-cli fill e5 "user@example.com" --submit
playwright-cli press Enter
playwright-cli mousewheel 0 800                  # scroll down (dx dy)

# DRIVE PAGE STATE / call the project's own debug hooks:
playwright-cli --raw eval "document.title"
playwright-cli eval "window.scrollTo(0, document.body.scrollHeight)"
playwright-cli eval "el => el.getAttribute('data-state')" e5   # eval against an element ref

playwright-cli close                             # close when done
playwright-cli list                              # show open sessions
```

Tips:
- `--raw` strips the status/snapshot wrapper and prints only the result — use it to pipe
  `eval` output or keep responses small.
- `snapshot` is cheapest for "what's on the page / what can I click". `screenshot` is the
  right tool when the change is **visual** (layout, color, canvas, animation) — the
  accessibility tree is **blind to `<canvas>` content**.
- Use a named session (`playwright-cli -s=<name> ...`) to keep parallel work isolated.

## 3. Pattern: reuse the project's own debug hooks

Many apps expose runtime hooks on `window`. Prefer driving those over reverse-engineering
state from pixels. Discover them: `playwright-cli --raw eval "Object.keys(window).filter(k => /debug|__/i.test(k))"`.

**Worked example — voidprojects-site drift animation** (illustrates the pattern):
the homepage exposes `window.driftDebug()` and the glyph bodies only exist while scrolling.

```bash
playwright-cli open http://localhost:5173/
playwright-cli eval "driftDebug(true)"                 # overlay the Matter.js wireframe
playwright-cli mousewheel 0 1200                       # scroll to populate drift bodies
playwright-cli screenshot --filename=/tmp/drift.png    # then Read it: bodies + wireframe present?
playwright-cli console                                 # any Matter.js runtime errors?
playwright-cli close
```

### Assert on source-of-truth state, not incidental rendered output

When verifying *behavior or timing* (a state machine advanced, an effect fired after a delay,
a flag flipped), assert on the app's real state, not a side effect of how it happens to render.
Inferring state from rendered output is fragile in both directions: it breaks when the rendering
changes, and it passes on a coincidental match.

Fragile proxies seen in the wild, and why they fail:

- **Counting nodes** — `document.querySelectorAll('svg text').length > 0` to mean "the graph
  formed." Breaks if the graph's rendering changes; false-positives if anything else ever draws
  SVG text.
- **Finding an element by class or text** — `[...document.querySelectorAll('div.inline-block')].find(d => /Warp to Github/i.test(d.textContent))`.
  Breaks on any copy or class tweak.
- **Parsing a computed transform** to recover logical state.

The robust move: have the app expose a **read-only state snapshot** on `window` (the same
console-hook pattern as a debug overlay), returning the actual flags — e.g.
`window.driftState() // { cards:[{title,state}], graph:{ formed }, dock:{ active } }` — and poll
*that*. It is a genuine dev affordance, not test-only cruft, and it is stable to assert against.
Add a field (and a tiny read accessor on the relevant object) when you need to observe something
new. Discover existing hooks with the `Object.keys(window).filter(...)` probe above.

`getBoundingClientRect()` on a **known ref** (an element the app already binds, not one you found
by guessing a selector) is the exception — it *is* source-of-truth for where something rendered,
so position/layout assertions off a real ref are fine.

## Verifying responsive layouts and animations

Two things a single snapshot misses: layout that changes with viewport size, and motion.

- **Drive breakpoints with `resize`.** `playwright-cli resize <w> <h>` sets the window size;
  follow it with `goto <url>` (a reload) so width-gated logic re-runs from scratch. Check
  desktop and mobile widths (e.g. 1280x800 and 390x844) for any responsive layout.
- **Verify motion by sampling state over time, not by screenshotting once.** A still frame
  cannot tell a settled layout from one mid-bounce. Run an `eval` that polls an element every
  N ms and returns an array, then read the trend: e.g. an element's
  `getBoundingClientRect().left` over a few seconds should converge monotonically if it is
  meant to settle, or swing if it is oscillating. This is how you confirm a spring is damped,
  a value reaches a target, or a pulse traverses a path.

```js
// returns e.g. [283,183,153,146,143,142,141] -> glides in, no bounce
() => new Promise((resolve) => {
  const xs = []; let n = 0;
  const id = setInterval(() => {
    xs.push(Math.round(document.querySelector('.thing').getBoundingClientRect().left));
    if (++n >= 12) { clearInterval(id); resolve(xs); }
  }, 500);
})
```

- **Catch a transient frame.** To screenshot something visible only briefly (a pulse, a
  flash), poll in `eval` until the condition holds, resolve, then screenshot immediately:
  `() => new Promise(r => { const id = setInterval(() => { if (document.querySelector('.dot')?.style.opacity === '1') { clearInterval(id); r('shown'); } }, 40); })`.
- **Do not verify a sub-second transient with a screenshot in a *separate* CLI call.** Each
  `playwright-cli` call has startup overhead, so the gap between an `eval` that sets up the state
  and a following `screenshot` can be hundreds of ms — long enough to outrun a state that only
  holds for ~1s (e.g. "before the graph forms at 900ms"), and the shot lands after it changed. For
  timing like this, *assert the state inside the eval* (poll and return the timestamps/flags); use
  the screenshot only to confirm the final look once the state is settled, not to catch the moment.
- **Windows path note.** A screenshot written to `/tmp/x.png` from Git Bash lives at
  `C:\Users\<user>\AppData\Local\Temp\x.png`; resolve it with `cygpath -w /tmp/x.png` before
  handing the path to the Read tool.

## 4. Notes / gotchas

- **Windows `&` in URLs**: `cmd`/PowerShell treat `&` as a separator. Escape query strings:
  `cmd` → `"...?a=1^&b=2"`; PowerShell → `playwright-cli --% goto "...?a=1&b=2"`.
- **Stray `.playwright-cli/` dir**: `open`/`snapshot` write timestamped snapshot files into a
  `.playwright-cli/` folder in the current directory. Run from a scratch dir, pass
  `--filename` to a temp path, or delete the folder afterward so it never lands in a commit.
- **Dynamic content / animation**: give it a beat to settle (re-`snapshot`, or
  `eval "new Promise(r => setTimeout(r, 500))"`) before screenshotting.
- **`eval` reads stale DOM right after a mutation**: a component framework (Svelte 5,
  React, Vue) re-renders **asynchronously** — its effects/derived flush in a microtask,
  not synchronously after your click. So an `eval` that clicks/sets a value and then
  reads `aria-pressed`/text/state **in the same call** returns the *pre-render* value,
  making a real change look like it did nothing. Split it: one `eval` (or `click`) to
  mutate, a `sleep`/short delay, then a **second** `eval` to read. The input `.value`
  you set yourself updates synchronously, but anything the framework recomputes does not.
- **Match visible text by its DOM value, not its rendered case**: CSS
  `text-transform: capitalize/uppercase` changes appearance only; `textContent` keeps the
  source case. Selecting a chip via `[...buttons].find(b => b.textContent.trim() === 'Rounded')`
  fails when the source is `rounded` — match the lowercase (or use `.toLowerCase()`).
- **Always close** (`playwright-cli close` / `close-all`) when finished; `kill-all` clears
  stale/zombie browser processes. But `close`/`kill-all` only reach sessions the daemon is
  still tracking — browsers can leak *past* that tracking (you'll see `list` report "no
  browsers" while real `chrome` processes are still alive). For those, sweep at the OS level
  — see §5.
- Headless by default; add `--headed` on `open` if you want to watch.

## 5. Teardown — close the leak (run at the END of every verify session)

This skill leaks two kinds of long-lived process if you don't tear down: the **persistent
browser** (`playwright-cli open` keeps the session alive *by design* between calls) and any
**dev server you started**. Across many verify loops these accumulate into GBs. Always run
teardown when you're done verifying — don't leave them for "next time".

```bash
playwright-cli close-all     # close every tracked browser session
playwright-cli kill-all      # kill the daemon + any tracked stragglers
```

Then sweep OS-level orphans the daemon never tracked. **Scope every sweep to this project**
(match the repo path / playwright cache) so you never touch the user's real browser or
unrelated servers:

```powershell
# PowerShell (Windows) — kill orphaned dev servers for THIS repo, then orphaned playwright chromium
$repo = 'YOUR-REPO-NAME'   # e.g. voidprojects-site
Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
  Where-Object { ($_.CommandLine -match 'vite.*dev' -or $_.CommandLine -match 'next.*dev' -or $_.CommandLine -match 'run dev') -and $_.CommandLine -match $repo } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
# orphaned playwright browsers live under the @playwright/playwright cache, not your normal profile:
Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
  Where-Object { $_.CommandLine -match 'playwright|ms-playwright|--remote-debugging-pipe' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

```bash
# macOS / Linux — same idea, scoped to the repo / playwright cache
pkill -f "vite.*dev.*$REPO" ; pkill -f "next.*dev.*$REPO"
pkill -f "ms-playwright.*chrome|playwright.*--remote-debugging-pipe"
```

Killing a dev server is non-destructive (it recompiles on next `npm run dev`) — but a
project-scoped match keeps it from being destructive to *other* work. **Periodic health
check**: if you ever notice the machine slow or many node/chrome processes, run the count
below; a healthy machine has at most one of each per active project.

```powershell
(Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.CommandLine -match 'run dev|vite|next' }).Count
```
