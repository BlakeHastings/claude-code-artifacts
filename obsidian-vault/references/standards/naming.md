---
standards-component: naming
component-version: "1.3.0"
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
| Investigation | `<Subject> Investigation` | `Provive Wellness Investigation` |
| Organization | Legal entity or brand name, as used | `PWS Wayne LLC` |
| Location / address | Literal address (value-titled), descriptive name as alias | `15 Perlman Dr, Spring Valley, NY 10977` |
| External-system record (NPI, etc.) | `<SYSTEM> <id>` (system-prefixed value-titled), descriptive name as alias | `NPI 1174242564` |
| Status update | `Status Update - <subject> (MM-DD-YY)` | `Status Update - Case Manager Call (06-29-26)` |
| Post | `<Author> - <Platform> - Post - <topic>` | `Provive Wellness - LinkedIn - Post - Oscar Health In-Network` |
| Link / identity | `<A> and <B> Identity Link` | `Moe Stern and Moishy Stern Identity Link` |

## Reference Note Names — Detail

Format: `<Material Name> <Material Form>`

- **Material Name** = the title of the source ("Complexity Theory", "SPQR A History of Ancient Rome")
- **Material Form** = how you accessed it (`PDF`, `Book`, `Youtube Series`, `Podcast`, `Website`, `Video`)

Examples:
- `SPQR A History of Ancient Rome Book`
- `The Nature of Code Youtube Series`
- `Complexity Theory Research Paper PDF`

The **tag** reflects content category (`reference/book`, `reference/paper`). The **filename** reflects form. These diverge on purpose: a research paper distributed as a PDF is still a paper.

## Website Reference Notes — Detail

A reference note about a website is named by its **bare registrable domain**: lowercase, no scheme, no `www.`, no path, no trailing slash. So `provivetn.com`, not `https://provivetn.com/` or `Provive Wellness Website`. The domain is the site's unique identifier, so if the same site is referenced again it lands on the same note and the reuse shows up immediately in backlinks (the same logic as the value-titled address and phone notes).

- **Aliases carry the human-readable names.** Add an `aliases` entry for each name a person would actually type or read, e.g. `Provive Wellness Website`, `Provive Wellness TN Site`. Prose elsewhere can link by those names and still resolve to the domain note.
- **One note per whole site.** Never make a note for a single sub-page. Capture the entire site in one note and discuss notable sub-pages (a landing page, an about page, a team page) inside it. This keeps a site from fragmenting across many notes and keeps the domain a clean collision point.
- **Why the bare domain and not the full URL:** filenames cannot contain `:` or `/` (see the general rules), so `https://provivetn.com/` is not a legal name. That same restriction is why social profile URLs, which carry a path like `/company/provivewellness`, stay as descriptively named account notes (`Provive LinkedIn Account`) rather than URL-named notes.

## Investigation & Entity Note Names — Detail

- **Investigation** notes are `<Subject> Investigation`, so the subject reads first and the Dataview back-link queries (`FROM [[<Subject> Investigation]]`) are obvious.
- **Link / identity** notes are titled for the entities they connect, ending in `Identity Link` (or `... Link`), e.g. `Moe Stern and Moishy Stern Identity Link`.
- **Organization** notes use the legal-entity or brand name exactly (`PWS Wayne LLC`, `Amwell Recovery`).
- **Value-titled identifier notes** (the address / phone collision pattern): title by the **literal value**, with the descriptive name as an `aliases` entry, so the same address or number referenced from anywhere lands on one note and the reuse shows in backlinks. Phones use the hyphenated form with no parentheses or spaces (`615-640-9994`, per body-format). Addresses use the `location` primary tag; a specific facility/site may instead be an `<Org> <City>` note that links the value-titled address. Same collision logic as the website-by-domain rule above.

## External-System Identifier Records — Detail

Records pulled from an external registry (a CMS NPI record, and any similar opaque, system-assigned ID) are value-titled like addresses and phones, but the title is **prefixed with the issuing system's short name**: `NPI 1174242564`, not `1174242564`. The descriptive name (`Rize OC CMS NPI Record`) goes in `aliases`, and prose links by it still resolve: `[[NPI 1174242564|Rize OC CMS NPI Record]]`.

- **Why prefix the system.** A bare registry ID is an opaque number or string with no self-evident type, so `1174242564` on its own could collide with an unrelated system's identifier that happens to share digits (a DEA number, a CMS CCN, a state license number, an internal case ID). The prefix namespaces the value: every reference to that NPI lands on one note, and it can never be mistaken for a same-valued ID from another system. Phones and addresses do **not** take a prefix because their format is already self-identifying; an opaque registry ID is not.
- **Keep the issuer's canonical ID form, only prepend the prefix.** An NPI is the bare 10 digits, so `NPI 1174242564`. Do not reformat, zero-pad, or punctuate the ID itself.
- **One note per record.** Same collision logic as the website-by-domain and value-titled address/phone rules above. The new note still uses the appropriate `reference/*` template and tags (NPI records: `reference/documentation`, `npi`).
