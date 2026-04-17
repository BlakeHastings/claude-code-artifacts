---
standards-component: frontmatter
component-version: "1.0.0"
part-of-standards: "1.0.0"
---

# Frontmatter Standard

Every note in the vault has YAML frontmatter at the very top of the file.

## Required Fields

```yaml
---
id: "202604131430"
template: "Atomic Note"
template-version: "2.0.0"
standard-version: "1.0.0"
tags:
  - idea
---
```

| Field | Type | Rules |
|-------|------|-------|
| `id` | string | `YYYYMMDDHHmm` timestamp. Set once at creation, never changes. Quoted to preserve leading zeros. |
| `template` | string | Human-readable name of the template the note was created from. Must match a filename in `Templates/` (without `.md`). |
| `template-version` | string | Semver version of that template at the time the note was created. |
| `standard-version` | string | Semver aggregate standards version the note was authored against. |
| `tags` | list | YAML list of tag strings. No `#` prefix. Supports hierarchy (e.g., `reference/book`). |

## Optional Fields

| Field | Type | When to Use |
|-------|------|-------------|
| `aliases` | list | Alternative names for the note (for wikilink aliasing). |

## Rules

1. Frontmatter must be the **first content** in the file — no blank lines or content before the opening `---`.
2. `tags` **never contains wikilinks** (`[[...]]`). Wikilinks belong in the note body where concepts are actually referenced.
3. `id` is **immutable**. If you rename a note, the id stays the same.
4. `template` and `template-version` reflect what the note was **created from**, not what it currently resembles. Lint workflows update these when migrating.
5. **No `Status:` field** — that was the old format. Status is now expressed as a tag in `tags`.
