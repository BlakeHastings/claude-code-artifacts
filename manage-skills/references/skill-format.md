# Skill Format Quick Reference

> Full specification: https://code.claude.com/docs/en/skills.md
> Agent Skills spec: https://agentskills.io/specification

## SKILL.md Structure

Every skill lives in a directory containing a `SKILL.md` file with YAML frontmatter + markdown body.

```
skill-name/
└── SKILL.md
```

## Frontmatter Fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `name` | string | directory name | Skill identifier; must match directory name |
| `description` | string | **required** | When this skill should be invoked; keyword-rich for matching |
| `argument-hint` | string | — | Placeholder shown in autocomplete (e.g., `"[query]"`) |
| `allowed-tools` | string[] | all | Tools the skill may use (whitelist) |
| `disable-model-invocation` | bool | `false` | If `true`, skill can only be invoked by user (`/name`), not by model |
| `context` | string | — | Set to `fork` to run in an isolated context (no conversation history) |
| `intercept-tool` | string | — | Tool name this skill intercepts before execution |
| `intercept-after-tool` | string | — | Tool name this skill intercepts after execution |

## String Substitution Variables

| Variable | Expands To |
|----------|------------|
| `$ARGUMENTS` | Everything after `/skill-name ` |
| `$1`, `$2`, ... `$N` | Positional arguments (space-separated) |
| `${CLAUDE_SESSION_ID}` | Current session identifier |

## Dynamic Context Injection

Use `` !`command` `` on its own line to inject command output into the prompt at invocation time:

```markdown
Current branch:
!`git branch --show-current`
```

## Name Validation Rules

- Lowercase letters, digits, and hyphens only
- 1-64 characters
- Must start and end with a letter or digit
- No consecutive hyphens (`--`)
- Must match the containing directory name

## Directory Conventions

```
skill-name/
├── SKILL.md              # Required entry point
├── references/           # Supporting reference material
│   └── *.md
└── scripts/              # Executable scripts (.cs, .sh, etc.)
    └── *.cs
```

- Keep SKILL.md under 500 lines
- Use `references/` for content SKILL.md reads via the Read tool
- Use `scripts/` for executable scripts invoked via Bash
