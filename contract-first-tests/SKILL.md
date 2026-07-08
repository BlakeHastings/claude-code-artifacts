---
name: contract-first-tests
description: >-
  Generate unit tests for an interface or service by delegating to
  implementation-blind sub-agents that derive black-box tests from the documented
  contract, with a clarifying-question and assumption-ledger loop that resolves
  spec ambiguity instead of guessing. Use when writing or expanding unit tests for
  C#/.NET interfaces, services, or DI seams (xUnit), when you want tests that do not
  share assumptions with the implementation, or when adding a test suite after
  introducing interfaces. Covers the sub-agent briefing, the question/answer loop,
  integration (build, run, serialize global-state tests for xUnit parallelism), and
  the common failure modes of LLM-written tests (tautological, vacuous, hallucinated APIs).
allowed-tools: Agent, SendMessage, Read, Write, Edit, Bash, Grep, Glob
---
# Contract-First Unit Tests

Write unit tests against a documented **contract** (an interface and its doc comments), authored by
sub-agents that never see the implementation. Separating the test author from the implementer breaks
the shared-wrong-assumption loop (the "test oracle" problem): if the tests are derived from the same
code they verify, a bug in the code becomes a bug baked into the test, and the suite passes on wrong
behavior. An implementation-blind author encodes an *independent* reading of the spec, so disagreements
surface as failing tests instead of agreeing on the wrong answer.

This pays a second dividend: where the author cannot tell what the correct behavior is, that is not a
blocked test, it is a **discovered spec gap**. Surfacing under-specified behavior is a primary output of
this workflow, not a nuisance.

## When to use

- Writing or expanding unit tests for a C#/.NET interface, service, or DI seam.
- Right after introducing interfaces (e.g. extracting `IFoo` from `Foo` for testability).
- Any time you want black-box tests that do not inherit the implementation's assumptions.

**When not to use:** trivial pure functions (just write the test inline); pinning the behavior of legacy
code that has no spec (that is *characterization* testing, a different mode, see the cheatsheet); UI or
end-to-end flows (integration tests, not this).

## Principles (the contract is the source of truth)

1. **Author from the contract, not the code.** The test author sees only the interface, its doc comments,
   and the signatures of the types it references. It must not open the implementation or the existing
   tests for the same unit. This is the whole point; do not weaken it for convenience.
2. **Assert observable behavior through the public surface.** Inputs to outputs and observable effects.
   Never assert private state, call order, or internal structure.
3. **Ambiguity is an output, not a guess.** When the contract under-specifies behavior, surface it (see
   the question loop). Resolve it *at the source*, never by padding a `<summary>` with vague prose.
4. **Name the limits honestly.** If a behavior needs state or fixtures the contract cannot reach, say so:
   propose a seam, or hand it off as an explicitly labeled white-box/integration test. Do not force it,
   and do not read the implementation to fake your way there.
5. **Hand the contract to the agent inline.** Hallucinated/fabricated APIs are the single largest cause of
   LLM-test compile failures. Pasting the exact signatures into the brief removes the need to guess them.

## Workflow

### 0. Orchestrator prep (you)

- Pick the unit(s). **One sub-agent per interface** (or per cohesive unit). Run them in parallel.
- Assemble a **contract packet** for each agent and paste it into the brief verbatim:
  - the interface, with its doc comments;
  - the signature + doc comments of every type it references (records, enums, delegates), **even if they
    live in the implementation file** — extract them so the agent never needs that file;
  - the **SUT construction recipe**: the concrete type name and how to build it (e.g. parameterless ctor),
    referenced through the interface;
  - the **isolation recipe** if the unit touches disk/env/global state (temp dir, env save/restore);
  - **project test conventions**: framework, test namespace, naming style, `InternalsVisibleTo`.
- Write the **off-limits list**: the concrete implementation file(s) and any existing tests for the same
  unit. Use `references/subagent-brief-template.md` to fill this in.

### 1. Plan + interrogate (each sub-agent, no code if blocked)

The agent reads only the contract packet, then builds an **assumption ledger** — one row per contract gap:

```
assumption | basis | impact if wrong | PROCEED or BLOCK
```

- **PROCEED**: low impact, or strongly implied by the contract/conventions. Write the test now and tag the
  spot with an inline `// ASSUMES:` comment.
- **BLOCK**: the answer changes which behavior is *correct* (e.g. does empty input throw or return empty?).
  Do **not** write that test. List it as a batched clarifying question.

If there are BLOCK questions, the agent returns its **test plan + ledger + questions** and writes only the
unambiguous (PROCEED) tests. If there are none, it writes the full suite and returns the file + ledger.

### 2. Resolve questions (you, then the human)

For each BLOCK question, resolve it by exactly one of:

- **Answer from domain knowledge** you already hold, and feed it back.
- **Escalate to the human** — a tight, batched list of only the questions that materially change the tests.
- **Fix the contract at the source** when it is genuinely under-specified. Prefer, in order: a
  **type/signature change** that makes the contract self-evident (e.g. a precise key type instead of a bare
  string) > a **single precise doc sentence** stating the guarantee > nothing. Never resolve ambiguity by
  adding hand-wavy filler to a summary. The question is a signal that the contract, not the test, needs work.

Feed answers back by continuing the **same** sub-agent with `SendMessage` (use its `agentId`) so it keeps
its context, rather than re-briefing a fresh one.

### 3. Write the rest

The agent finalizes the blocked tests from the answers. Sub-agents must **not** run `dotnet build`/`test`
— parallel siblings race on the same project. They return the finished file plus the resolved ledger.
xUnit mechanics (naming, AAA, fakes, fixtures) are in `references/csharp-xunit-cheatsheet.md`.

### 4. Integrate (you)

- **Build once. Run the whole suite twice.** A green-then-green run rules out a parallelism race; a flaky
  pass/fail across the two runs means shared state (see next).
- **Serialize global-state tests.** If a new test class mutates process-global state (an environment
  variable, current directory, a static field), xUnit's per-class parallelism will race it against any
  other class touching the same state. Put them in one shared `[Collection]` backed by
  `[CollectionDefinition(DisableParallelization = true)]`. This is the failure this workflow most often
  surfaces — expect it.
- **Triage failures honestly.** A red contract test is either a real bug *or* a wrong assumption. Re-read
  the contract to decide. Do **not** weaken an assertion just to get green; if the contract is right and
  the code is wrong, you found a bug.
- **Optional quality gate:** mutation testing (Stryker.NET). Coverage lies — a high-coverage suite can kill
  almost no mutants. Mutation score catches vacuous assertions empirically.

## Anti-patterns to reject (LLM test smells)

- **Tautological / behavior-pinning** tests that assert whatever the code happens to do. The blind author
  guards against this; do not undo it by leaking the implementation into the brief.
- **Vacuous assertions** — the test calls the method and asserts nothing meaningful (or only "did not throw").
- **Hallucinated APIs** — fabricated members/types. Mitigated by the inline contract packet; if an agent
  invents an API, the packet was incomplete.
- **Over-mocking** — asserting interactions/call order, or mocking everything so you test the mocks. Prefer
  fakes; assert observable results.
- **Doc-fluff resolution** — answering a real ambiguity by padding `<summary>`. Fix the contract precisely
  or record a labeled `// ASSUMES:` instead.
- **Silent paid dependency** — reaching for FluentAssertions (v8+ needs a paid commercial license). Use xUnit
  `Assert`, or a free Apache-2.0 option (AwesomeAssertions / Shouldly). See the cheatsheet's assertion-library note.

## Reference files

- `references/subagent-brief-template.md` — fill-in-the-blanks brief for one test-writing sub-agent
  (contract packet, off-limits list, SUT + isolation recipes, the two-phase report contract).
- `references/csharp-xunit-cheatsheet.md` — xUnit naming, AAA, fakes vs mocks, lifecycle/fixtures, the
  parallelism + global-state serialization fix, the abstract/contract-test pattern, with sources.
