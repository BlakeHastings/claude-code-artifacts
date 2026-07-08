---
name: osint-investigation
description: >-
  Methodically investigate a subject entity (person, company, organization, politician, or facility) from open sources and build a sourced, graph-linked dossier in an Obsidian vault, then distill the patterns and concerns that matter to a decision. Use when the user wants to look into, vet, research, dig into, profile, or do due diligence on a subject ("look into X", "is X legit", "who owns or runs X", "build a profile of X", "investigate this facility / company / person"). This is a collaborative, interactive way of working WITH the user, never an autonomous pipeline. Defers to the obsidian-vault skill for note standards and references the playwright-cli skill for browser work.
allowed-tools: Read, Write, Edit, Bash, Glob, Grep, WebSearch, WebFetch, Agent, AskUserQuestion, Skill
---

# OSINT Investigation

Investigate a subject (person, company, organization, politician, facility) from open sources, capture findings as a sourced, graph-linked dossier in the Obsidian vault, and distill the patterns and concerns that matter to the user's decision.

## The one rule: work WITH the user

This is a way of working together, not a job to run. **Default to interactive; never execute end-to-end autonomously.** You research, propose, and capture; the user sets scope, picks which threads to pull, supplies their own findings, drives their browser past logins and CAPTCHAs, and makes the judgment calls. Go autonomous only on an explicit "go do X," then return to collaboration. If you have run several passes without checking in, you have drifted, stop and ask.

**The conclusions are the user's to draw, not yours.** Your role is to gather, verify, and organize evidence so the user can reach a defensible conclusion themselves. You are an aggregator, not a judge. Surface findings as proposed, evidence-linked claims for the user to confirm. Never record a verdict in the investigation that the user has not been shown and approved with sufficient evidence behind it. See principle 8 and `references/consolidation.md`.

## When to use / not

Use for an in-depth look at a subject. NOT for a single fact lookup, and never for autonomous mass-profiling of people.

## Core principles

1. **Collaborative, not autonomous** (see above).
2. **Due-diligence lens, not a hit piece.** Document facts and fair concerns toward a decision. Do not presume bad faith or import a fraud frame from a past investigation, let the subject and the sources set the tone.
3. **Facts hang off sources, one proof note per entity × source.** Every claim lives in a reference (source) note that records the source and URL; investigation and atomic notes point to those facts; understanding is consolidated from them. Analysis never floats free of a source. **Keep proof atomic and attached to its subject:** a piece of verification evidence about a specific entity (a clinician's license / discipline check, a company's filing) becomes its own proof note, titled for and linking to that entity (e.g. `Adam Meadows License Verification` links `[[Adam Meadows]]`), holding the source URL, the captured screenshot / PDF, and the extracted facts. Do not pool several subjects' evidence into one bundled "Records" note. The entity's own note carries a short **Verification / Records** section linking its proof notes; the investigation note links those proof notes and lifts only the synthesized conclusion, it never restates the underlying evidence.
4. **No fluff.** Build structure around what the user and sources actually provide; do not pad with invented criteria or empty scaffolding.
5. **Verify; never overclaim.** Label confidence (confirmed / likely / plausible / unverified) and say why. Common names and look-alikes are traps. Sever and quarantine unconfirmed identities. If challenged, re-examine and downgrade honestly, never defend a stretch.
6. **Let the graph do the work.** Extract atomic entities and link them so shared identifiers (addresses, phones, owners) collide and patterns surface through backlinks and Dataview, not prose lists.
7. **Build bottom-up, and annotate the signal.** Each dive into a category (a source domain: social presence, ownership, licensing, reviews) maps that category into atomic notes, one per account, post, or record, then *reads them up* into a synthesized section conclusion: a dated Raw Log entry that states what this slice of the investigation establishes. Lift the signal with Obsidian callouts grounded in that section's own evidence: `[!important]` for a load-bearing fact or conclusion that moves the picture, `[!warning]` for a concern or red flag. The `Outcomes` section then rolls the section assertions up into the handful of decision-level concerns and reassurances, each **promoted once** (not duplicated back into the Raw Log, not speculative, not editorializing past the evidence). So: atomic notes → section assertion (Raw Log, annotated) → decision roll-up (Outcomes).
8. **The agent aggregates; the user concludes.** You are an evidence aggregator, not a judge (this is the operational form of the one rule above). Do not write a conclusion into the investigation that the user has not seen and approved, with sufficient evidence behind it. Record material conclusions as **proposed, confirmable claims**, not settled facts: each belongs in a `Claims to Confirm` checklist near the top of the investigation, written as a discrete checkbox claim tagged by confidence type and **linked to the exact artifact the user would read to verify it** (the proof note, the screenshot, the source URL). The user reads the artifact and checks the box. Language matters: write "the evidence points to X, pending your confirmation," never "X is resolved / proven / settled." If you catch yourself asserting a verdict, convert it into a claim-to-confirm and raise it to the user. Distinguish the tags so the user knows what kind of checking each needs: a record-backed fact (spot-check the cited record), a site-visible fact (look at the page or screenshot), an **inference** (your reading of combined evidence, a judgment for the user to weigh), and a negative / "searched and found none" result (re-run the search). Inferences are where over-claiming hides, label them as inferences, never launder them into facts.

## Notes: defer to the obsidian-vault skill

All note creation, naming, linking, and standards go through the **`obsidian-vault`** skill, invoke it to create and lint notes. The note types this workflow uses: `investigation`, `status-update`, `person`, `organization`, `location`, `reference/*`, `post`, value-titled identifier notes, and link/identity notes. Honor these conventions (in obsidian-vault standards and the user's memories):

- **Back-link + Dataview inversion:** each child note states its origin inline (`Discovered during the [[X Investigation]].`); the investigation lists children with `LIST FROM [[X Investigation]] AND #tag`, never a hand-maintained list.
- **Value-titled identifiers:** phones and addresses become notes titled by the literal value (descriptive alias), so shared identifiers collide in backlinks. **External-registry records** (CMS NPI records especially, the common one here) are value-titled too but **prefixed with the issuing system**, e.g. `NPI 1174242564`, never the bare number, so an opaque ID cannot collide with a same-valued identifier from another system. The descriptive name (`Rize OC CMS NPI Record`) is the alias. See the naming standard's "External-System Identifier Records" detail.
- **Renames/moves via the Obsidian CLI** so backlinks rewrite (never raw create-and-delete).

**Investigation note layout:** `Outcomes` (top) → `Claims to Confirm` (the evidence-linked checklist the user signs off on, principle 8) → `Raw Log` (dated, append-only passes) → `Open Questions / Next Steps` → Dataview aggregations (`Sources` = `#reference`/`#post`, `Extracted` = `#person`/`#location`, `Organizations` = `#organization`).

## Workflow (states + gates)

🧑 = pause and get the user's input or decision.

1. **Frame** 🧑 — establish the subject, the goal, the lens/tone, and the scope. If it serves a larger goal, open or append the project note and log a status update for how the subject arrived.
2. **Scaffold** — create the investigation note (Outcomes placeholder, Raw Log, Open Questions, Dataview aggregations) via the obsidian-vault skill.
3. **Sweep** (loop) 🧑 — pick a source domain from `references/source-playbook.md` and work it as a *dive*: gather the category, capture each artifact as an atomic note (Extract, below), then read those up into a dated Raw Log entry that **concludes what this section establishes**, with `[!important]` / `[!warning]` callouts lifting the key facts and concerns (principle 7). Recommend the next thread; let the user choose. One pass at a time; check in between.
4. **Extract** — lift atomic entities into typed, back-linked notes; value-title shared identifiers; embed evidence screenshots, capturing each verification or record as its own proof note attached to the entity it concerns (principle 3), not bundled by facility or subject. These atomic notes are the raw material the section's Raw Log conclusion is built from. See `references/tools-and-tactics.md`.
5. **Verify** 🧑 — assess confidence; resolve identity and cross-entity questions in a link/identity note; sever and quarantine the unconfirmed. See `references/verification.md`.
6. **Consolidate** 🧑 — lift the concerns into the investigation's `Outcomes` and itemize each material conclusion in the `Claims to Confirm` checklist (proposed, evidence-linked, for the user to verify and sign off, principle 8), and lift the understanding into the project note. The user draws the conclusion, you assemble the evidence for it. See `references/consolidation.md`.

Loop 3–6 until the user is satisfied; then repeat for the next subject.

## Tools

- **WebSearch / WebFetch** for open search and page extraction.
- **Browser work: use the `playwright-cli` skill** for mechanics. The OSINT-specific tactics (attaching to the user's real browser to beat gated sources, sub-agents, APIs, evidence capture) are in `references/tools-and-tactics.md`.
- **Sub-agents** for parallel deep dives that **report findings back rather than writing to the vault**.

## Reference files

| File | Read when |
|------|-----------|
| `references/source-playbook.md` | Sweeping (choosing and working a source domain) |
| `references/tools-and-tactics.md` | Using the browser, sub-agents, APIs, or capturing evidence |
| `references/verification.md` | Assessing confidence or resolving an identity / cross-entity question |
| `references/consolidation.md` | Writing the Outcomes or lifting understanding into the project |
| `references/brand-vs-entity.md` | Investigating behavioral-health, treatment, or therapy organizations where the consumer-facing brand may differ from the legal entity |

## Related

- `obsidian-vault` skill — note standards, types, linking, and CLI renames.
- `playwright-cli` skill — browser automation mechanics.
- User memories: `investigation-backlink-dataview`, `capture-shared-identifiers-as-value-titled-notes`, `rename-via-obsidian-cli`.
