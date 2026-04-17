---
standards-component: versioning
component-version: "1.0.0"
part-of-standards: "1.0.0"
---

# Versioning Standard

Three independent version numbers govern the skill, standards, and templates. All follow [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## Version Dimensions

| Dimension | Where It Lives | Bumps When |
|-----------|---------------|------------|
| **Skill version** | `CHANGELOG.md` at skill root | Any change to skill structure, operations, or references |
| **Standards version** (aggregate) | `references/standards/README.md` frontmatter | Any component bumps — aggregate takes the highest bump |
| **Standards component version** | Each `references/standards/*.md` frontmatter | That specific component changes |
| **Template version** | Each template file in vault `Templates/` frontmatter | That specific template changes structurally |

## Semver Bump Rules

- **MAJOR** (`X.0.0`) — Breaking change. Existing notes written against the previous MAJOR need migration. Examples: renaming a required field, removing a field, changing filename conventions.
- **MINOR** (`x.Y.0`) — Additive, backwards-compatible. Existing notes still conform. Examples: new optional field, new tag sub-category, new template.
- **PATCH** (`x.y.Z`) — Clarification only. No note is affected. Examples: typo fix, prose rewording, example swap.

## When the Aggregate Standards Version Bumps

The aggregate version in `references/standards/README.md` equals the **highest** component change since the last aggregate bump:
- `frontmatter.md` bumps `1.0.0 → 1.1.0` (MINOR) → aggregate goes `1.0.0 → 1.1.0`
- `tagging.md` bumps `1.0.0 → 2.0.0` (MAJOR) + `frontmatter.md` bumps `1.0.0 → 1.1.0` (MINOR) → aggregate goes `1.0.0 → 2.0.0`

## When a Template Version Bumps

Each template in `Templates/` carries its own version in its frontmatter:
- **MAJOR** — template's required fields change
- **MINOR** — optional sections added
- **PATCH** — example text tweaked

## Migration

When versions bump, the `lint-note` operation (stub for skill v1.0.0) is the mechanism for finding and migrating existing notes. Notes store `template-version` and `standard-version` so lint can detect drift.
