---
name: jira-api
description: >-
  Interact with Jira Cloud via the REST API v3. Use when the user wants to
  create issues or stories, search with JQL, read issue details, list projects,
  add comments, transition issues between statuses, assign issues to a user,
  add issues to a sprint, list board sprints, or link issues (e.g. blocks /
  is blocked by) on a Jira board. Handles
  Basic auth with an Atlassian email + API token stored in the OS credential
  store, and converts plain text to Atlassian Document Format. Direct REST calls,
  no MCP server required.
argument-hint: "<auth|projects|issue|sprint> <subcommand> [args]"
allowed-tools: Bash(dotnet run *), Read
---
# Jira API

Interact with Jira Cloud (`*.atlassian.net`) through the REST API v3. Read
`references/auth-guide.md` for how to mint an API token and `references/api-ref.md`
for endpoint and field details.

## Argument Parsing

Parse `$ARGUMENTS` — the first token routes to a command group in the single
consolidated script:

| First Token | Description                                                |
|-------------|------------------------------------------------------------|
| `auth`      | Store/verify/clear Jira credentials                        |
| `projects`  | List all projects visible to the account                   |
| `issue`     | Create, get, search, comment, transition, assign, list types, link issues |
| `sprint`    | List board sprints, add issues to a sprint, report on a sprint (Agile API) |

If no arguments or an unrecognized first token, the script prints usage and stops.

## First-Time Setup

Read `references/auth-guide.md` and guide the user through:
1. Creating an API token at https://id.atlassian.com/manage-profile/security/api-tokens
2. Running `auth setup` with their site, email, and token
3. Running `auth status` to confirm the token works

Credentials are stored in the OS credential store (Windows Credential Manager /
macOS Keychain / Linux Secret Service) via `Devlooped.CredentialManager` — each
user stores their own token locally. Nothing is hardcoded, so the skill is
portable to anyone who installs it.

## Operations

All operations use `scripts/jira.cs`. The working directory is the skill root, so
relative paths work. Commands run in both bash and PowerShell.

### Auth
```
dotnet run scripts/jira.cs -- auth $2 $3 $4 $5 $6 $7
```
- `setup --site <site> --email <email> --token <api-token>` — store credentials.
  `--site` is the subdomain: for `https://acme.atlassian.net` it is `acme` (a full
  URL is also accepted and normalized).
- `status` — show stored config and verify login against `/myself`.
- `logout` — clear stored credentials.

### Projects
```
dotnet run scripts/jira.cs -- projects
```
Lists every project the account can see, with KEY, type, and name. Use this to
find the project KEY needed for `issue create`.

### Issue
```
dotnet run scripts/jira.cs -- issue $2 $3 $4 $5 $6 $7 $8 $9 ${10} ${11} ${12} ${13}
```
- `create --project <KEY> --summary <text> [--type <Story>] [--description <text>] [--description-file <path>] [--parent <KEY>] [--labels a,b,c] [--priority <name>] [--assignee <me|accountId|email>]`
  Default type is `Story`. For multi-paragraph descriptions, write the text to a
  temp file and use `--description-file`. The script renders Markdown to ADF (see
  "Description formatting" below). See `--assignee` resolution below.
- `edit <KEY> [--summary <text>] [--description <text>] [--description-file <path>] [--labels a,b,c] [--priority <name>] [--assignee <me|accountId|email>]`
  Update an existing issue. Only the fields you pass are changed; `--description`
  replaces the whole description (Markdown is rendered to ADF). At least one field
  is required. Use this to fix or revise a ticket after `create`.
- `--assignee <value>` (on `create` and `edit`) — set the assignee. Accepts `me`
  (the authenticated user, resolved via `/myself`), a raw `accountId`, or an email /
  display name (resolved via user search, first match wins). To unassign, this is not
  yet supported via flag.
- `get <KEY>` — show summary, status, type, priority, assignee, story points (if
  set), resolved date (if resolved), labels, parent, links, description.
- `search --jql "<JQL>" [--max <n>]` — run a JQL query (default max 25).
- `comment <KEY> --body <text> | --body-file <path>` — add a comment.
- `transition <KEY> [--to <status name>]` — with no `--to`, lists available
  transitions; with `--to`, applies the matching one.
- `types --project <KEY>` — list the issue types available in a project (e.g.
  Story, Bug, Task, Epic, Sub-task).
- `link <OUTWARD-KEY> --to <INWARD-KEY> [--type <name>]` — create an issue link.
  The link reads `<OUTWARD-KEY> <type.outward> <INWARD-KEY>`. Default type is
  `Blocks` (outward phrase "blocks"), so `link A --to B` means **A blocks B** (B is
  blocked by A). The type name is validated; run `linktypes` to see valid names.
- `linktypes` — list available issue link type names with their inward/outward phrasing.
- `delete <KEY>` — permanently delete an issue. Irreversible; confirm intent first.

### Sprint
```
dotnet run scripts/jira.cs -- sprint $2 $3 $4 $5 $6 $7
```
Sprint operations use Jira's Agile board API (`/rest/agile/1.0`), which is separate
from the issue REST API. Sprints live on **scrum** boards; kanban boards have none.
- `list --project <KEY> [--state active|future|closed]` — list the sprints on every
  board associated with the project, with each sprint's id, name, state, and **date
  range**. Omit `--state` to list all. Use this to find a sprint id, or to see which
  sprint is active.
- `add <KEY> [<KEY> ...] --to <active|SPRINT-ID> [--project <KEY>]` — move one or more
  issues into a sprint. `--to active` resolves the project's active sprint (so it
  needs `--project`); `--to <number>` targets a specific sprint id. This is the
  correct way to set an issue's sprint — the sprint field is a board concept, not a
  normal issue field, so it cannot be set via `issue edit`.
- `report --sprint <ID>` (or `--project <KEY> [--sprint active]`)
  `[--brief] [--by-project] [--full]` — a **human-readable "what got done" report**
  for a sprint, not just a list of keys. It prints the sprint name, state, date
  range, and goal; a completion summary (counts and story-point totals when the
  project uses points); then every issue grouped by outcome: **Completed**, **In
  progress**, **Not started / carryover**, and **Dropped** (rejected / won't do,
  split out from genuine Done). Flags shape the output:
    - `--brief` — one line per item (its summary + key), no type/status/assignee or
      description. Best for a recap you paste into chat or email.
    - `--by-project` — sub-group each outcome by project (e.g. Constellation,
      Protostar), using the project name. Combine with `--brief` for a per-project,
      one-line-each breakdown.
    - `--full` — print whole descriptions instead of one-line snippets.
  Without flags, each item shows type, status, points, assignee, and a one-line
  description, and the report ends with a per-assignee completed tally. Find the
  sprint id with `sprint list`. This is the right tool for a sprint review or
  retrospective summary.

Descriptions and comment bodies are written in **Markdown** and rendered to Jira's
Atlassian Document Format (ADF) by the script. Write real Markdown, not flat text —
Jira will display proper headings, lists, and formatting. Supported syntax:

- `#` / `##` / ... `######` — headings
- `- ` or `* ` line — bullet list item; `1. ` line — ordered list item
- ```` ``` ```` fenced block (optional language on the opening fence) — code block
- `**bold**`, `*italic*`, `` `inline code` ``, `[text](url)` — inline marks
- Blank line — paragraph break. Consecutive non-blank lines join into one paragraph
  (Markdown soft-wrap), so put each list item / heading on its own line and separate
  blocks with a blank line.

Underscores are treated literally (so `snake_case` and `CLAUDE_CONFIG_DIR` survive);
use `*` for italics. Anything outside the supported subset renders as plain text.

## Writing ticket content: capture WHAT, not HOW

A Jira ticket states **what needs to be done and why** — not how to do it. The
"how" belongs in the PR, the code, and review, where it can be discussed against a
real diff. Over-detailed tickets are a known complaint: they go stale the moment
the approach changes, and they bury the actual ask. Keep ticket bodies short and
outcome-focused.

**Put in the ticket:**
- The problem or goal, in a sentence or two — the outcome someone wants.
- Acceptance criteria when they sharpen the ask: how you'll know it's done.
- Scope / out-of-scope only when it genuinely prevents misunderstanding.

**Leave out of the ticket:**
- Implementation steps, file-by-file plans, function names, commands, or code.
- Design decisions and trade-offs (those are for the PR description / review).
- A blow-by-blow of work already done — link the PR instead of narrating it.

Default to **brief**. A few sentences or a short bullet list beats a multi-section
spec. Reserve longer descriptions for genuine ambiguity or risk that the assignee
could not otherwise resolve. If you catch yourself writing "first do X, then Y,
then edit Z," that is the "how" — cut it. The same applies to comments: link the
PR and state outcomes; do not paste the implementation narrative.

This shapes what you write into `--description` / `--description-file` and
`comment`, not how the script renders it.

## Reference Files

- `references/auth-guide.md` — Atlassian API token creation, Basic auth, credential storage
- `references/api-ref.md` — REST v3 endpoints, JQL examples, ADF notes, common fields

## Notes for Agents

- Run `auth status` first to confirm the token is valid before other operations.
- `issue create` needs the project KEY, not its name. Run `projects` if unsure.
- Issue type names are case-sensitive and project-dependent. If create fails with
  an issuetype error, run `issue types --project <KEY>` to see valid names.
- Jira Cloud v3 requires ADF (not plain text) for `description` and comment bodies.
  The script renders Markdown to ADF for you — write Markdown (see "Description
  formatting" above), not flat text, so tickets get real headings and lists.
- `--priority` and `--parent` only work if those fields are on the project's
  create screen. Omit them if create reports a field error.
- Always show the user the returned issue KEY and browse URL after creating.
- Keep descriptions and comments focused on **what/why**, not implementation
  detail — see "Writing ticket content: capture WHAT, not HOW". Default to brief.
- To set an issue's **status**, use `issue transition`; for the **assignee**, use
  `--assignee` on `issue create`/`edit`; for the **sprint**, use `sprint add`. These
  are three different mechanisms (workflow transition, issue field, Agile board API)
  and cannot be combined into one `issue edit` call.
- `sprint` needs a **scrum** board with sprints. If a project only has a kanban board,
  `sprint list` shows nothing and `sprint add --to active` reports no active sprint.
