# Operation: Create Note

Create a new note in the vault from a template.

## Steps

1. **Read the standards** — start with [`references/standards/README.md`](../standards/README.md), then read each component file. This gives you the current aggregate standards version and all the rules the new note must follow.

2. **Enumerate available templates** by running the lookup script:
   ```bash
   dotnet run scripts/list-templates.cs
   ```
   This returns each template's name, filename, `template` field, `template-version`, `standard-version`, and primary tag.

3. **Pick the template** that matches the user's intent. If nothing matches, ask the user if a new template should be built. If yes:
   - Build it under `C:\Users\Blake\Documents\main\Templates\<Name>.md` at version `1.0.0`
   - Ensure it conforms to [`references/standards/frontmatter.md`](../standards/frontmatter.md)
   - Then continue this flow

4. **Read the chosen template file** from `C:\Users\Blake\Documents\main\Templates\<Name>.md` to see its frontmatter schema and body structure.

5. **Generate the new note's frontmatter**:
   - `id` — current timestamp in `YYYYMMDDHHmm` format. Use Bash: `date +%Y%m%d%H%M`
   - `template` — the template's `template` field
   - `template-version` — the template's current version (from its frontmatter)
   - `standard-version` — the current aggregate standards version (from `references/standards/README.md`)
   - `tags` — the primary tag for this note type (see [`references/standards/tagging.md`](../standards/tagging.md)) plus any topic tags the user requests

6. **Choose the filename** per [`references/standards/naming.md`](../standards/naming.md).

7. **Place the new note** at:
   - `C:\Users\Blake\Documents\main\The Pile\<Filename>.md` for most notes
   - `C:\Users\Blake\Documents\main\Index\<Filename>.md` for index / MOC notes

8. **Populate the body** per the template's structure and the user's content. Do NOT include an H1 repeating the title (see [`references/standards/body-format.md`](../standards/body-format.md)).

9. **Report the file path** to the user.

## Notes

- Templater plugin is NOT installed in this vault. Core template plugin's `{{date:...}}` syntax only fires on Obsidian-driven template insertion, so when creating notes programmatically via this skill, **manually compute and substitute** the date/time values rather than leaving `{{...}}` placeholders in the file.
- The lookup script always reflects current vault state — if the user adds a template manually in Obsidian, the script picks it up automatically. **Never hardcode a template list anywhere in the skill.**
