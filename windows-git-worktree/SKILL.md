---
name: windows-git-worktree
description: Gotchas working in a git worktree on Windows and sharing a branch across parallel sessions. Use when a fresh worktree shows the whole tree as "modified" with no real changes (core.autocrlf / CRLF-LF warnings), when node_modules is missing in a worktree, or when two agents/sessions edit the same worktree/branch and you must commit only your own files. Covers diagnosing phantom EOL "modifications" (git diff --ignore-cr-at-eol / --numstat) and fixing them permanently with a committed .gitattributes + git add --renormalize, junctioning node_modules into the worktree, staging scoped to your files with git mv to preserve history, and matching exact tab indentation for whitespace-sensitive edits.
---

# Windows git worktree + shared-branch gotchas

## Phantom "modified" whole tree (core.autocrlf)

**Symptom.** A fresh worktree on Windows shows nearly every tracked file as ` M` in
`git status`, with `warning: LF will be replaced by CRLF the next time Git touches it`.
Nothing was actually edited.

**Diagnose** it's EOL-only, not real changes:
- `git diff --ignore-cr-at-eol --stat <file>` → empty.
- `git diff --numstat <file>` → no changed-line rows.
- The files on disk and in the repo are byte-identical (often both LF already).

**Cause (mechanism).** `core.autocrlf=true` with no `.gitattributes`: git wants to
re-record every text file under its line-ending policy, so `git status` flags them even
though the bytes match the repo. Fresh Windows worktrees surface this for the whole tree
at once. It's cosmetic — the diffs are empty — and won't enter a commit unless you stage
those files.

**Fix permanently** (for the whole team) — commit a `.gitattributes`:

```
* text=auto eol=lf
*.png binary
*.jpg binary
*.woff2 binary
```

Then `git add .gitattributes && git add --renormalize .`. For an EOL-identical repo,
renormalize stages **no** file content, so the commit contains only `.gitattributes` and
the phantom ` M` entries clear. Caveat: `git add --renormalize .` also stages files that
have *genuine* uncommitted changes — if you want an isolated `.gitattributes` commit,
`git reset` first, then `git add .gitattributes` alone.

Do **not** just flip `core.autocrlf` — config lives in the common `.git` and affects the
main checkout and any other sessions.

## node_modules is missing in a worktree

`git worktree add` does not copy `node_modules`. Junction it to the main checkout instead
of reinstalling (PowerShell):

```powershell
New-Item -ItemType Junction -Path "<worktree>\node_modules" -Target "<main>\node_modules"
```

Do **not** use `mklink` through Git Bash — the Windows path args get mangled
(`Invalid switch - "C:\..."`). Use the PowerShell junction, or run `mklink /J` from a
real `cmd`.

## Two sessions sharing one worktree + branch

Parallel agents can end up editing the **same** worktree directory and branch. Coexist:

- Commit only **your** files. Stage explicit paths, never `git add -A`.
- `git status` may show another session's real changes (especially once the EOL fix above
  un-hides them). Leave them; don't clobber files you didn't create.
- Refactor with `git mv old new` so renames keep history; then edit the moved file and
  `git add` it (the rename + content edit record as a rename).
- Before committing, run `git diff --cached --name-status` and confirm only your files are
  staged. A shared file (`package.json`) may carry both sessions' edits — committing it
  pulls theirs in too; fine if it references already-committed code, but know it.

## Whitespace-exact edits (agent tip)

When an exact-match edit needs a tab-indented file's literal indentation, get the bytes
first — `sed -n 'X,Yp' file | cat -A` renders tabs as `^I`. Reconstruct the match string
with the right tab counts; otherwise the edit fails to match (or matches the wrong copy).
