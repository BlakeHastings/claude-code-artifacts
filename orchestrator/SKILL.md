---
name: orchestrator
description: >-
  When and how to delegate work to subagents inside a coding harness, and — just
  as important — when to NOT delegate. Covers the delegation decision, model-tier
  selection, prompt contracts for briefing, response contracts for output,
  parallelism, context-pressure management, and verification via back-pressure.
  Invoke when planning a non-trivial multi-step task, before spawning a subagent,
  or when the main thread's context is starting to feel crowded.
allowed-tools: Read, Grep, Glob
---

# Orchestrator

Guidance for being the primary agent inside a coding harness (Claude Code or similar) — specifically the decisions around delegating to subagents. Grounded in published research and practitioner write-ups, not one-off anecdote.

This skill is about *orchestrating inside* a harness. Rules from multi-agent *product* write-ups (systems with custom harnesses, LLM-as-judge evaluators, and bespoke agent hierarchies) are directional, not prescriptive — their harness is not this harness.

## 1. The delegation decision — default is NOT delegating

The guiding rule, from practitioner consensus:

> If a split doesn't reduce context pressure, it's probably not a useful split.

Anthropic's own caveat on multi-agent orchestration: most coding tasks aren't well-suited to it, and multi-agent systems burn roughly **15× the tokens** of a single-agent session. Reserve delegation for work that's high-value, genuinely parallelizable, or under real context pressure.

Do it yourself when:
- The target is known — `Read` a specific path.
- A single `Grep` answers the question.
- The change fits in one file.
- The work is inherently sequential.

## 2. Positive signals for delegating

| Signal | Agent |
|---|---|
| Codebase exploration spanning multiple files or unknown scope | `Explore` |
| Non-trivial implementation needing approach alignment before code | `Plan` |
| Bulk, spec-driven mechanical edits (renames, boilerplate, pattern-driven rewrites) | `general-purpose` on haiku |
| Research/reading that would bloat main-thread context with content not reused verbatim | `Explore` or `general-purpose` |

## 3. Scaling heuristics

A ceiling, not a target. If the task feels bigger than the row it fits, the scope is probably wrong, not the agent count.

| Task shape | Typical agent count | Tool calls per agent |
|---|---|---|
| Fact-finding / single lookup | 0–1 | 3–10 |
| Comparison or 2–3 related questions | 2–4 | 10–15 |
| Large parallelizable research | many, explicitly divided | — |
| Coding work | usually 0–2, mostly sequential | — |

## 4. Picking `subagent_type`

- `Explore` — read-only codebase research. Specify thoroughness level (quick / medium / very thorough).
- `Plan` — design an approach before any code ships; feed it known constraints and the problem statement.
- `general-purpose` — writes, edits, or research outside `Explore`'s read-only fit.
- `claude-code-guide` — questions about Claude Code / Agent SDK / Anthropic API specifically.

Prefer continuing an existing agent via `SendMessage` over spawning a new one when context continuity matters.

## 5. Model tier

- **Main thread** — synthesis, decisions, reviewing subagent output, user alignment. The expensive tier earns its keep here.
- **Haiku via `general-purpose`** — narrow, spec-driven work where verification is cheap (renames, boilerplate, mechanical rewrites against a complete spec).
- **Sonnet** — mid-complexity exploration or edits that need judgment but not top-tier reasoning.

Model-tier separation (cheap subagent, expensive orchestrator) reliably cuts bills without degrading well-scoped subagent work.

## 6. Prompt contract — brief like a colleague who just walked in

The agent has zero conversation context. The prompt must be self-contained.

Include:
- **Goal** — what the work accomplishes, in one sentence.
- **Background** — what's been tried, what's been ruled out.
- **Exact targets** — file paths with line numbers, exact change or exact question.
- **Output shape** — what the return should look like.
- **Length cap** — "under 200 words" or equivalent.

Pass artifacts *by path*, not in the prompt body. If a plan file exists, reference its path; don't paste the plan into the prompt.

Say explicitly: *"write code"* vs *"research and report"*. The agent can't infer user intent.

**Never delegate understanding.** Don't write "based on your findings, fix the bug" or "based on the research, implement it" — that pushes synthesis onto the agent. Prompts that name the exact file:line and exact change prove the understanding is already on the main thread. Synthesis stays with the orchestrator.

Vague briefs are a documented failure mode: subagents given instructions like "research the semiconductor shortage" duplicate each other's searches or misinterpret the task. Detailed task decomposition prevents this.

## 7. Response contract — constrain what comes back

- Ask for 3–7 bullets with evidence references (file:line, URL).
- Require a state flag: **done / partial / blocked** (+ why if blocked).
- Never ask for "full file contents" or "paste everything" — that defeats the split and wastes the token budget on content the main thread can retrieve directly.
- Target summaries at ~1,000–2,000 tokens. For larger output, have the agent write to a file and return the path — **file system as memory**.

## 8. Parallelism

- Independent reads, greps, or agents → single message, multiple tool blocks.
- Dependent calls → sequential; don't guess values for dependent params.
- Batch size matters: 10 parallel file reads × 500 lines = 5,000 new lines on the next turn. Parallelize for wall-clock win, but keep each batch integrable in one main-thread turn.

## 9. Context pressure — techniques that don't require subagents

Subagents are one context-pressure tool; not the first one.

- **Just-in-time retrieval** — keep lightweight references (paths, queries, links) on the main thread; load data on demand, don't pre-load.
- **Clear tool results** from conversation history once the relevant content is saved elsewhere.
- **Structured note-taking** for long-horizon work — maintain a `NOTES.md` the main thread updates as it goes.
- **Context isolation > bigger context window.** Bigger windows just make bigger haystacks — isolation via subagents is what fixes context rot.

## 10. Verification via back-pressure

Verification mechanisms are one of the highest-leverage investments an orchestrator can make; agent success rates correlate strongly with the agent's ability to check its own work.

- **Silent on success, verbose only on failure.** A check that prints 4,000 lines of passing test output floods context and destroys signal.
- **Run test *subsets*, not full suites.** Target the tests that cover the change.
- **Prefer context-efficient checks** — typecheck, build, focused tests — over broad verification.
- **Spot-check delegated edits.** A subagent's summary describes *intent*, not what shipped. When the code touches shared or user-owned paths (home directory, config dirs, `/etc`, databases), `Read` the diff on the main thread before executing it.

## 11. Scope discipline

- User says "surgical" / "minimal" → take it literally. Don't turn a fix into a refactor.
- Plan mode before non-trivial code. Hidden requirements tend to surface during planning, not during implementation.
- Three similar lines < premature abstraction.

## 12. Common workflows

- **Big refactor.** `Plan` drafts approach → main thread reviews + aligns with user → haiku agents do per-file edits against a complete spec → main thread reads each changed file → targeted tests → report. Many refactors are faster on the main thread with parallel `Read`s than through a subagent chain.
- **Unknown codebase region.** `Explore` first. Don't grep blindly on main thread.
- **Docs-only change.** Main thread. Delegation overhead exceeds the work.
- **Multi-file rename.** Haiku with exact search/replace; verify by grepping for the old name and confirming it's gone.

## 13. Anti-patterns

- Spawning a subagent for what a single `Grep` would answer.
- Duplicating work — agent researches X, main thread also researches X.
- "Based on your findings, implement it" — delegates judgment, not just legwork.
- Asking a subagent for "full contents" or "paste everything you found."
- Trusting "tests pass" without spot-checking the tests themselves.
- **Role-based splits** ("frontend engineer", "backend engineer") — explicitly tested and shown not to work. Organize agents by task shape, not job title.
- Spawning more agents than the scaling table suggests "just to be thorough."
- Installing every MCP or tool preemptively — crowds the tool set and creates ambiguous routing decisions.
- Auto-generating agent config files — documented to hurt performance ~20% and cost more.
- Micro-managing tool access across many subagents — causes tool thrash.
- Writing to user-owned paths in tests without backup/restore (`beforeAll`/`afterAll`) or redirecting via env var.

## 14. When NOT to multi-agent — the explicit veto list

- Shared-context problems — every agent would need the same full context anyway.
- Tasks with heavy inter-agent dependencies — real-time coordination between agents is not yet reliable.
- Low-value tasks where the token premium isn't justified.
- Most straightforward coding tasks (Anthropic's own position).

## What this skill does NOT cover

- Claude Code setup (hooks, slash commands, MCP plumbing) → `claude-code-guide`.
- Designing agent SDKs or building harnesses → product engineering, not orchestrating inside one.
- Memory system usage → covered by the main system prompt's auto-memory section.

## Sources

Transferable rules for orchestrating inside a coding harness:

- [Martin Fowler — Context Engineering for Coding Agents](https://martinfowler.com/articles/exploring-gen-ai/context-engineering-coding-agents.html)
- [Anthropic — Effective Context Engineering for AI Agents](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents)
- [HumanLayer — Skill Issue: Harness Engineering for Coding Agents](https://www.humanlayer.dev/blog/skill-issue-harness-engineering-for-coding-agents)
- [huuhka.net — Primary vs Subagents in LLM harnesses](https://www.huuhka.net/primary-vs-subagents-in-llm-harnesses/)
- [claudekit — Claude Code Subagents: Common Mistakes & Best Practices](https://claudekit.cc/blog/vc-04-subagents-from-basic-to-deep-dive-i-misunderstood)
- [claudefa.st — Sub-Agents: Parallel vs Sequential Patterns](https://claudefa.st/blog/guide/agents/sub-agent-best-practices)
- [Parallel.ai — What is an Agent Harness](https://parallel.ai/articles/what-is-an-agent-harness)
- [dev.to — Parallel Tool Calling in LLM Agents](https://dev.to/rahulxsingh/parallel-tool-calling-in-llm-agents-complete-guide-with-code-examples-3ilo)

Directional context only (research-product harness, not this harness):

- [Anthropic — How we built our multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system)
