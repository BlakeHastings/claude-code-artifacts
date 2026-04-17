# Operation: Search Vault

> **⚠ STATUS: STUB — NOT YET IMPLEMENTED IN SKILL v1.0.0 ⚠**
>
> This file describes the *intended shape* of the search operation so the scaffold is in place. **Do not attempt to run this operation until it is implemented.** If the user asks to search, tell them this operation is a stub and ask if they want to implement it now.

## Intended Behavior

Search the vault for notes matching a query — by text, by tag, by template, by version drift, by backlink, etc.

## Intended Query Types

- **Content search** — ripgrep across `The Pile/` and `Index/`
- **Tag search** — notes having a specific primary or secondary tag
- **Template search** — notes created from a specific template, optionally at a specific version
- **Backlink search** — notes linking to a specific note
- **Orphan search** — notes no other note links to
- **Version drift search** — notes whose `template-version` or `standard-version` is below current

## When To Implement

When built-in Obsidian search is insufficient, or when programmatic filters are needed (e.g., "all reference notes at standard-version < 1.1.0").
