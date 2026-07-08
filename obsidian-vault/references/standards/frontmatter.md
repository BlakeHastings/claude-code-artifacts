---
standards-component: frontmatter
component-version: "1.1.0"
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

### Research Paper fields

The `Research Paper` template adds typed bibliographic fields so Dataview can index papers by author, year, and reading status. They are optional everywhere else.

| Field | Type | Rules |
|-------|------|-------|
| `authors` | list | Person-note wikilinks, one per list item, in published order. Each is a **quoted** wikilink so YAML accepts the `[`: `- "[[Jane Smith]]"`. Link by full name (matches the [person-note filename](naming.md)); the link may stay **unresolved** until a person note exists. Empty list `[]` if unknown. |
| `year` | string | Publication / submission year as a **quoted** wikilink to the year note, e.g. `"[[2024]]"`. May stay unresolved; clusters every paper from a given year. |
| `arxiv` | string | Bare arXiv id, e.g. `2410.08328` (no `arXiv:` prefix, no URL). Blank for non-arXiv papers. |
| `url` | string | Canonical landing page (the abstract page for arXiv, the DOI/publisher page otherwise). |

## Rules

1. Frontmatter must be the **first content** in the file — no blank lines or content before the opening `---`.
2. `tags` **never contains wikilinks** (`[[...]]`) — topic links belong in the note body where the concept is referenced. This is a `tags`-specific rule, **not** a blanket ban: typed **link fields** (like `authors`) are meant to hold wikilinks, since Obsidian treats them as native link properties (clickable, counted in backlinks and the graph). Quote any wikilink in frontmatter (`- "[[Name]]"`) so YAML does not parse the `[` as a flow sequence.
3. `id` is **immutable**. If you rename a note, the id stays the same.
4. `template` and `template-version` reflect what the note was **created from**, not what it currently resembles. Lint workflows update these when migrating.
5. **No `Status:` field** — that was the old format. Status is now expressed as a tag in `tags`.
