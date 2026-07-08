---
standards-component: linking
component-version: "1.0.0"
part-of-standards: "1.0.0"
---

# Linking & Aggregation Standard

How notes relate to each other. The governing principle: **a relationship is declared once, on the child, and surfaces automatically on the parent through Dataview.** Never hand-maintain a list of children inside a parent note.

Manually listing children in a parent means every new child has to be added in two places, and the parent's list rots the moment someone forgets. Pushing the link onto the child and letting the parent query for it makes the list self-maintaining: add the child, and it shows up on its own. It also records each child's origin on the child itself, which reads naturally when you open that note later.

## The Two Mechanisms

### 1. Tag aggregation (index / MOC notes)

The parent gathers everything carrying a tag. Used by `index` notes. The child needs no special back-link, just the right tag.

````markdown
## Subprojects
```dataview
LIST
FROM #void-projects AND #project/active
WHERE file.name != "Void Projects"
SORT file.name ASC
```
````

### 2. Inlink aggregation (parent, value, and investigation notes)

The child writes a short, readable sentence that links back to the parent. The parent queries for notes that link to it, narrowed by tag.

- **Child** (somewhere in its prose, written to read naturally):

  > Discovered during the [[Provive Wellness Investigation]].

  or

  > Pointer note for [[Provive Wellness Website|Provive Wellness]]'s LinkedIn presence.

- **Parent** (replaces what would have been a hand-typed list):

  ````markdown
  ## Sources
  ```dataview
  LIST
  FROM [[Provive Wellness Investigation]] AND (#reference OR #post)
  SORT file.name ASC
  ```
  ````

`FROM [[Parent]]` selects every note that links to the parent. The `AND #tag` clause sorts those inbound notes into categories (sources vs extracted, accounts vs posts, etc.), so one parent can show several typed lists.

## Rules

1. **Parents never hand-maintain child lists.** If a section would be a bullet list of links to child notes, it is a Dataview block instead.
2. **The child carries the relationship.** Every child states its parent inline, in a sentence that reads naturally on its own (not a bare `[[link]]` dumped under a heading).
3. **Categories come from the child's tags**, surfaced by the `AND #tag` clause in the parent's query. Keep the readable section heading (`## Sources`, `### Online presence`) above the query.
4. **Use plain DQL** (fenced ```dataview), `LIST` by default, `SORT file.name ASC`. Reach for `TABLE` only when a column of metadata genuinely helps. `dataviewjs` is not needed for this pattern.
5. This inversion also applies to **status updates on a project** (the update links the project, the project queries the updates) and to **value-titled notes** (entities link the shared address or phone, the value note queries its users).
