# Vault Layout

**Vault path:** `C:\Users\Blake\Documents\main`

This path is hardcoded for skill v1.0.0. Iterate later if the vault moves or a second vault is added.

## Directories

| Path (relative to vault) | Contents |
|--------------------------|----------|
| `The Pile/` | All notes, flat (no subfolders). ~236+ files. |
| `Index/` | MOC/index notes that aggregate and link to notes in The Pile. |
| `Templates/` | Note templates. **Source of truth** for `template` + `template-version` fields. |
| `Excalidraw/` | Excalidraw diagram notes. |
| `Files/` | Pasted images and assets referenced by notes. |

## Where New Notes Go

- **Regular notes** → `The Pile/`
- **Index/MOC notes** → `Index/`
- **Templates** → `Templates/` (only when building a new template)

## Installed Plugins (Relevant)

- **Dataview** — used by Index notes for queries by tag
- **Tasks** — `- [x]` / `- [-]` syntax with `✅ date` markers
- **Excalidraw**, **Kanban**, **Natural Language Dates**, **Copy Image**
- **Templater is NOT installed** — templates use core template plugin syntax (`{{date:...}}`, `{{time:...}}`). This limits dynamic lookup; standards version is hardcoded per template.
