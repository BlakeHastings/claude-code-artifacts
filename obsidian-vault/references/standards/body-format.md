---
standards-component: body-format
component-version: "1.0.0"
part-of-standards: "1.0.0"
---

# Body Format Standard

How the body of a note (everything after the frontmatter) is structured.

## Rules

1. **No H1 repeating the title.** The filename is the title. Starting the body with `# Title` duplicates it and pollutes hover-link previews. Start directly with content.
2. **No body-level `Status:` or `Tags:` lines.** Those moved to frontmatter.
3. **Wikilinks inline.** When a concept is referenced, link it inline in the prose: `[[Turing Machine]]`. Don't list links in a dedicated "Tags" section.
4. **Use native Obsidian callouts**, not the Admonition plugin:
   - `> [!important]`, `> [!quote]`, `> [!abstract]`, `> [!note]`
   - NOT `` ```ad-important `` or `` ```ad-quote `` (that syntax is from the old Second Brain vault)
5. **Horizontal rule `---`** before any trailing References section.
6. **Optional References section** at the bottom — only when a link has no natural home in the body prose. Ideally every link lives inline.
7. **Book / source references** may include an APA-style citation as the final line of the note.

## Example

```markdown
---
id: "202604131430"
template: "Atomic Note"
template-version: "2.0.0"
standard-version: "1.0.0"
tags:
  - idea
---

The idea that [[Ignorance]] sculpts our search for [[truth]] comes from the
observation that questions are always shaped by what we already know.

More prose here, linking [[other concepts]] inline as they're referenced.

> [!abstract] A pithy summary worth calling out

---
# References
[[Related Note With No Natural Inline Home]]
```
