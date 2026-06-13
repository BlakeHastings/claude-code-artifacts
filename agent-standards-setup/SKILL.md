---
name: agent-standards-setup
description: >-
  How to capture and enforce coding standards and conventions for AI coding agents in a
  repository, cross-tool and not Claude-specific. Use when setting up AGENTS.md, deciding where a
  convention should live, wiring a CLAUDE.md @AGENTS.md bridge, making rules portable across Cursor,
  Copilot, Codex, Gemini CLI, Aider, and Claude Code, or enforcing conventions via linters,
  analyzers, or .editorconfig. Covers the two-layer model (deterministic enforcement plus
  natural-language context) and the routing rule for which layer a given rule belongs in.
allowed-tools: Read, Write, Edit, Bash, Glob
---
# Agent Standards Setup

How to make a repository's coding standards land reliably with AI agents (and humans), without
locking into one tool. The governing idea: **conventions live in two layers that fail differently,
and each rule belongs in exactly one of them.**

## The two-layer model

1. **Enforcement layer (deterministic).** Linters, formatters, static analyzers, and CI. An agent
   can ignore prose, but it cannot ignore a failing check. Anything a machine can verify or fix goes
   here. Applies to every agent, editor, and human identically, with no file to read.
2. **Context layer (natural language).** A markdown file the agent reads for everything a machine
   *can't* mechanically check: architecture, naming intent, design rationale, "why," do-not-touch
   lists, build/test commands. The cross-tool standard for this file is **`AGENTS.md`**.

The two are complementary, not competing. Enforcement catches the mechanical; context conveys intent.

## Routing rule: which layer does a rule belong in?

Ask: *can a linter/formatter/analyzer mechanically check or fix this?*

- **Yes** -> enforcement layer (config file + CI). Examples: formatting, import order, naming casing,
  unused usings, a specific analyzer toggle.
- **No** -> `AGENTS.md`. Examples: "commands are thin and inject services," "prefer composition for
  X," "this module owns persistence," the nuance behind a style choice.

If a rule has *both* a mechanical half and a nuance half (common), put the mechanical half in the
enforcement layer and the nuance in `AGENTS.md`, and cross-reference them. Example: a constructor
style rule disables an analyzer suggestion in `.editorconfig` *and* explains the when/why in
`AGENTS.md`.

## Layer 1: deterministic enforcement

Pick the standard formatter + linter for the language, commit its config, and run it in CI so it is
non-optional. Per-language cheat-sheet:

| Language | Format/lint/analyze | Config file |
|----------|---------------------|-------------|
| C# / .NET | Roslyn analyzers + `dotnet format` | `.editorconfig` |
| TypeScript/JS | ESLint + Prettier | `eslint.config.js`, `.prettierrc` |
| Python | Ruff (lint + format) | `pyproject.toml` / `ruff.toml` |
| Go | `gofmt` / `golangci-lint` | `.golangci.yml` |
| Rust | `rustfmt` + Clippy | `rustfmt.toml`, `clippy.toml` |

### C# / .NET specifics

`.editorconfig` is the universal home: it is consumed by Roslyn, every IDE, `dotnet format`, and CI,
so it binds humans and all agents the same way. A rule is a diagnostic severity line:

```ini
# .editorconfig
[*.cs]
# Disable the "use primary constructor" suggestion (style choice documented in AGENTS.md).
dotnet_diagnostic.IDE0290.severity = none
```

- Severities: `none` / `silent` (no nudge), `suggestion`, `warning`, `error`.
- IDE code-style rules (`IDExxxx`) are **IDE-time only by default**. To make them fail the build/CI,
  set `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` in the project. Without it, you are
  suppressing or surfacing a hint, not gating the build.
- Keep a comment on each non-obvious rule pointing at the `AGENTS.md` section that explains the why.

## Layer 2: AGENTS.md (the cross-tool context file)

`AGENTS.md` is an open standard (originated by OpenAI, Aug 2025; now under the Linux Foundation's
Agentic AI Foundation) read natively by Codex, Cursor, Copilot, Gemini CLI, Aider, Windsurf, Zed, and
more. Properties:

- Plain markdown, **no required schema** — use any headings.
- Lives at the repo **root**; large monorepos may add **nested** `AGENTS.md` files per package
  (agents read the nearest one, which takes precedence).
- Complements `README.md`, it does not replace it. README is for humans (quick start, contribution);
  `AGENTS.md` holds the agent-facing build/test/convention detail that would clutter a README.

Recommended sections: project overview, build/test commands, conventions (the prose ones), and a
note that mechanical rules are enforced in the config files. Keep it about what analyzers cannot
express; do not duplicate what the linter already guarantees.

## Claude Code bridge

Claude Code does **not** read `AGENTS.md` natively — it loads `CLAUDE.md`. Keep `AGENTS.md` as the
single source of truth and add a thin `CLAUDE.md` that imports it:

```markdown
Project conventions for all agents are in AGENTS.md. Claude reads them via the import below.

@AGENTS.md
```

- **Use the `@AGENTS.md` import, not a symlink, on Windows** — symlinks require Admin / Developer
  Mode and break on cross-platform teams. The import works everywhere.
- The import is resolved at session start; a freshly added `CLAUDE.md` takes effect next session.
- Other tools that want a tool-specific filename (e.g. `.cursor/rules/`) can symlink or import the
  same `AGENTS.md` so there is still one source of truth.

## Setup checklist

1. Add/verify the language's formatter + linter config (`.editorconfig` for .NET) and wire it into CI.
2. Create `AGENTS.md` at the repo root: project overview, build/test commands, and the prose
   conventions only.
3. Add a one-line `CLAUDE.md` containing `@AGENTS.md` (import, not symlink).
4. For each convention, route it: mechanical -> config + CI; nuance -> `AGENTS.md`; cross-reference
   when split.
5. Verify the enforcement layer actually runs (build/CI green; `dotnet format --verify-no-changes`
   or equivalent).

## Sources

Two-layer model / enforcement vs docs:
- Stack Overflow Blog, "Building shared coding guidelines for AI (and people too)" — https://stackoverflow.blog/2026/03/26/coding-guidelines-for-ai-agents-and-people-too/
- dev.to, "Making AI Code Consistent with Linters" — https://dev.to/fhaponenka/making-ai-code-consistent-with-linters-27pl
- Aviator, "Standardizing AI Coding Practices Across Your Engineering Org" — https://www.aviator.co/blog/standardizing-ai-coding-practices-across-your-engineering-org/

AGENTS.md standard:
- Official site — https://agents.md/
- Linux Foundation / Agentic AI Foundation announcement — https://www.linuxfoundation.org/press/linux-foundation-announces-the-formation-of-the-agentic-ai-foundation
- Cross-tool comparison (AGENTS.md vs CLAUDE.md vs Cursor Rules vs Copilot) — https://codersera.com/blog/agents-md-vs-claude-md-vs-cursor-rules-comparison-2026/

Claude Code bridge (import vs symlink, Windows):
- "Does Claude Code read AGENTS.md?" import/symlink gist — https://gist.github.com/yurukusa/d36197848911f025add142abefcde685
- CLAUDE.md to AGENTS.md migration guide — https://solmaz.io/log/2025/09/08/claude-md-agents-md-migration-guide/

C# / .NET enforcement:
- Microsoft Learn, Code-style rule options (.editorconfig) — https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-style-rule-options
- Microsoft Learn, IDE0290 (use primary constructor) — https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0290
