---
name: skill-finder
description: Discover and install Claude Agent Skills (SKILL.md packages) from across the ecosystem by querying live registries. Use when the user wants to find a skill for a task, browse what skills exist for a topic, check whether someone has already built a skill, refresh the skill catalog, or get install commands for a skill. Reads the official Anthropic marketplaces plus community registries (SkillsMP REST API, GuildSkills cross-agent catalog) and ranks by trust and popularity.
metadata:
  author: blake
  version: "1.0"
---

# Skill Finder

Find Agent Skills (the SKILL.md package format) anywhere in the ecosystem and return ranked, install-ready results. This skill compounds: it reads from registries that already index tens of thousands of skills, so it stays current without me re-crawling GitHub.

## When to use

- "Is there a skill for X?" / "find me a skill that does Y"
- "What skills exist for <topic>?" (pdf, security, react, testing, marketing...)
- "Refresh / re-run skill discovery"
- The user wants an install command for a specific skill

## How to find skills

Run the reader. It queries live registries, dedupes, and ranks (official first, then by stars / security grade):

```bash
node "C:/Users/Blake/.claude/skills/skill-finder/scripts/find-skills.mjs" "<query>" --limit 25
```

Flags:
- `--limit N` — how many results (default 25)
- `--sources skillsmp,official,guild` — which registries (default `skillsmp,official`; add `guild` for the heavy 75MB cross-agent catalog with security grades)
- `--json` — machine-readable output

Examples:
- `node .../find-skills.mjs "security audit" --limit 15`
- `node .../find-skills.mjs "" --sources official` — list the full official catalog
- `node .../find-skills.mjs "kubernetes" --sources skillsmp,official,guild` — widest net

If `node` is unavailable, fall back to curl: see `references/registries.md` for the raw endpoints and example requests.

## How to install a found skill

Two install paths dominate. Pick based on where the skill lives:

- **Vercel `skills` CLI (works for any GitHub repo with a SKILL.md):**
  `npx skills add <github-url-or-owner/repo> --skill <name>`
- **Claude Code plugin marketplace (for repos shipping `.claude-plugin/marketplace.json`):**
  `/plugin marketplace add <owner/repo>` then `/plugin install <plugin>@<marketplace>`
- **Manual:** clone the skill folder into `~/.claude/skills/<name>/` (user scope) or `<project>/.claude/skills/<name>/` (project scope), then `/reload-skills` or restart.

Always tell the user the source repo and that skills are executable instructions — only install from sources they trust.

## Ongoing discovery (feeds)

To catch newly published skills over time, poll these (see `references/registries.md` for the full list):
- `https://github.com/anthropics/skills/commits/main.atom` — new/updated official skills
- `https://skillsmp.com/api/skills?q=&sortBy=recent` — newest community skills
- `https://skills.sh` — install-telemetry leaderboard (true popularity)

## Reference

- `references/registries.md` — every registry, its read path (API/JSON/raw), schema, and example requests.
- `references/catalog.md` — a curated, ranked snapshot of the most useful skills as of 2026-06-14, organized by category.
