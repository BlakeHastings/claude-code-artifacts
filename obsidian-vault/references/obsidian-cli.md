# Obsidian CLI: use it for renames and link operations

Obsidian ships an official command-line interface (Obsidian 1.12.7+). It drives the running Obsidian app, so it uses Obsidian's real link-resolution engine. That is the whole reason to prefer it: **a rename through the CLI rewrites every backlink across the vault**, whereas a raw filesystem rename (write a new file, delete the old one) silently orphans every inbound link. Those links keep resolving only if you remember to add the old name as an alias, which is fragile and easy to miss.

## Prerequisites (one-time, done in the app by Blake)

The CLI cannot be enabled from the terminal. Blake must do this once in the desktop app:

1. **Update the installer to 1.12.7+** (this is the gotcha). Obsidian's in-app auto-update only updates the app layer, **not the installer** (`Obsidian.exe`). The CLI redirector ships with the installer, so an old installer means no CLI even though the app reports a newer version. Check About > Installer version, or from a shell: `(Get-Item "C:\Users\Blake\AppData\Local\Programs\obsidian\Obsidian.exe").VersionInfo.ProductVersion`. If it is below 1.12.7, download and run the latest installer from obsidian.md/download. (Seen at 1.8.4 on this machine, which is why no `Obsidian.com` redirector existed.)
2. Settings, General, enable **Command line interface**, then complete the registration prompt. On Windows this creates the `Obsidian.com` terminal redirector that `obsidian` resolves to (the `.com` is what makes terminal stdin/stdout work; the `.exe` is the GUI). Until this exists, typing `obsidian` just launches the GUI.
3. Settings, Files and Links, turn **Automatically update internal links** ON. This is the setting that makes `rename` and `move` rewrite backlinks. Without it the CLI rename still moves the file but does not fix links.
4. The Obsidian app must be running. If it is not, the first CLI command launches it (see "Ensuring the app is running" below).

Before relying on the CLI, confirm it is live: `obsidian version` and `obsidian vault info=name` (should print the active vault, `main`). Note `--version` is **not** valid (the command is bare `version`); `--help` does work. On Windows, also confirm `where.exe obsidian.com` resolves; if it points only at `Obsidian.exe`, the redirector is missing and the CLI is not actually enabled. Fall back to the manual protocol below.

> Verified live on this machine on 2026-06-29: installer 1.12.7, redirector present, active vault `main`.

## The command list is self-documenting

`obsidian help` prints every command this installed CLI supports, with each command's own options, and it is always current for the running version. Treat it as the source of truth. The commands named in this file are the ones that matter for vault work, not the whole surface; run `obsidian help` (or `obsidian help` piped to a grep) before assuming a command does or does not exist. Versions differ: for example `daily:read`/`daily:append` seen in other CLI docs do **not** exist here — `daily` is only a flag on other commands (e.g. `obsidian tasks daily`).

## Syntax

Options are `key=value`, **not** `--flags`. General form: `obsidian <command> key="value" flagname`.

A few flags work across most commands:
- `--copy` copies the command's output to the clipboard instead of (or alongside) printing it. Handy for lifting a note's body or a query result straight into another note.
- `total` on any list command returns just the count.
- `counts` adds per-item counts where it applies (`backlinks`, `tags`).
- `format=json|tsv|csv` for structured output on the commands that support it.

- `file=<name>` resolves by note name like a wikilink; `path=<folder/note.md>` is an exact path. Most commands default to the active file if both are omitted.
- Quote values with spaces: `name="My Note"`. Bare words like `counts`, `total`, `permanent` are boolean flags.
- `vault=<name>` targets a specific vault. The CLI acts on the active vault by default.
- On Windows from this Bash/PowerShell session the `obsidian` command may be missing from the inherited (stale) PATH; call the redirector by full path if so: `& "C:\Users\Blake\AppData\Local\Programs\obsidian\Obsidian.com" <command> ...`.

## Ensuring the app is running (the "headless" question)

The management CLI (`rename`, `move`, `backlinks`, etc.) has **no headless mode**; it needs the GUI app running. There is no `--headless` flag and no hidden-window mode on Windows.

- **Do not confuse this with `obsidian-headless`** (the official Feb 2026 package). That is a Sync/Publish-only client that needs an active Obsidian Sync subscription. It cannot run `rename`/`backlinks` or any vault-management command, so it is not a substitute here.
- **Windows pattern when the app is not running:** launch it minimized in the background, wait for it to come up, then run CLI commands:
  ```powershell
  if (-not (Get-Process Obsidian -ErrorAction SilentlyContinue)) {
    Start-Process -WindowStyle Minimized "C:\Users\Blake\AppData\Local\Programs\obsidian\Obsidian.exe"
    Start-Sleep -Seconds 6
  }
  ```
  This is the closest thing to headless on Windows: the window exists but stays minimized and out of the way. The CLI would auto-launch the app anyway; pre-launching minimized just controls how it appears. (The true-headless route, an Xvfb virtual display, is Linux-only and does not apply on this Windows machine.)

## What MUST go through the CLI

- **Renaming a note**: `obsidian rename file="Old Name" name="New Name"` (the `name=` new file name is required). Never rename by creating a new file and deleting the old one.
- **Moving a note**: `obsidian move file="Note" to="Folder/Note.md"` (`to=` is required; it both moves and renames).
- **Deleting a note**: `obsidian delete file="Note"` sends it to trash and is recoverable. Add the bare `permanent` flag only when that is genuinely intended. Prefer this over `rm`.

Renames and moves rewrite backlinks vault-wide only when "Automatically update internal links" is on (see prerequisites).

## Also prefer the CLI over grep for link analysis

- `obsidian backlinks file="Note" counts`: everything linking to a note, from Obsidian's resolved graph. Catches alias-resolved links and embeds that a text grep can miss or miscount.
- `obsidian links file="Note"`: outgoing links from a note.
- `obsidian unresolved verbose`: every dangling link in the vault. **This is the proper post-rename check**: after a rename, run it (optionally filtering for the old name) to confirm nothing was orphaned. Stronger than a grep sweep because it uses Obsidian's resolver.
- `obsidian orphans` / `obsidian deadends`: notes with no incoming / no outgoing links.
- `obsidian search query="text"` (or `search:context` for matching lines): vault text search.
- `obsidian files folder="The Pile"` / `obsidian search`: **the preferred way to locate notes.** The Glob tool was observed to miss notes in the large flat `The Pile/` folder; the CLI (and Grep) read the real vault state. See [vault-layout.md](vault-layout.md#locating-notes).
- `obsidian tags counts sort=count`: every tag in the vault with usage counts (add `file="Note"` to scope to one note). Reads Obsidian's resolved tag index, so it counts both frontmatter and inline `#tags`.
- `obsidian tasks todo` / `obsidian tasks done` (add `file=`, `path=`, or `active` to scope; `verbose` groups by file with line numbers): list checkbox tasks across the vault from Obsidian's parser rather than a grep for `- [ ]`.

## What stays in this skill's flow (NOT the CLI)

- **Creating notes**: use the `create` operation here. It applies the template, frontmatter, tags, and standards. The CLI `create` is primitive and would skip all of that.
- **Linting**: use the lint scripts.

## Fallback when the CLI is unavailable

If the app is not running or the CLI is not enabled, do the rename by hand and then make it safe yourself, reproducing what the CLI would have done:

1. Create the new file, move the content, delete the old file.
2. Sweep the whole vault for the old name as a link: `[[Old Name]]`, `[[Old Name|`, and as an embed `![[Old Name`.
3. Rewrite each occurrence to the new name, preserving display text.
4. Re-grep to confirm zero remain, ignoring intentional `aliases:` entries and anything under `.obsidian/`.

This is error-prone (it is exactly how backlinks get orphaned), so it is the fallback, never the default.
