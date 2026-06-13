# Changelog

All notable changes to the `obsidian-vault` skill.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
