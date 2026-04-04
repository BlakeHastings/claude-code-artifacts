# claude-code-artifacts

Shareable Claude Code configuration — skills, hooks, and settings.

## Structure

```
skills/       # Custom slash commands (SKILL.md entry point per skill)
hooks/        # Shell scripts triggered by Claude Code hook events
settings.json # Reference configuration (permissions, model, tools)
```

## Installation

Clone into your `~/.claude/` folder:

```bash
git clone https://github.com/BlakeHastings/claude-code-artifacts.git ~/.claude/artifacts
```

Then symlink or copy the pieces you want, or clone directly as the skills folder:

```bash
git clone https://github.com/BlakeHastings/claude-code-artifacts.git ~/.claude/skills
```

## Skills

| Skill | Description |
|-------|-------------|
| `kroger-api` | Interact with the Kroger Public API (products, locations, cart, identity) |
| `manage-skills` | Create, edit, list, delete, and validate Claude Code skills |

## Hooks

Hooks are shell scripts invoked by Claude Code at lifecycle events (`PreToolUse`, `PostToolUse`, `Notification`, `Stop`). See the `hooks/` directory and [Claude Code docs](https://docs.anthropic.com/en/docs/claude-code/hooks) for details.
