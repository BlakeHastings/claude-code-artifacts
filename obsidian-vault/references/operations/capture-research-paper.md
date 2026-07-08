# Operation: Capture Research Paper

A specialization of [create-note](create-note.md) for academic papers. On top of a normal note it downloads the paper's PDF into the vault and embeds it at the bottom, so the full text reads in-place. Trigger this flow whenever the user hands you a paper (an arXiv link, a DOI, a publisher page, or a PDF) and wants it captured.

Use the `Research Paper` template (primary tag `reference/paper`), not the generic `Reference Note`.

## Steps

1. **Resolve the source.** Get the canonical landing page and a direct PDF URL.
   - **arXiv:** from any arXiv id `XXXX.XXXXX` (optionally with a `vN` suffix), the landing page is `https://arxiv.org/abs/<id>` and the PDF is `https://arxiv.org/pdf/<id>`. Both are deterministic, so no scraping is needed for the link itself.
   - **DOI / publisher:** the landing page is the DOI URL; the PDF link must be found on the page (not always open-access). If no open PDF exists, skip the download and tell the user.

2. **Pull metadata** for the frontmatter and `Source` line: full title, author list (published order), year, and the id/DOI. For arXiv, fetch the abstract page to read these. Keep the title verbatim.

3. **Choose the names.**
   - **Note filename:** `<Material Name> <Material Form>` per [naming.md](../standards/naming.md), e.g. `Agents Thinking Fast and Slow Research Paper PDF`. Material Name is the title with any `:` subtitle dropped (colons are illegal in filenames).
   - **PDF filename:** the **paper title only**, matching the note's Material Name, e.g. `Agents Thinking Fast and Slow.pdf`. Not the arXiv id, not the full note filename.

4. **Download the PDF into `Files/`** (the vault's asset folder). Verify it landed and is non-empty before embedding.
   ```bash
   curl -L -o "C:/Users/Blake/Documents/Obsidian/main/Files/<Title>.pdf" "https://arxiv.org/pdf/<id>"
   ```
   If the download fails, create the note anyway, leave `## Paper` empty, and tell the user the embed is missing.

5. **Create the note** from the `Research Paper` template following [create-note](create-note.md) steps 5-9, with these specifics:
   - **Frontmatter:** fill `authors` as a YAML list of **quoted person-note wikilinks** by full name (`- "[[Jane Smith]]"`), leaving them unresolved (do **not** create person notes); then `year` as a quoted wikilink to the year note (`"[[2024]]"`, left unresolved), `arxiv` (bare id, blank for non-arXiv), and `url` (landing page). See [frontmatter.md](../standards/frontmatter.md).
   - **Reading status:** add one `reading/*` tag. Default `reading/backlog`; use `reading/active` if the user says they are currently reading it. See [tagging.md](../standards/tagging.md).
   - **Body:** `Source` gets the citation prose. `Why It's On My List` is the only opinion section, written in Blake's voice. `Facts` and `Key Quotes` stay factual. Embed the PDF under `## Paper`, the **last content section**, with no horizontal rule above it:
     ```markdown
     ## Paper

     ![[<Title>.pdf]]
     ```
     The trailing `---` is reserved for an optional References block per [body-format.md](../standards/body-format.md). If a note has both, References sits **below** `## Paper`; the embed never goes under that rule.

6. **Report** the note path and confirm the PDF embedded.

## Notes

- The embed is `![[<filename>.pdf]]` with no path: Obsidian resolves it from `Files/` by filename, so the PDF name must be unique in the vault.
- Reference notes are "truth notes": facts only. The personal angle lives solely in `Why It's On My List`.
