# Operation: Lint Note

Scans one or more notes for standards violations, applies safe deterministic fixes via script, and surfaces judgment-needed items for human review.

## Scope

- **Single file:** `dotnet run scripts/lint-scan.cs -- "The Pile/Note.md"`
- **Directory:** `dotnet run scripts/lint-scan.cs -- "The Pile"`
- **Whole vault:** `dotnet run scripts/lint-scan.cs` (no argument = vault root)

Templates and Excalidraw are always skipped.

## Workflow

### Step 1 — Read current standards

Read `references/standards/README.md` then each component file. Establishes the current aggregate `standard-version` and the rules being linted against.

### Step 2 — Run the scanner

From the skill root:
```bash
dotnet run scripts/lint-scan.cs -- "<target>"
```

Output is JSON on stdout. Structure:
```
{
  "scan_root": "...",
  "standard_version": "1.0.0",
  "current_template_versions": { "Atomic Note": "2.0.0", ... },
  "summary": {
    "files_scanned": N,
    "files_with_violations": N,
    "total_violations": N,
    "auto_fixable": N,
    "needs_judgment": N
  },
  "files": [
    {
      "path": "The Pile/Note.md",
      "full_path": "C:\\...",
      "violation_count": N,
      "auto_fixable_count": N,
      "violations": [
        { "code": "...", "severity": "error|warn|info", "auto_fixable": true|false, "line": N, "detail": "..." }
      ]
    }
  ]
}
```

Report the summary to the user before proceeding.

### Step 3 — Apply auto-fixes

For each file in the report that has `auto_fixable_count > 0`, run:
```bash
dotnet run scripts/lint-autofix.cs -- "<full_path>"
```

The script handles these and only these:
- Removes body H1 matching the filename
- Converts `` ```ad-TYPE `` blocks to `> [!type]` callouts
- Bumps `standard-version` to current
- Bumps `template-version` to current (when template is known)
- Adds `standard-version` if missing
- Adds `template-version` if missing AND `template` is known

Report each fix to the user as it is applied.

### Step 4 — Handle judgment-needed violations

After autofix, work through the remaining violations per file. Work **one file at a time**, **one violation at a time**. For each:

1. Read the file
2. Apply the **Judgment Guide** below to determine the correct fix
3. Show the user the proposed edit (diff or before/after)
4. Apply via Edit tool only on user approval
5. Continue to the next violation

Do NOT batch-apply judgment edits without approval.

### Step 5 — Verify (optional)

Re-run `lint-scan.cs` on the target to confirm all violations are resolved.

---

## Violation Catalog

| Code | Severity | Auto-fixable | Description |
|------|----------|--------------|-------------|
| `no-frontmatter` | error | no | No YAML frontmatter |
| `frontmatter-missing-field` | error | partial | Required field absent (`id`, `template`, `template-version`, `standard-version`, `tags`). Auto-fixable only for `standard-version` (always) and `template-version` (when `template` is known). |
| `standard-version-drift` | info | yes | `standard-version` below current aggregate |
| `template-version-drift` | info | yes | `template-version` below current for that template |
| `body-h1-matches-filename` | warn | yes | First body H1 duplicates the filename |
| `admonition-syntax` | warn | yes | `` ```ad-TYPE `` block — use `> [!type]` instead |
| `body-has-status-line` | warn | no | Legacy `Status:` line in body |
| `body-has-tags-line` | warn | no | Legacy `Tags:` line in body |
| `body-has-inline-tags` | warn | no | Bare `#tag` line(s) in body (Second Brain legacy) |

---

## Judgment Guide

### `no-frontmatter` — entire frontmatter missing

1. **Determine note type** from content and inline tags. Map to a template:

   | Content signals | Template |
   |----------------|----------|
   | Single concept, idea | `Atomic Note` |
   | Person biography | `Person Note` |
   | Recipe | `Recipe Note` |
   | Facts about a source material | `Reference Note` |
   | Project description | `Project Template` |
   | Feature description | `Feature Note` |
   | Hub/index with links | `Index Note` |
   | Daily log / tasks | `Daily Note` |
   | No clear fit | Ask user; build a new template or fall back to `Atomic Note` |

2. **Determine `id`** — use the visible timestamp if the note has one on line 1 (e.g., `202405011347`). Otherwise use the file's last-modified time.

3. **Determine `tags`** — convert old inline tags from the bottom of the note:
   - Second Brain `#clean-architecture/chapter-1` → keep as secondary tag
   - `#idea` → primary tag `idea`
   - `#shortstory #scene` → primary `idea`, secondary `shortstory` (or flag for new template)
   - `#reference` → primary `reference/CATEGORY` — determine sub-tag from content

4. **Build frontmatter** and prepend. Then remove the now-duplicate bottom tags, old `Status:` / `Tags:` lines, and any H1 matching the filename.

5. **Propose the full edit to the user** before writing.

### `body-has-status-line` / `body-has-tags-line`

Extract the tag values, merge into frontmatter `tags:` list, then delete the body line. If frontmatter doesn't exist yet, treat as `no-frontmatter` first.

### `body-has-inline-tags`

Same pattern — move inline tags to frontmatter `tags:`, delete the lines.

### `frontmatter-missing-field` (non-auto-fixable fields)

- **Missing `id`** — use file modification time formatted as `YYYYMMDDHHmm`
- **Missing `template`** — classify note type (same table as `no-frontmatter`) and set accordingly
- **Missing `tags`** — determine primary tag from content; ask user if genuinely ambiguous

---

## Notes

- Never rename files during lint — naming drift is a separate operation.
- The scan reads current standards and template versions fresh on each run. No stale config.
- Never run `lint-autofix.cs` on a note without first confirming it has frontmatter — the script handles this gracefully but will emit `Fixes applied: 0` on frontmatter-less files.
