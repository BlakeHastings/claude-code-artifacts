# Changelog

All notable changes to the `obsidian-vault` skill.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
