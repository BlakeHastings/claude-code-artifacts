---
standards-component: naming
component-version: "1.0.0"
part-of-standards: "1.0.0"
---

# Naming Standard

How note filenames are chosen.

## General Rules

1. Title Case is acceptable; so is Sentence case for atomic ideas.
2. Use regular spaces, not hyphens or underscores.
3. No leading dates (except daily notes, which use `MM-DD-YY` as the filename).
4. Avoid special characters that break Obsidian links: `: / \ | ? * " < >`.

## Per-Type Conventions

| Note Type | Filename Pattern | Example |
|-----------|------------------|---------|
| Atomic idea | Sentence case, expressive | `Ignorance sculpts our search for truth` |
| Daily note | `MM-DD-YY` | `05-08-24` |
| Index / MOC | Topic name | `Projects`, `Cooking` |
| Project | Project name | `Gate Answer` |
| Feature | Feature name | `Guest Status Change Notification` |
| Reference | `<Material Name> <Material Form>` | `Complexity Theory Research Paper PDF` |
| Person | Full name | `Jonathan West` |
| Recipe | Dish name + "Recipe" | `Lemon Garlic Salmon & Rice Recipe` |

## Reference Note Names — Detail

Format: `<Material Name> <Material Form>`

- **Material Name** = the title of the source ("Complexity Theory", "SPQR A History of Ancient Rome")
- **Material Form** = how you accessed it (`PDF`, `Book`, `Youtube Series`, `Podcast`, `Website`, `Video`)

Examples:
- `SPQR A History of Ancient Rome Book`
- `The Nature of Code Youtube Series`
- `Complexity Theory Research Paper PDF`

The **tag** reflects content category (`reference/book`, `reference/paper`). The **filename** reflects form. These diverge on purpose — a research paper distributed as a PDF is still a paper.
