# Skill Best Practices

> Full docs: https://code.claude.com/docs/en/skills.md

## Core Principles

1. **One skill = one purpose.** A skill should do one thing well. If it needs a conjunction ("and") to describe, consider splitting it.

2. **Meaningful names.** The name is how users invoke the skill (`/name`). Make it short, memorable, and action-oriented.

3. **Keyword-rich descriptions.** The `description` field drives automatic invocation. Include synonyms and use-case phrases so Claude matches the skill to relevant requests.

4. **Keep SKILL.md under 500 lines.** Move reference material to `references/` files and read them on demand. The main file should be orchestration logic, not content.

5. **Minimal `allowed-tools`.** Only grant tools the skill actually uses. This communicates intent and limits blast radius.

6. **Deterministic behavior via scripts.** When a skill needs programmatic logic — API calls, data transformations, validation, file processing — use .NET single-file scripts (`scripts/*.cs`) instead of relying on Claude to perform the logic ad-hoc. This makes operations reproducible and testable. See `references/dotnet-scripts.md`.

7. **Never hardcode secrets.** API keys, tokens, and credentials must use `dotnet user-secrets` for development. Never put secrets in SKILL.md, reference files, or scripts. See `references/dotnet-scripts.md` for implementation details.

## Common Patterns

| Pattern | Use When | Key Traits |
|---------|----------|------------|
| **Simple Reference** | Conventions, style guides | SKILL.md only, no tools needed |
| **User-Invoked Task** | Deployments, workflows | `disable-model-invocation: true` |
| **Subagent Research** | Analysis, exploration | `context: fork` for isolation |
| **Scripted Automation** | Deterministic operations | `.cs` scripts in `scripts/` |
| **Complex Multi-File** | Large orchestrations | `references/` + `scripts/` |
| **Dynamic Context** | Live data injection | `` !`command` `` syntax |

## Anti-Patterns

- **Kitchen-sink skills** — Doing too many unrelated things in one skill. Split them.
- **Inline reference bloat** — Embedding large reference content in SKILL.md. Move to `references/` and read on demand.
- **Vague descriptions** — "A helpful skill" tells Claude nothing. Be specific about triggers.
- **Overly broad tool access** — Omitting `allowed-tools` grants everything. Be explicit.
- **`context: fork` on interactive skills** — Fork loses conversation history. Only use for stateless/research tasks.
- **Hardcoded secrets** — API keys in files. Always use `dotnet user-secrets`.
- **Ad-hoc logic that should be scripted** — If Claude is performing the same multi-step data transformation every time, put it in a script.

## When to Split Into Multiple Files

Split when:
- SKILL.md exceeds ~200 lines of reference content → move to `references/`
- You have reusable logic → extract to `scripts/`
- Multiple operations share setup → common reference files
- Content changes independently → separate files for independent update cycles

Keep together when:
- The skill is simple and self-contained (<100 lines total)
- There's only one operation with no reference material
