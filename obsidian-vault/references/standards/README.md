---
standards-version: "1.0.0"
---

# Obsidian Vault Standards

**Current aggregate version:** 1.0.0

Standards for how notes in Blake's vault are structured. Each component is versioned independently; the aggregate version above bumps when any component bumps.

## Components

| Component | File | Current Version |
|-----------|------|-----------------|
| Frontmatter schema | [frontmatter.md](frontmatter.md) | 1.0.0 |
| Body format | [body-format.md](body-format.md) | 1.0.0 |
| Tagging | [tagging.md](tagging.md) | 1.0.0 |
| Naming | [naming.md](naming.md) | 1.0.0 |
| Versioning rules | [versioning.md](versioning.md) | 1.0.0 |

## How To Use These

When **creating** or **linting** a note, read all five component files. They are short and independent.

The aggregate standards version (1.0.0) is what goes in the note's `standard-version` frontmatter field.

## How This Version Bumps

The aggregate version bumps by the **highest** of the component bumps since the last aggregate bump (MAJOR > MINOR > PATCH). See [versioning.md](versioning.md).
