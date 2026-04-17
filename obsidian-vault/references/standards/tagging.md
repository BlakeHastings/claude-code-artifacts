---
standards-component: tagging
component-version: "1.0.0"
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
| `reference/{category}` | Reference / "truth note" (see below) |
| `person` | Person / bio note |
| `recipe` | Recipe note |

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

## Rules

1. **No `#` prefix** in frontmatter — Obsidian adds it automatically.
2. **No wikilinks** in `tags`. Wikilinks go in body prose where the concept is actually referenced.
3. **Every note must have at least one primary tag.**
4. Tags are **lowercase**, hyphen-separated if multi-word.
