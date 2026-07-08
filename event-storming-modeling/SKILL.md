---
name: event-storming-modeling
description: >-
  Reference for modeling a domain with Event Storming and tactical/strategic DDD: the sticky-note color
  vocabulary, the command-aggregate-event-policy flow grammar, and the conflations to avoid. Use when
  building or reviewing an event-storming diagram or model, choosing colors/labels for domain concepts,
  deciding whether something is a Policy vs an Invariant, representing aggregates/commands/events/read
  models/external systems/actors, mapping a DDD domain, generating such diagrams from code, or recovering
  a model from annotated .NET assemblies with Hindstorm. Covers what a topological (code-derived) view
  loses versus a workshop wall, and when a domain is a streaming/dataflow transformation pipeline (where
  forcing the command/aggregate grammar is over-modeling). For those, names the second-plane vocabulary:
  CEP / event abstraction hierarchy, soft real-time, supporting vs core subdomain, data on the inside vs
  outside, domain event vs measurement, and Event Modeling's Automation and Translation patterns, joined to
  the domain at an anti-corruption seam.
allowed-tools: Read, WebFetch, WebSearch
disable-model-invocation: false
---

# Event Storming & DDD Modeling Conventions

Cross-checked against Brandolini's *Introducing EventStorming*, the "Picture That Explains Everything,"
and practitioner sources. Event Storming has no rigid spec — these are the de-facto community standards.

## Sticky colors (the vocabulary)

| Concept | Color | Meaning |
|---|---|---|
| **Domain Event** | orange | Something that happened, past tense ("OrderPlaced"). The most stable convention. |
| **Command** | blue | Intent to change state ("PlaceOrder"). |
| **Actor / User** | small yellow | A person/role that issues commands. Distinct by size/shape from the aggregate. |
| **Aggregate** | large/pale yellow | Receives commands, enforces invariants, emits events. |
| **Policy** (process/reaction) | lilac / purple | "Whenever \<event\> then \<command\>" — reactive automation or human procedure. |
| **Read Model / View** | green | The data an actor reads before deciding. |
| **External System** | pink | Outside the boundary; issues commands or receives/raises events. |
| **Hotspot** | red | Friction, disagreement, unknowns. Human judgment — cannot come from code. |

Caveats: **Actor color is the softest convention** (often just a small yellow with a stick figure; treat
actor vs aggregate as different shapes/sizes of yellow). Pink is sometimes also used for bounded contexts.
Don't hard-assert one hex per color across legends. In a flat digital diagram you lose the size/shape cue,
so give actor and aggregate **genuinely different hues** or they're indistinguishable.

## The flow grammar ("The Picture That Explains Everything")

The canonical, self-reinforcing loop:

```
Actor (or Policy, or External System)  --issues-->  Command
Command  --invoked on / handled by-->  Aggregate
Aggregate  --emits / raises-->  Domain Event
Domain Event  --triggers / reacts to-->  Policy
Policy  --issues-->  Command            (loop)
Domain Event  --updates-->  Read Model  --read by-->  Actor
External System  --raises-->  Domain Event  (and can receive commands)
```

Hard rule worth encoding: **aggregates only receive commands and emit events — they never issue commands.**
Commands come from actors, policies, or external systems. Common edge verbs: *issues*, *invoked on* /
*handles*, *emits* / *raises* / *produces*, *triggers* / *reacts to*, *updates*, *enforces* (an invariant).

## Policy vs Invariant vs Business Rule (the conflation to avoid)

These are **different concepts** — labeling all of them "Policy" is a real modeling error:

- **Policy** (lilac) is strictly **reactive**: "whenever \<event\> then \<command\>." It sits *after* an
  event and *before* the next command, orchestrating cross-aggregate reactions. Defined by being
  triggered *by an event*.
- **Invariant / Business Rule** is a constraint enforced *inside* an aggregate while it handles a command,
  *before* any event ("reject order if stock unavailable," "cannot refund more than paid"). It governs
  whether a command is allowed to succeed; it is **not** event-triggered and does **not** issue a command.
  Classic Event Storming keeps it inside the aggregate or on a separate "business rule" sticky — never as
  a lilac Policy. Related DDD terms: invariant, specification, guard.

So: policies are *reactive logic triggered by events*; invariants are *constraints maintained within an
aggregate's transaction boundary*. Model them as distinct things with distinct labels/colors.

## Streaming / dataflow contexts: the second plane

The command/aggregate/event/policy grammar is built for **transactional** domains: an actor issues intent,
an aggregate guards invariants, and a decision can succeed or be **rejected**. A **streaming / dataflow**
context (audio to segment to text to decision; ETL; sensor and inference pipelines) is a different plane.
It continuously **transforms** immutable records that already happened and **cannot reject** anything.
Do not force one grammar onto both. Model two planes joined at a named seam:

```
sensor -> transform -> transform -> [ TRANSLATION / DECISION ] -> domain event -> policy -> command -> aggregate
|------------- dataflow plane -------------|       seam (ACL)      |-------------- domain plane --------------|
   continuous, immutable, can't reject                              discrete, has intent, can reject
```

Everything left of the seam is plumbing; only domain-significant facts cross it. It is the same seam under
many names: an Anti-Corruption Layer (Evans), Event Modeling's Translation/Automation slice (Dymitruk),
CEP's abstraction step (Luckham), the IoT "threshold-to-event," the ML "score-to-label."

**Vocabulary to reach for (name the plane instead of forcing stickies onto it):**

- **Complex Event Processing (CEP) / event abstraction hierarchy** (Luckham): the formal name for a pipeline
  that lifts a high-frequency stream up levels of meaning (frame -> segment -> utterance -> intent), deriving
  a smaller number of significant events from many low-level ones. Transform stages are *Event Processing
  Agents*, not aggregates.
- **Soft real-time** (vs hard/firm): a late result degrades quality but does not crash or corrupt. This is
  the license to keep aggregates, transactions, and ceremony out of the hot path; tactical DDD in the
  latency path is a known mismatch.
- **Supporting / generic subdomain** vs **core domain** (Evans): the pipeline is usually supporting/generic
  plumbing; the decisions and what consumes them are core. Different subdomains deserve different modeling
  effort by design, not by accident.
- **Domain event vs measurement / telemetry / data-point**: the discriminator for what crosses the seam. A
  raw frame or score is a measurement; a named business fact ("UtteranceTranscribed", "ThresholdExceeded")
  is a domain event. "A row was inserted" is not a domain event; "a delivery was cancelled" is.
- **Data on the inside vs data on the outside** (Helland): inside data is rich, mutable, in-process; outside
  data is immutable, slim, versioned, on the wire. A rich in-process domain event and the slim integration
  event published from it are not duplication, they are the same fact in two registers.
- **Event Modeling's Automation and Translation patterns** (Dymitruk): the grammar Event Storming lacks for
  transforms. A pure transform that reacts to data with no human and no decision is an **Automation** slice,
  not a Policy. The same step crossing a system boundary is a **Translation** slice.

**Decision rule (transform stage vs decision point):**

- A stage is **dataflow infrastructure** (a filter / processor / EPA, leave it off the domain wall) when it
  always runs, validates nothing, rejects nothing, and just produces the next representation. "Do the next
  step" is a pipeline edge, not a command, and "proceed to the next stage" is not a Policy.
- A stage is a **genuine command / decision** only when intent can be **validated, rejected, or routed**
  (e.g. "is this relevant?", "dispatch to the agent"), or when the same step is exposed across a **service
  boundary** as a request: an in-process function call is ceremony, but a `TranscribeUtterance` message sent
  to a remote worker **is** a real command. The same operation can be ceremony in-process and a legitimate
  command across a boundary.
- There are **no aggregates** in the pure pipeline: nothing holds a consistency boundary with invariants over
  entities. Stateful processors are **domain services**, not aggregates; do not tag them Aggregate by reflex.
- A node that exchanges no domain event does not belong on the wall: a VAD model handing a probability to an
  endpointer is part of that step, not a separate external system. Drop it rather than leave it floating.

Sources for this section: Helland, "Data on the Outside vs. Data on the Inside" (CIDR 2005); Luckham, *The
Power of Events* and the CEP literature; Dymitruk, eventmodeling.org (Automation/Translation); Kleppmann,
*DDIA* ch. 11 and "Event Sourcing & Stream Processing at DDD Europe"; Fowler, "What do you mean by
Event-Driven?"; de la Torre (Microsoft), "Domain Events vs Integration Events." Re-verify exact wording with
WebSearch when it must be precise.

## Tactical building blocks are mostly implied

**Value Objects and Entities do not belong on an Event Storming wall.** The wall is behavioral
(commands/events/policies/aggregates/read models/actors/external systems); tactical structure is implied,
not drawn. Modeling a Value Object (e.g. `Money`) as its own node is an extension beyond the method — fine
if deliberate, but flag it as tactical and expect it to sit apart from the flow.

## Strategic: bounded context is declared, not inferred

A bounded context is a strategic boundary. When tooling groups concepts by context, that context must be
**declared** (an explicit label), never guessed from namespace/folder structure — code layout is an
accident, not a domain fact (the same reason you don't infer behavior from method bodies). Draw each
context as a labeled boundary box; edges that cross a boundary are the integration points (ACL /
published language).

## What a code-derived (topological) view loses

A graph extracted from code captures nodes and edges but not the narrative/spatial semantics of a wall:

- **Timeline order** — events read left-to-right in the order they happen; a topological graph is not a
  timeline (horizontal position = reachability, not chronology).
- **Pivotal events** — major state transitions that divide phases/contexts; not derivable.
- **Bounded-context grouping** — emerges where the ubiquitous language changes; needs a declared boundary.
- **Swimlanes** — parallel/alternative streams; a flat graph collapses concurrency.
- **Hotspots** — friction/unknowns; human artifacts, absent from any code-derived view.

Treat a code-derived storm as an always-true scaffold and conversation starter, not a replacement for a
workshop.

## Recovering a model from code with Hindstorm

Hindstorm (nuget.org/packages/Hindstorm) recovers an Event Storming model from .NET assemblies via
attributes (`Hindstorm.Annotations`) and exports Mermaid/DOT/JSON (the `Hindstorm` package). Observations
from use (scope to the package version you have; these are from the 0.2.0 era, verify before relying):

- **Concept tags and relation attributes have different valid targets.** Concept tags (`[Aggregate]`,
  `[DomainEvent]`, `[ExternalSystem]`, `[Invariant]`, ...) allow class/struct, and `[ExternalSystem]` also
  allows interface. **Relation attributes (`[Raises]`, `[ReactsTo]`, `[Handles]`, `[Issues]`, `[Enforces]`,
  `[Updates]`) are valid only on class/struct/method, not interfaces** (you get CS0592). To state an edge
  from an interface-typed external system, put the relation on the **method** that performs it (e.g.
  `[Raises]`/`[ReactsTo]` on `TranscribeAsync`); the scanner reads method-level relations on any tagged
  concept, and it is more precise anyway.
- **The scanner uses live reflection** (`GetCustomAttribute`, `GetMethods`), so the assemblies must be
  **loaded** in the generating process with their dependencies resolvable. `Scan(IEnumerable<Assembly>)`
  takes many assemblies, so one pass yields a unified model.
- **An Aspire AppHost cannot scan the app assemblies**: Aspire project references are
  `ReferenceOutputAssembly=false`, so the app DLLs are not in the AppHost output and its types are not
  loadable there. Centralize generation in a **dedicated tool project** with real `ProjectReference`s to
  the annotated projects (deps resolve via its `deps.json`) that discovers assemblies from its own output
  directory, so onboarding a new service is one project reference and no code change. (A future
  metadata-only scan via `System.Reflection.MetadataLoadContext` would remove the loaded-deps requirement
  but needs Hindstorm to read `CustomAttributeData` instead of instantiating attributes.)
- Frame the artifact: emit a `.md` embedding the Mermaid in a fenced block (renders on GitHub; a raw
  `.mmd` does not) with a one-line note on the context's nature so readers judge it on its own terms.

Hindstorm deliberately stays **Event-Storming-pure**: the dataflow plane (transform processors, measurement
data events, the abstraction hierarchy, translation/ACL seams; see "Streaming / dataflow contexts" above) is
NOT part of its vocabulary, a scope decision made 2026-06-22. To model a streaming pipeline with the existing
stickies, represent transform stages as **external systems** or **domain services**, leave raw frames/scores
off the wall as plumbing, and promote only the abstraction-top facts to `[DomainEvent]`. The two-plane model
is a thinking tool, not a Hindstorm annotation set.

## Sources

Brandolini, *Introducing EventStorming* / "The Picture That Explains Everything"; wwerner
event-storming-cheatsheet; Open Group O-AA; IBM refarch-eda; Qlerify; Context Mapper; Avanscoperta;
plus practitioner blogs on design-level Event Storming. Re-verify specifics with WebSearch when a claim
must be exact — colors and grammar are conventions, not a standard.
