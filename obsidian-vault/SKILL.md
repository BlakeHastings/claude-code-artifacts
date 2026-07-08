---
name: obsidian-vault
description: >-
  Create, lint, and search notes in Blake's Obsidian vault (second brain,
  knowledge base). Use when the user wants to create an atomic note, daily
  note, index note, project note, feature note, reference note, person note,
  recipe note, repository note, or any other Obsidian note; lint existing notes against vault
  standards; search the vault; explore a note's related notes (backlinks/incoming links,
  outgoing links, neighbors); rename, move, or delete notes (via the official Obsidian CLI,
  so backlinks stay intact); or propagate template/standards version updates.
  Operates on the vault at C:\Users\Blake\Documents\Obsidian\main.
allowed-tools: Read, Write, Edit, Glob, Grep, Bash
---
# Obsidian Vault

Skill for managing Blake's Obsidian vault at `C:\Users\Blake\Documents\Obsidian\main`.

- **Skill version:** 1.13.0 — see [CHANGELOG.md](CHANGELOG.md)
- **Vault standards version:** 1.10.0 — see [references/standards/README.md](references/standards/README.md)

## Argument Parsing

Parse `$ARGUMENTS`. First token is the operation:

| Operation | Aliases | Routes to |
|-----------|---------|-----------|
| `create` | `new`, `add` | `references/operations/create-note.md` |
| `lint` | `check`, `normalize` | `references/operations/lint-note.md` |
| `related` | `links`, `backlinks`, `neighbors`, `connections` | `references/operations/related-notes.md` |
| `search` | `find`, `query` | `references/operations/search-vault.md` (STUB) |

If no operation is given, ask the user what they want to do.

## Vault Location

Vault layout and paths are in `references/vault-layout.md`. Read that first before touching the vault.

## Renames, Moves, Deletes, and Link Analysis

**Never rename or move a note by raw filesystem operations** (writing a new file and deleting the old one). That orphans every backlink. Use the official Obsidian CLI, which rewrites backlinks vault-wide. The full policy, prerequisites, exact commands, and the manual fallback when the CLI is unavailable are in [`references/obsidian-cli.md`](references/obsidian-cli.md). Read it before any rename, move, delete, or backlink check.

To **explore a note's relationships** (backlinks, outgoing links, second-degree neighbors, shared-tag relatives) rather than mutate them, use the `related` operation — see [`references/operations/related-notes.md`](references/operations/related-notes.md). It reads Obsidian's resolved link graph via the CLI; it never modifies the vault.

## Operation Dispatch

1. **Identify the operation** from the user's request.
2. **Read the matching operation file** from `references/operations/`.
3. **Follow that file's instructions** — it tells you which standards and templates to consult next.

Do not inline standards or templates into this file. They live elsewhere:
- **Standards** → `references/standards/` (within the skill)
- **Templates** → `C:\Users\Blake\Documents\Obsidian\main\Templates\` (within the vault — never duplicated here)

## Scripts

| Script | Purpose |
|--------|---------|
| `scripts/list-templates.cs` | Enumerate vault templates with current versions |
| `scripts/lint-scan.cs` | Scan notes for standards violations, emit JSON report |
| `scripts/lint-autofix.cs` | Apply safe, unambiguous fixes to a single note |

Always use `list-templates.cs` to enumerate templates rather than hardcoding — it reflects live vault state.

## Skill Versioning

Follows semver. See [CHANGELOG.md](CHANGELOG.md) for history and [references/standards/versioning.md](references/standards/versioning.md) for the full rules governing skill, standards, and template versions.

## Deferred Work

- **search-vault** operation — stub only, not yet implemented
- **standard-version auto-lookup** at note creation — requires Templater plugin (not installed); templates hardcode the standards version and lint detects drift
