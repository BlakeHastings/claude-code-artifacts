---
name: open-link-after-animation
description: >-
  Open a URL in a new tab after a delay or animation, without tripping popup blockers
  or stealing focus from the current page. Use when navigation must wait for a transition
  or animation to finish, when window.open after a setTimeout / requestAnimationFrame gets
  blocked, when a freshly opened tab steals focus mid-animation, or when you want a link to
  open in a background tab from JavaScript. Covers transient user activation and the
  Ctrl/Cmd-click background-tab technique.
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
---

# Open a link after an animation (no popup block, no focus steal)

When a click should play an animation first and only then navigate, two browser behaviors
bite you. This is how to handle both.

## 1. A short deferred window.open is NOT blocked

Transient user activation lasts about 5 seconds after a user gesture. `window.open` (and a
synthetic anchor click) is permitted any time activation is still live, even from a
`setTimeout` or `requestAnimationFrame` callback. So opening a link ~300-900ms after a
click, once your animation finishes, generally is NOT popup-blocked.

Do not "pre-open a blank tab to preserve the gesture" (`const t = window.open('', '_blank')`
then set `t.location` later). That opens an empty tab immediately and **steals focus right
when the animation starts**, so the user never sees it. Just open at the end.

## 2. Open in a BACKGROUND tab so focus stays on your page

A normal `window.open(url, '_blank')` usually foregrounds the new tab, yanking the user off
your page. To open in the background (keeping focus on the current page so the animation can
finish and they can see it settle), synthesize a Ctrl/Cmd-click on a hidden anchor:

```js
function openInBackgroundTab(url) {
  const a = document.createElement('a');
  a.href = url;
  a.target = '_blank';
  a.rel = 'noopener noreferrer'; // sever window.opener; avoid reverse tabnabbing
  a.style.display = 'none';
  document.body.appendChild(a);
  a.dispatchEvent(
    new MouseEvent('click', {
      bubbles: true,
      cancelable: true,
      view: window,
      ctrlKey: true, // Windows/Linux: open in a background tab
      metaKey: true  // macOS: open in a background tab
    })
  );
  a.remove();
}
```

Set both `ctrlKey` and `metaKey` to cover all platforms (the extra one is harmless). Call
this from your animation's completion callback.

## Caveats

- Foreground vs background is ultimately the browser's decision. Chromium and Firefox honor
  the modifier-click hint reliably; if a browser foregrounds it anyway, the open still
  happens after the animation, so the user is never interrupted mid-transition.
- Keep the total delay under the ~5s activation window. A 600-900ms animation is safe.
- `rel="noopener noreferrer"` is important when opening from JS: without it the destination
  can reach back through `window.opener`.
- If you also reset the page (e.g. `window.scrollTo(0, 0)`) so it is clean on return, do it
  in the same completion callback.
