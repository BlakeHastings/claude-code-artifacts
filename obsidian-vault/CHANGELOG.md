# Changelog

All notable changes to the `obsidian-vault` skill.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.13.0] - 2026-06-30

### Added
- **External-system identifier record naming convention** (`references/standards/naming.md`, component 1.2.0 -> 1.3.0). Records pulled from an external registry (CMS NPI records, and any similar opaque, system-assigned ID) are value-titled like addresses and phones but **prefixed with the issuing system's short name**: `NPI 1174242564`, not the bare `1174242564`, with the descriptive name (e.g. `Rize OC CMS NPI Record`) as an alias. The prefix namespaces the opaque ID so it cannot collide with a same-valued identifier from another system (a DEA number, a CMS CCN, a state license number, an internal case ID); phones and addresses keep no prefix because their format is already self-identifying. New table row plus an "External-System Identifier Records — Detail" section. First applied note: `NPI 1174242564` (was `Rize OC CMS NPI Record`).

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.10.0; naming row to 1.3.0
- `SKILL.md` — skill version bumped to 1.13.0; vault-standards reference to 1.10.0

## [1.12.2] - 2026-06-30

### Changed
- `references/operations/create-note.md` — added a cross-reference to the osint-investigation skill's "one proof note per entity × source, linked from the entity" convention: when capturing verification/proof during an investigation, each record is its own atomic proof note (source URL + screenshot/PDF + facts) titled for and linking to the entity it concerns, with that entity's note carrying a **Verification / Records** section, rather than bundling several subjects into one note. `SKILL.md` skill version bumped to 1.12.2.

## [1.12.1] - 2026-06-30

### Changed
- `references/obsidian-cli.md` — CLI documentation touch-ups borrowed from kepano/obsidian-skills, all verified against the installed CLI: a "self-documenting" note that `obsidian help` is the always-current source of truth (with the caveat that `daily:read`/`daily:append` from other CLI docs do **not** exist here — `daily` is only a flag on commands like `obsidian tasks daily`); a general-flags list (`--copy` to clipboard, `total`, `counts`, `format=`); and two added link/index queries, `obsidian tags counts sort=count` and `obsidian tasks todo|done`, that read Obsidian's resolved index instead of grepping.

## [1.12.0] - 2026-06-30

### Added
- **`related` operation** (`references/operations/related-notes.md`, aliases `links`/`backlinks`/`neighbors`/`connections`) — a read-only workflow for assembling a note's neighborhood from Obsidian's resolved link graph instead of grep. Four layers: incoming links (`obsidian backlinks … counts`), outgoing links (`obsidian links`), second-degree co-cited/co-referenced neighbors (fan-out over the first-degree set), and shared-context tag/identifier relatives. Documents the per-command flag asymmetry (`backlinks` takes `counts`/`format`/`total`; `links` only `total`), the whole-vault queries (`orphans`/`deadends`/`unresolved`), the `property:read name=tags` unreliability (read frontmatter directly), and ties into the value-titled-identifier and investigation-Dataview vault patterns. Grouped, deduped output rather than raw TSV.

### Changed
- `SKILL.md` — skill version bumped to 1.12.0; added `related` to the dispatch table; added a link-exploration pointer to the "Renames, Moves, Deletes, and Link Analysis" section; description now mentions exploring a note's related notes.

## [1.11.0] - 2026-06-30

### Added
- **Report note type** for reader-facing reports that distill an investigation (or other notes) into plain-language findings for a specific audience and decision. `tagging.md` (component 1.4.0 -> 1.5.0) adds the `report` primary tag. New **Report template** (v1.0.0): a `> [!abstract]` for/against summary line, then `Why it could be a good fit`, `What gives us pause`, and `What to confirm before deciding` sections. First use: `Report - Provive Wellness Brentwood TN (06-30-26)`, distilled from the `Provive Wellness Investigation`.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.9.0; tagging row to 1.5.0
- `SKILL.md` — skill version bumped to 1.11.0; vault-standards reference to 1.9.0

## [1.10.0] - 2026-06-29

### Added
- **OSINT investigation note types codified** (to back the new global `osint-investigation` skill). `tagging.md` (component 1.3.0 -> 1.4.0) adds six primary tags: `investigation`, `status-update`, `organization`, `location`, `post`, and `link` (a link/identity note: evidence + verdict on a connection between entities, distinct from the linking *standard*). `naming.md` (component 1.1.0 -> 1.2.0) adds filename patterns for each (`<Subject> Investigation`, `<A> and <B> Identity Link`, etc.) plus an "Investigation & Entity Note Names" detail section covering value-titled identifier notes (addresses by literal value with the `location` tag, phones in hyphenated form).
- **Investigation template** enriched to the full layout (template 1.0.0 -> 1.1.0): `Outcomes` (lifted concerns) at the top, then `Raw Log`, `Open Questions / Next Steps`, and the `Sources` / `Extracted` / `Organizations` Dataview aggregations.
- **Link Note template** (new, v1.0.0) for link/identity notes: enumerated evidence (for / against), a confidence verdict, and what would confirm or refute.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.8.0; tagging row to 1.4.0, naming row to 1.2.0
- `SKILL.md` — skill version bumped to 1.10.0; vault-standards reference to 1.8.0

## [1.9.0] - 2026-06-29

### Added
- **Note-locating guidance** — `vault-layout.md` gains a "Locating Notes" section recording that the Glob tool intermittently missed notes in the large flat `The Pile/` folder (returned nothing or only non-markdown assets) while Grep with an explicit path and the Obsidian CLI found them; prefer Grep or `obsidian files`/`search`. Cross-linked from `obsidian-cli.md`.
- **Search-before-create step** — `references/operations/create-note.md` now directs a vault search for an existing note on the same subject or identifier before creating, especially for reference/website, person, location, and value-titled notes, to avoid the parallel-duplicate trap (this session created duplicate `provivetn.com` and LinkedIn notes that an existing investigation already covered).

### Changed
- `SKILL.md` — skill version bumped to 1.9.0

## [1.8.0] - 2026-06-29

### Added
- **Obsidian CLI policy** — new `references/obsidian-cli.md` plus a SKILL.md section. Renames and moves must go through the official Obsidian CLI (Obsidian 1.12.7+, `obsidian rename` / `obsidian move`), which rewrites backlinks vault-wide, rather than raw filesystem create-and-delete, which orphans links. Also routes deletes (`obsidian delete`, recoverable) and link analysis (`obsidian backlinks` / `links` / `search`) through the CLI, keeps note creation and linting in the skill flow, and documents prerequisites and a manual grep-sweep fallback. Captures the **installer-version gotcha** (in-app auto-update does not update the installer/`.exe`, and the CLI redirector ships with the installer, so the installer must be 1.12.7+; the `Obsidian.com` redirector is what `obsidian` resolves to on Windows) and the **headless reality** (the management CLI has no headless mode and needs the GUI running; `obsidian-headless` is Sync-only and not a substitute; the Windows pattern is to launch the app minimized in the background). Verified live on 2026-06-29 (installer 1.12.7, active vault `main`); documents the real command syntax (`key=value`, e.g. `obsidian rename file="Old" name="New"`, not `--flags`) and `obsidian unresolved` as the proper post-rename orphan check.

## [1.7.0] - 2026-06-29

### Added
- **Website notes named by domain** — `naming.md` (component 1.0.0 -> 1.1.0) now requires website reference notes to be titled by their bare registrable domain (`provivetn.com`), with human-readable names carried as `aliases`, one note per whole site (sub-pages discussed inside, never their own notes). The domain is the unique identifier, so the same site referenced twice collides on one note. Documents why the bare domain is used (filenames cannot contain `:` or `/`) and why social profile URLs (which carry a path) stay descriptively named.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.7.0; naming component row to 1.1.0
- `SKILL.md` — skill and vault-standards versions bumped to 1.7.0

## [1.6.0] - 2026-06-29

### Added
- **Linking & aggregation standard** — new `references/standards/linking.md` component (v1.0.0). Codifies the inversion the vault already practices: a relationship is declared once on the child (a readable back-link sentence) and surfaces on the parent through a Dataview query, never a hand-maintained list. Documents both mechanisms (tag aggregation for index/MOC notes, inlink-plus-tag aggregation for parent/value/investigation notes) and ties in the value-titled-note and project-status-update cases.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.6.0; Linking added to the component table; "six" component files is now "seven"
- `SKILL.md` — skill and vault-standards versions bumped to 1.6.0

## [1.5.0] - 2026-06-29

### Added
- **Value-formatting rule for phone numbers** — `body-format.md` (component 1.0.0 -> 1.1.0) now carries a "Value Formatting" section requiring phone numbers in the hyphenated form with no parentheses or spaces (`615-703-6651`), everywhere they appear (titles, aliases, body, frontmatter). A standard format keeps the same number a single string, so value-titled phone notes collide reliably across entities.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.5.0; body-format component row to 1.1.0
- `SKILL.md` — skill and vault-standards versions bumped to 1.5.0

## [1.4.0] - 2026-06-23

### Added
- **Research Paper pattern** — new `Research Paper` template (vault `Templates/`, v1.0.0) for capturing academic papers, with the PDF downloaded into the vault and embedded at the bottom under `## Paper`. Inherits the `reference/paper` tag and the `reading/*` lifecycle from the reference-note conventions.
- **Typed bibliographic frontmatter** — `frontmatter.md` (component 1.0.0 -> 1.1.0) now documents the `authors` / `year` / `arxiv` / `url` fields the Research Paper template uses, so Dataview can index papers by author, year, and reading status. `authors` holds quoted person-note wikilinks (full name) and `year` a quoted wikilink to the year note, both may stay unresolved, and the standard now scopes the no-wikilinks rule to `tags` only, explicitly allowing wikilinks in typed link fields.
- `references/operations/capture-research-paper.md` — workflow for the full capture: resolve the source to a deterministic arXiv PDF URL, download into `Files/` named after the paper title, then create the note from the template and embed the PDF.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.4.0; frontmatter component row to 1.1.0
- `references/operations/create-note.md` — routes academic-paper sources to the new capture flow
- `SKILL.md` — skill bumped to 1.4.0; stale vault-standards reference corrected to 1.4.0

## [1.3.1] - 2026-05-29

### Changed
- `Templates/Repository Note.md`: moved the URL, Org, Visibility, Language, and Project fields out of the `[!info]` callout to sit as plain bold label lines at the top of the note body. Template bumped to 1.1.0. The five existing Void Projects repository notes were migrated to match.

## [1.3.0] - 2026-05-29

### Added
- **Voice standard** — new `references/standards/voice.md` component (v1.0.0) capturing Blake's writing voice (tense by note type, POV, sentence structure, punctuation fingerprint, diction, openings, register calibration, do/don'ts), reverse-engineered from a wide sample of his own notes. Notably documents that Blake uses no em dashes and no semicolons in his own prose.

### Changed
- `references/standards/README.md` — aggregate standards version bumped to 1.2.0; Voice added to the component table; "five" component files is now "six"
- `references/operations/create-note.md` — body-population step now directs the author to write prose in Blake's voice per `voice.md`
- `Templates/Repository Note.md` — `standard-version` bumped to 1.2.0 to track the new aggregate

## [1.2.0] - 2026-05-29

### Added
- **Repository note pattern** — new `Repository Note` template (vault `Templates/`, v1.0.0) and new `repository` primary tag in `tagging.md`, for capturing Git/GitHub repos as first-class notes (URL, org, visibility, language, linked project).

### Changed
- `references/standards/tagging.md` — bumped component to 1.2.0 (added `repository` primary tag)
- `references/standards/README.md` — reconciled aggregate standards version to 1.1.0 (the prior tagging 1.1.0 bump had not been propagated to the aggregate) and corrected the tagging component row
- Corrected the hardcoded vault path from `C:\Users\Blake\Documents\main` to `C:\Users\Blake\Documents\Obsidian\main` across `SKILL.md`, `references/vault-layout.md`, `references/operations/create-note.md`, and the three `scripts/*.cs` files — the old path was stale and broke every operation

## [1.1.0] - 2026-04-13

### Added
- `scripts/lint-scan.cs` — deterministic scanner; detects all standards violations across a file, directory, or the whole vault; emits a structured JSON report
- `scripts/lint-autofix.cs` — deterministic autofixer; applies safe, unambiguous fixes to a single note (H1 removal, admonition conversion, version bumps)

### Changed
- `references/operations/lint-note.md` — rewritten from stub to full workflow (scan → autofix → judgment guide with violation catalog)
- `SKILL.md` — scripts table added; lint no longer marked as stub; bumped to v1.1.0

## [1.0.0] - 2026-04-13

### Added
- Initial skill scaffold
- Vault standards v1.0.0 (frontmatter, body-format, tagging, naming, versioning)
- `create-note` operation
- `lint-note` operation — STUB (implemented in 1.1.0)
- `search-vault` operation — STUB
- `scripts/list-templates.cs` — enumerate vault templates
- Vault template updates:
  - `Atomic Note` → 2.0.0
  - `Daily Note` → 2.0.0
  - `Index Note` → 2.0.0
  - `Feature Note` → 2.0.0
  - `Project Template` → 2.0.0
  - `Project Idea Template` → 2.0.0
  - `Project Feature Template` → 2.0.0
  - `Reference Note` → 1.0.0 (new)
  - `Person Note` → 1.0.0 (new)
  - `Recipe Note` → 1.0.0 (new)

## Versioning Rules

See [references/standards/versioning.md](references/standards/versioning.md). Summary:
- **MAJOR** — breaking change; existing notes may need migration
- **MINOR** — additive, backwards-compatible
- **PATCH** — clarification or typo; no note impact
