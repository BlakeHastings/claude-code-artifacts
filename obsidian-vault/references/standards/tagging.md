---
standards-component: tagging
component-version: "1.5.0"
part-of-standards: "1.0.0"
---

# Tagging Standard

Tags live in the `tags` frontmatter field **only**. No body tags, no wikilink-style tags.

## Primary Tags (Note Type)

Every note has **one primary tag** identifying its type.

| Primary Tag | Note Type |
|-------------|-----------|
| `idea` | Atomic idea / concept note |
| `daily` | Daily note |
| `index` | MOC / hub / index note |
| `project/active` | Active project |
| `project/paused` | Paused project |
| `project/archived` | Archived project |
| `project/idea` | Project idea (not started) |
| `feature` | Feature note for a project |
| `project/feature` | Feature note specifically within a project |
| `repository` | Code repository note — pointer to a Git/GitHub repo |
| `reference/{category}` | Reference / "truth note" (see below) |
| `person` | Person / bio note |
| `recipe` | Recipe note |
| `how-to` | How-to / process note |
| `investigation` | OSINT investigation hub for a subject (see the `osint-investigation` skill) |
| `report` | A reader-facing report that distills an investigation (or other body of notes) into plain-language findings for a specific audience and decision |
| `status-update` | Atomic status / event-log entry (a call, email, observation) tied to a project |
| `organization` | A company, legal entity, or brand |
| `location` | A place or address; often value-titled by the literal address |
| `post` | A specific social-media post |
| `link` | A link / identity note: the evidence and verdict on a connection or identity question between entities. Distinct from the linking *standard* (which is the wikilink / Dataview mechanics) |

## Reference Sub-Tags

Reference notes are **"truth notes"** about external source material — facts only, no opinions. Any PDF, website, YouTube video, book, etc. referenced elsewhere in the vault should have a reference note.

Sub-tags classify by **content category, NOT medium**:

| Sub-Tag | What It Is |
|---------|------------|
| `reference/book` | A book in any form (PDF, physical, audiobook) |
| `reference/paper` | Academic / research papers (even if stored as PDF) |
| `reference/article` | Blog posts, news articles |
| `reference/website` | General websites, docs sites |
| `reference/video` | Video content (any platform) |
| `reference/podcast` | Podcasts / podcast episodes |
| `reference/course` | Online courses, cert prep |
| `reference/documentation` | Technical / API documentation |

**Filename still mentions the form** (see [naming.md](naming.md)). The tag reflects **content category**, the filename reflects **form**.

- A research paper stored as PDF → filename `Complexity Theory Research Paper PDF`, tag `reference/paper`
- A book stored as PDF → filename `Clean Architecture Book PDF`, tag `reference/book`

## Secondary Tags

Secondary tags are **topic tags**: `rome`, `physics`, `writing`, etc. A note may have as many as are meaningful. Hierarchy is allowed (`physics/quantum`).

## Status Tags

Some notes move through a **lifecycle**. Status is expressed as a **hierarchical tag under a status namespace, never a frontmatter `Status:` field** (that field was retired). Namespacing groups every state under one parent in the tag pane, so you can filter the whole lifecycle (`reading/`) or a single state (`reading/active`). Exactly **one** status from a namespace applies at a time; you swap the tag as the note advances. The primary tag (e.g. `reference/book`) never changes.

### Reading status (`reading/`)

For anything you intend to read (usually `reference/book`). One state at a time:

| Tag | State |
|-----|-------|
| `reading/backlog` | Captured, want to read, not yet queued (the someday pile) |
| `reading/queued` | Queued to read next |
| `reading/active` | Currently reading |
| `reading/done` | Finished |

The `Index/Reading List` MOC sections books by these states. `reading/done` is a **kept record, not a removal**, so completed reading stays tracked rather than falling off the list.

Precedent: this mirrors project lifecycle states (`project/active`, `project/paused`, `project/archived`, `project/idea`), except a reading status sits **alongside** the primary tag rather than being the primary tag. Status tags follow the same lowercase, hyphen-separated rule as every other tag.

## Rules

1. **No `#` prefix** in frontmatter — Obsidian adds it automatically.
2. **No wikilinks** in `tags`. Wikilinks go in body prose where the concept is actually referenced.
3. **Every note must have at least one primary tag.**
4. Tags are **lowercase**, hyphen-separated if multi-word.
