# Agent Skill Registries — read paths

Every source the finder can read, with verified endpoints (checked 2026-06-14). All "no-auth" endpoints confirmed returning live data.

## Programmatic (machine-readable) — best for the reader

### SkillsMP — searchable REST, no auth (PRIMARY)
- Search: `https://skillsmp.com/api/skills?q=<term>&limit=<n>`
- Returns: `{ skills: [{ id, name, author, description, githubUrl, stars, forks, updatedAt, path, branch }], pagination }`
- `limit` slice capped ~1200; `totalAll` reports the full crawl (~1.7M, marketing figure). GitHub-sourced, so `stars` is a real signal.
- llms.txt: `https://skillsmp.com/llms.txt`
- Example: `curl -s "https://skillsmp.com/api/skills?q=pdf&limit=10"`

### GuildSkills — cross-agent catalog, no auth, schema-rich (BULK)
- Full catalog: `https://guildskills.com/data/guildskills.json` — **~75MB**, ~115k skills. Heavy; opt-in only.
- Stats: `https://guildskills.com/data/stats.json` — `{ total, categories, contributors, authors_total, updated }`
- llms.txt: `https://guildskills.com/llms.txt`
- Record: `{ slug, name, description, category, subcategory, tags[], featured, lang, author, license, daily_eligible, agent_compat[], source_url, also_on_claudskills, security_grade, use_cases[] }`
- `security_grade` (A/B/C...) and `agent_compat` (claude-code, codex, cursor, gemini-cli, hermes, opencode) are unique signals here.
- NOTE: the lighter `data/skills.json` mirror that some docs mention 404s; only `data/guildskills.json` exists.

### Official Anthropic marketplaces — raw GitHub JSON, no auth
- `https://raw.githubusercontent.com/anthropics/skills/main/.claude-plugin/marketplace.json` (the 3 first-party plugins: document-skills, example-skills, claude-api)
- `https://raw.githubusercontent.com/anthropics/claude-plugins-official/main/.claude-plugin/marketplace.json` (curated vendor plugins)
- Schema: `{ name, owner, metadata, plugins: [{ name, description, source, skills: [paths] }] }`
- Marketplace schema doc: `https://anthropic.com/claude-code/marketplace.schema.json`

### SkillsDirectory — REST, **API key required**
- Base `https://www.skillsdirectory.com/api/v1`, `GET /skills`, `/skills/:slug`, `/skills/search`
- Needs `Authorization: Bearer sk_live_...`. Free tier 100 req/day. Not wired into the reader (auth-gated).

## Popularity / ranking signals (web, not pure API)

- **skills.sh** (Vercel) — `https://skills.sh` — leaderboard ranked by real install telemetry. The most credible popularity signal. Top installs (2026-06): find-skills 2.0M, frontend-design 544K, vercel-react-best-practices 476K, agent-browser 449K, microsoft-foundry 390K, web-design-guidelines 390K, remotion-best-practices 370K, grill-me 315K, skill-creator 269K.
- **claudemarketplaces.com** — daily-refreshed directory of skills/plugins/MCP; publishes "This Week in Claude" Mondays.

## Curated awesome-lists (GitHub, watch for new entries)

| Repo | Notes |
|---|---|
| VoltAgent/awesome-agent-skills | Most comprehensive/fresh, 1,400+ skills from 50+ orgs |
| ComposioHQ/awesome-claude-skills | Most-starred; integration-heavy |
| travisvn/awesome-claude-skills | Best hand-picked community skills w/ attribution |
| BehiSecc/awesome-claude-skills | Includes SkillCheck-Free validator |
| hesreallyhim/awesome-claude-code | Broad: skills + hooks + slash-commands |
| heilcheng/awesome-agent-skills | Tutorials + directory index |

## RSS / Atom feeds (poll for new skills over time)

- `https://github.com/anthropics/skills/commits/main.atom` — new/updated official skills (CONFIRMED working)
- `https://github.com/anthropics/claude-code/commits/main/CHANGELOG.md.atom` — Claude Code changelog (skills/plugin features)
- `https://dev.to/feed/tag/claude` — dev.to Claude tag
- ClaudeLog feed (`https://claudelog.com/rss.xml`) — site 403s automated fetch; verify in browser
- Releasebot Anthropic/Claude Code — aggregates 13 sources, offers RSS

## Discovery / installer tools (the meta layer)

- **vercel-labs `find-skills`** skill — in-agent skill discovery (`npx skills add vercel-labs/agent-skills --skill find-skills`). The #1 most-installed skill.
- **`npx skills` CLI** (skills.sh) — de-facto package manager; installs any GitHub repo with a SKILL.md across 20+ agents.
- **openskills** (`npm i -g openskills`) — universal SKILL.md installer across Claude Code/Cursor/Codex/Aider.
- **Skill_Seekers** (yusufkaraaslan) — converts docs sites/repos/PDFs into Claude skills.
- **skill-creator** (anthropics/skills) — scaffold + eval new skills.
- **SkillCheck-Free** (BehiSecc) — validate SKILL.md structure/naming/semantics.
