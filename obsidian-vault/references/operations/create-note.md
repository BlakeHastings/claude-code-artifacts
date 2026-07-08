# Operation: Create Note

Create a new note in the vault from a template.

> **Research papers have their own flow.** If the source is an academic paper (an arXiv link, a DOI, a publisher page, or a PDF the user wants captured), use [capture-research-paper.md](capture-research-paper.md) instead: it downloads the PDF into the vault and embeds it on top of these steps.

> **Check for an existing note before creating.** Especially for reference/website, person, location, and value-titled (address / phone / URL) notes, first search the vault for the same subject or identifier (`obsidian search query="..."` or `obsidian files`, or Grep with an explicit `path`). If a note already exists, for example from a parallel investigation, **augment it instead of creating a duplicate**. Value-titled notes (a domain, an address, a phone number) collide on one filename by design, but a prose-named note (a person, or the same site under a different descriptive name) will silently duplicate. See [the note-locating guidance](../vault-layout.md#locating-notes).

## Steps

1. **Read the standards** — start with [`references/standards/README.md`](../standards/README.md), then read each component file. This gives you the current aggregate standards version and all the rules the new note must follow.

2. **Enumerate available templates** by running the lookup script:
   ```bash
   dotnet run scripts/list-templates.cs
   ```
   This returns each template's name, filename, `template` field, `template-version`, `standard-version`, and primary tag.

3. **Pick the template** that matches the user's intent. If nothing matches, ask the user if a new template should be built. If yes:
   - Build it under `C:\Users\Blake\Documents\Obsidian\main\Templates\<Name>.md` at version `1.0.0`
   - Ensure it conforms to [`references/standards/frontmatter.md`](../standards/frontmatter.md)
   - Then continue this flow

4. **Read the chosen template file** from `C:\Users\Blake\Documents\Obsidian\main\Templates\<Name>.md` to see its frontmatter schema and body structure.

5. **Generate the new note's frontmatter**:
   - `id` — current timestamp in `YYYYMMDDHHmm` format. Use Bash: `date +%Y%m%d%H%M`
   - `template` — the template's `template` field
   - `template-version` — the template's current version (from its frontmatter)
   - `standard-version` — the current aggregate standards version (from `references/standards/README.md`)
   - `tags` — the primary tag for this note type (see [`references/standards/tagging.md`](../standards/tagging.md)) plus any topic tags the user requests

6. **Choose the filename** per [`references/standards/naming.md`](../standards/naming.md).

7. **Place the new note** at:
   - `C:\Users\Blake\Documents\Obsidian\main\The Pile\<Filename>.md` for most notes
   - `C:\Users\Blake\Documents\Obsidian\main\Index\<Filename>.md` for index / MOC notes

8. **Populate the body** per the template's structure and the user's content. Do NOT include an H1 repeating the title (see [`references/standards/body-format.md`](../standards/body-format.md)). Write the prose in Blake's voice per [`references/standards/voice.md`](../standards/voice.md). Match tense and POV to the note type, ground abstractions in concrete analogies, and use no em dashes and no semicolons.

9. **Report the file path** to the user.

## Notes

- Templater plugin is NOT installed in this vault. Core template plugin's `{{date:...}}` syntax only fires on Obsidian-driven template insertion, so when creating notes programmatically via this skill, **manually compute and substitute** the date/time values rather than leaving `{{...}}` placeholders in the file.
- The lookup script always reflects current vault state — if the user adds a template manually in Obsidian, the script picks it up automatically. **Never hardcode a template list anywhere in the skill.**
- **Investigation evidence (cross-reference):** when capturing verification or proof during an OSINT dig, follow the osint-investigation skill's "one proof note per entity × source, linked from the entity" convention (its principle 3) rather than bundling multiple subjects into one note. Each captured record is its own atomic proof note (source URL + screenshot/PDF + facts) titled for and linking to the entity it concerns; that entity's own note carries a **Verification / Records** section linking its proof notes.
