---
name: diataxis
description: >-
  Write and organize technical documentation with the Diátaxis framework: the four
  modes (tutorial, how-to guide, reference, explanation), the compass for classifying
  content, the deep/functional quality model, and the iterative improvement workflow.
  Use when writing or restructuring docs, READMEs, guides, API/reference pages, or
  architecture/explanation write-ups; when a page feels tangled or mixes teaching with
  lookup; when deciding what kind of page something should be; or when auditing and
  splitting an existing documentation set into a navigable structure.
allowed-tools: Read, Write, Edit, Glob, Grep
---

# Diátaxis: documentation that fits user needs

Diátaxis (from the Greek for "across/through arrangement") is a way of thinking about and
doing documentation. Its claim: technical documentation serves **four distinct user needs**,
and so has **four modes**, each with its own purpose, shape, and voice. The single biggest
quality lever is keeping them separate. Most bad docs are bad because one page tries to teach,
instruct, describe, and explain all at once, and so does none of them well.

> Source: https://diataxis.fr/ . This skill captures the framework so you can apply it; when
> in doubt about a fine point, the site is authoritative.

## The map: two axes, four modes

A craft has two dimensions, and documentation must serve both halves of each:

- **Action vs cognition** - practical steps (knowing *how*) vs theoretical knowledge (knowing *that*).
- **Acquisition vs application** - studying to gain skill vs working with the skill you have.

Two binary axes give exactly four quadrants. "There are only two dimensions ... This is why
there are necessarily four quarters to it, and there could not be three, or five."

|                     | Acquisition (study)     | Application (work)   |
| ------------------- | ----------------------- | -------------------- |
| **Action** (steps)  | Tutorial                | How-to guide         |
| **Cognition** (knowledge) | Explanation       | Reference            |

## The compass: classify any piece of content

When you are unsure what a page, section, or paragraph should be, run the compass. Ask two
questions and read off the answer:

| If the content…   | …and serves the user's… | …then it must belong to… |
| ----------------- | ----------------------- | ------------------------ |
| informs action    | acquisition of skill    | a tutorial               |
| informs action    | application of skill    | a how-to guide           |
| informs cognition | application of skill    | reference                |
| informs cognition | acquisition of skill    | explanation              |

Use it as a working tool: the moment a draft feels off, stop and ask the two questions about
the specific sentence in front of you. If a how-to guide drifts into "the reason this works
is...", that sentence is explanation and belongs elsewhere.

## The four modes at a glance

Each mode has a one-line litmus test. Read the matching `references/` file before writing one
in earnest, and start from the matching `templates/` scaffold.

| Mode | Serves | Litmus test | Voice |
| --- | --- | --- | --- |
| **Tutorial** | a learner acquiring skill, by doing | "Could a newcomer follow this end to end and succeed, learning through the doing?" | "we will…", "you will see…" |
| **How-to guide** | a competent user pursuing a goal | "Does this get someone who already knows the basics through a real-world task?" | "To do X, do Y. If Z, then…" |
| **Reference** | a working user looking something up | "Is this neutral, complete description a user consults mid-task?" | "X is. X takes. X returns." |
| **Explanation** | someone building understanding, at leisure | "Does this illuminate the why, the context, the trade-offs, read away from the keyboard?" | "X exists because…", "one approach is…" |

The hardest pair to keep apart is **tutorial vs how-to**: a tutorial teaches a beginner (the
author owns the goal and the outcome), a how-to helps a competent person reach *their* goal
(the user owns the goal). And the easiest leak is **explanation bleeding into everything**:
keep it bounded so it does not absorb the other three.

## The non-negotiable principle: do not mix modes

- A **tutorial** does not stop to explain alternatives or theory. It links out instead.
- A **how-to guide** does not teach fundamentals or list every option. It assumes competence
  and links to reference.
- **Reference** describes and *only* describes. No instruction, no opinion, no recipes.
- **Explanation** discusses and may hold opinion, but contains no step-by-step procedure and
  no exhaustive description.

When one mode needs another, **link, don't inline**: "For the full list of options see the X
reference," "To learn the basics first, follow the X tutorial."

## The workflow: improve continuously, in small steps

Diátaxis is "a guide, not a plan." Do not redesign the whole documentation set up front, and
do **not** create empty tutorial/how-to/reference/explanation folders to fill in later. Work
the cycle on one small thing at a time:

1. **Choose** something to improve (a section, a paragraph, even a sentence).
2. **Assess** it: which of the four needs does it serve, and how well? Run the compass.
3. **Decide** the single next action that yields an immediate improvement.
4. **Do** it, and ship it.

Then repeat. Aim for documentation that is "complete, not finished": useful and coherent at
every step, never blocked on a grand rewrite. Documentation grows like an organism, from the
inside out.

## Applying it to an existing (messy) doc set

This is the common real case, including rewriting docs we already shipped. Procedure:

1. **Inventory** the existing pages/sections (use Glob/Grep to find `*.md`, READMEs, headings).
2. **Run the compass on each chunk**, not each file. A single page often contains all four
   modes tangled together; classify paragraph by paragraph.
3. **Separate by mode.** Pull tutorial content, how-to content, reference content, and
   explanation content into distinct pages. A "one big architecture page" usually splits into:
   an *explanation* (how/why it works), one or more *how-to guides* (add a new X), and
   *reference* (the API/contract tables).
4. **Replace inlined cross-mode content with links.**
5. **Build landing pages**, not bare lists. Group under the four modes once you have enough
   material to warrant it (see the workflow rule: do not pre-build empty buckets).
6. **Keep navigation lists short.** "Seven items seems to be a comfortable general limit"; past
   that, regroup. Let the structure get "as complex as it needs to be," no more.

## Quality: functional first, then deep

- **Functional quality** (accuracy, completeness, consistency, usefulness, precision) is the
  measurable floor. Get this right first.
- **Deep quality** (feels good to use, has flow, fits human needs, anticipates the user) is the
  ceiling, and it is "conditional upon functional quality." Diátaxis earns deep quality mainly
  by removing friction, e.g. not interrupting a how-to with a digression into theory.

Good docs "feel right": the reader is never yanked between needs mid-page.

## Reference files

Read the relevant one before writing or reviewing that mode in depth:

| File | Contents |
| --- | --- |
| `references/foundations.md` | Why exactly four, the axes in full, the learning/work cycle, quality, scaling to complex hierarchies. |
| `references/tutorials.md` | Writing learning-oriented tutorials: the teaching contract, reliability, voice, what to cut. |
| `references/how-to-guides.md` | Writing goal-oriented how-to guides: real problems, flow, titles, conditional imperatives. |
| `references/reference.md` | Writing information-oriented reference: describe-and-only-describe, mirror the product, consistency. |
| `references/explanation.md` | Writing understanding-oriented explanation: context, why, alternatives, bounding it. |

## Templates

Copy the matching scaffold as a starting structure:

- `templates/tutorial.md`
- `templates/how-to-guide.md`
- `templates/reference.md`
- `templates/explanation.md`
