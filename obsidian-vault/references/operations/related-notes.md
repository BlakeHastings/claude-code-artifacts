# Operation: Related Notes

Assemble the neighborhood of a note: what links **in**, what it links **out** to, and the notes one hop further out that share its context. This is the read side of the graph. It does not modify anything.

Use it when the user asks for "related notes", "what links here", "backlinks", "what does this note connect to", "neighbors", "what's near this note", or when you yourself need a note's context before editing, renaming, or summarizing it.

## Get the relationships from the CLI, not from grep

Obsidian's CLI reads the running app's resolved link graph, so it catches alias-resolved links and embeds that a text grep miscounts or misses. Always prefer it here. The enablement prerequisites, the full-path fallback, and what to do when the app is not running are in [`../obsidian-cli.md`](../obsidian-cli.md) — read that first if any command below errors.

The note is addressed by `file="Name"` (resolves like a wikilink) or `path="The Pile/Name.md"` (exact). Most commands default to the active file if both are omitted.

## The four layers

Run them in order. Stop early if the user only asked for one (e.g. "backlinks" = layer 1 only).

### 1. Incoming — what links to this note (backlinks)

```
obsidian backlinks file="Note Name" counts
```

- `counts` appends the number of links from each source (a note linking 3 times signals tighter coupling).
- `format=json` (or `csv`) for structured output; default is TSV (`path<TAB>count`).
- `total` returns just the count.

These are the notes that consider this one worth pointing at. In this vault that includes Dataview-driven aggregations: an investigation note is the backlink target of every source/extracted note that says "Discovered during [[X Investigation]]", and a value-titled identifier note (an address, a phone number) is the backlink target of every entity that shares it. Backlinks are how those collisions surface. See the vault patterns in [`../../references/standards/linking.md`](../standards/linking.md).

### 2. Outgoing — what this note links to

```
obsidian links file="Note Name"
```

- `total` for just the count.
- No `counts`/`format` flags on this command (CLI asymmetry — `links` is plain, `backlinks` is rich).

### 3. Second-degree — neighbors one hop out

The notes that are *near* the target without linking it directly. Two kinds worth computing:

- **Co-cited** — other notes that the target's backlinkers also link to. Take each note from layer 1 and run `obsidian links file="<that note>"`; notes that recur across several backlinkers are siblings of the target.
- **Co-referenced** — other notes that the target's outgoing links also point back to or sit beside. Take each note from layer 2 and run `obsidian backlinks file="<that note>"`.

Only go to layer 3 when the user wants a real neighborhood ("everything around this", "map the cluster"), not for a plain backlink check. It is N extra CLI calls — fan them out in parallel and dedupe the union, dropping the target itself and the layer-1/2 notes already reported.

### 4. Shared-context — tag and identifier relatives

Graph edges are not the only relationship. Notes also relate by sharing a tag or a value-titled identifier.

- Read the target's frontmatter (`Read` the file) to get its `tags`. For each meaningful tag, `obsidian search query="#tag"` lists co-tagged notes. (`property:read name=tags` is unreliable here — read the frontmatter directly.)
- If the note is an entity in an investigation, its shared **address/phone** notes are already covered by layer 1 (those identifiers are value-titled notes and the entity links them). Pull them out of the backlink set and name them as shared-identifier relatives, since they mean "these entities collide on a real-world value" — see [[capture-shared-identifiers-as-value-titled-notes]].

## Whole-vault relationship queries

When the question is about the graph as a whole rather than one note:

- `obsidian orphans` — notes nothing links to (candidates for linking-in or archiving).
- `obsidian deadends` — notes with no outgoing links (candidates for connecting out).
- `obsidian unresolved verbose` — every dangling link target in the vault. Also the correct post-rename check.

## Output

Report the neighborhood grouped by relationship, most-connected first, each as a clickable wikilink-style reference with a one-line reason:

```
**Void Projects** — neighborhood

Incoming (8):
- [[Void Projects Index]] — index note (×1)
- [[Void Projects Site Repository]] — tightly coupled (×2)
- [[Larry Jones]], [[Jackson Torregrossa]] — collaborators
- [[Protostar]], [[Wormhole]], [[Constellation]], [[Space Trash]] — subprojects

Outgoing (12): … the subprojects plus each one's Repository note

Shared-context: notes tagged #project; …
```

Collapse long lists, lead with what is most linked, and say plainly when a layer is empty (e.g. "no second-degree neighbors beyond the direct set"). Do not dump raw TSV at the user.
