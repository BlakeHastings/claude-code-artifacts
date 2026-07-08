# Sub-agent brief template

Copy this, fill the `{{...}}` placeholders from the contract packet, and pass it as the prompt to one
`Agent` call (subagent_type `general-purpose`). Spawn one per interface and run them in parallel (one
message, multiple `Agent` calls). A fresh agent has no conversation history, which is exactly the
implementation-blind property you want.

Keep the agents alive: when one returns BLOCK questions, answer them and continue **that** agent with
`SendMessage` (its `agentId` is in the tool result) so it keeps its context.

---

```
You are writing black-box CONTRACT unit tests for a C# interface, based ONLY on the interface and the
supporting types/doc comments given below. Derive every expected behavior from the documented contract,
NOT from reading the implementation.

# Repository
- Project: {{project name}} (.NET {{version}}, xUnit). Work in: {{absolute worktree path}}
- Test project: {{test project path}} (namespace `{{test namespace}}`, `using Xunit;`). The production
  assembly exposes internals to this test project via InternalsVisibleTo, so internal types are accessible.
- Test method naming style is {{e.g. snake_case: Method_State_Expected}}. Match it. Use `sealed class`,
  `[Fact]`/`[Theory]`.

# HARD CONSTRAINTS (the point of this exercise)
- Do NOT open, read, grep, or inspect: {{concrete implementation file(s)}} and {{existing tests for this unit}}.
  Reading them defeats the purpose. Reason from the contract below instead.
- Do NOT run `dotnet build` or `dotnet test`. Sibling agents are writing into this same project
  concurrently; the integrator builds and runs everything afterward.
- Create exactly ONE file. Do not modify any other file (no csproj edits, no new packages).

# The contract under test
{{interface verbatim, WITH its doc comments}}

# Supporting types (rely on these verbatim; their doc comments ARE the contract for failure modes)
{{every record/enum/delegate the interface references, with doc comments — extracted so you never need
the implementation file}}

# System under test
Construct the real implementation through the interface (it is internal with a public ctor, visible here):
{{e.g. ISomething sut = new Namespace.Concrete();}}

# Environmental facts you may rely on (so you do not have to discover them)
{{e.g. the registry contains exactly one harness today and it supports X, so the `Unsupported` failure
mode is NOT reachable — note it in your report rather than trying to force it}}

# Test isolation (only if the unit touches disk/env/global state)
{{the temp-dir + env save/restore scaffold to use, e.g. redirect ENV_VAR to a per-test temp dir in the
ctor and restore + delete in Dispose}}

# How to work (two-phase, ask before guessing)
1. Build an ASSUMPTION LEDGER, one row per contract gap:
     assumption | basis | impact-if-wrong | PROCEED or BLOCK
   - PROCEED: low impact / strongly implied by the contract. Write the test now; tag the spot `// ASSUMES:`.
   - BLOCK: the answer changes which behavior is CORRECT (e.g. empty input throws vs returns empty). Do NOT
     write that test; list it as a clarifying question.
2. If you have any BLOCK questions: return your TEST PLAN + the LEDGER + the batched questions, and write
   ONLY the unambiguous (PROCEED) tests. Wait for answers (you will be messaged) before writing the rest.
   If you have no BLOCK questions: write the full suite and return the file + ledger.

# What to cover
Derive cases from the contract: the happy path, each documented failure/edge case, null/empty/boundary
inputs, idempotency and "no-op" guarantees, and independence between separate keys/instances. For any
behavior you CANNOT reach black-box (needs fixtures/state the contract does not expose), do NOT read the
implementation to fake it — list it in your report as needing a fixture or a contract seam.

# Deliverable
1. Write the file: {{target test file path}}
2. Final message (data for the integrator, not a user summary): the test cases you wrote (one line each);
   the assumption ledger; any BLOCK questions; any behaviors not testable black-box and why; any interface
   doc ambiguities you had to guess about.
```

---

## Notes for the orchestrator

- **The ledger is the high-value output.** Even an all-PROCEED run should return it, so you can audit every
  guess and see which `// ASSUMES:` tags mark soft spots.
- **Batch human escalation.** Collect BLOCK questions across all agents, dedupe, and ask the human once.
- **Resolve at the source.** A recurring BLOCK question is the contract telling you to tighten it: prefer a
  type/signature change, then a single precise doc sentence. Do not answer it with summary filler.
- **Do not pre-empt the find.** Give the agent environmental facts (what's registered, what's reachable) but
  not the implementation's logic — let the tests, not your briefing, decide what passes.
