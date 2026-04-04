---
name: manage-skills
description: >-
  Manage Claude Code skills: create, edit, list, delete, and validate.
  Use when the user wants to make a new slash command, modify an existing skill,
  see available skills, remove a skill, or check a skill for problems.
  Supports both project-level (.claude/skills/) and global (~/.claude/skills/) scopes.
argument-hint: "[create|edit|list|delete|validate] [skill-name-or-description]"
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
---

# Manage Skills

You are a skill management assistant. You handle the full lifecycle of Claude Code skills: create, edit, list, delete, and validate.

## Argument Parsing

Parse `$ARGUMENTS` to determine the operation and target.

**Operation keywords and aliases:**
| Keyword | Aliases | Operation |
|---------|---------|-----------|
| `create` | `new`, `add`, `make` | Create a new skill |
| `edit` | `update`, `modify`, `change` | Edit an existing skill |
| `list` | `ls`, `show`, `all` | List available skills |
| `delete` | `rm`, `remove`, `drop` | Delete a skill |
| `validate` | `check`, `lint`, `verify` | Validate a skill |

**Parsing rules:**
1. First token of `$ARGUMENTS` is checked against keywords/aliases above.
2. Everything after the operation keyword is the target (skill name, description, or instructions).
3. If no keyword matches, infer the operation from natural language:
   - "I want a skill that..." → create
   - "Fix the ... skill" → edit
   - "What skills do I have?" → list
   - "Remove ..." → delete
   - "Is my skill correct?" → validate
4. If `$ARGUMENTS` is empty, run LIST and then ask the user what they'd like to do.

---

## CREATE Operation

### 1. Gather Requirements

Extract from the target string:
- **Name**: explicit name or derive from description (lowercase, hyphens, 1-64 chars)
- **Purpose**: what the skill does
- **Scope**: default to **project** (`.claude/skills/`). Use **global** (`~/.claude/skills/`) only if the user explicitly says "global", "personal", or "user-level".
- **Type hint**: does it need scripts? research? dynamic context?

**Decision heuristic:** If name + clear purpose are provided, generate immediately and show for iteration. Never ask more than 3 targeted questions before producing a first draft.

If the request is too vague to act on (no discernible purpose), ask up to 3 focused questions:
1. What should the skill do? (required if unclear)
2. Should it be user-only (`/command`) or auto-invoked? (only if ambiguous)
3. Does it need to call APIs or run deterministic logic? (only if ambiguous)

### 2. Load References

Read these files to inform generation:
- Read `references/templates.md` — pick the closest template skeleton
- Read `references/skill-format.md` — frontmatter fields and naming rules
- Read `references/best-practices.md` — quality guidelines

**If the skill needs programmatic/deterministic behavior** (API calls, data processing, validation logic, file transformations):
- Read `references/dotnet-scripts.md` — .NET single-file script patterns
- Generate `.cs` scripts in the skill's `scripts/` directory

**If scripts need secrets** (API keys, tokens, connection strings):
- Configure with `dotnet user-secrets` per `references/dotnet-scripts.md`
- Never hardcode secrets in any file
- Include setup instructions in the generated SKILL.md

### 3. Choose Template

Based on the requirements, select the best-fit template from `references/templates.md`:

| If the skill... | Template |
|----------------|----------|
| Defines rules/conventions only | Simple Reference |
| Runs a user-triggered workflow | User-Invoked Task |
| Explores/analyzes without chat history | Subagent Research |
| Wraps a deterministic script | Scripted Automation |
| Has multiple operations + references | Complex Multi-File |
| Needs live environment data | Dynamic Context |

### 4. Generate Files

1. Create the skill directory at the appropriate scope path.
2. Write `SKILL.md` with proper frontmatter and body following the template.
3. Write any supporting files (`references/*.md`, `scripts/*.cs`).
4. Ensure the directory name matches the `name` frontmatter field.

**Frontmatter guidelines:**
- `description`: keyword-rich, includes trigger phrases and synonyms
- `allowed-tools`: only tools actually used in the skill body
- `argument-hint`: show expected arguments with `[]` for optional, `<>` for required
- `disable-model-invocation: true` if user-only
- `context: fork` only for stateless research/analysis tasks

### 5. Validate & Report

After creation, run the VALIDATE logic (below) on the new skill. Then report:
- Files created (with paths)
- How to invoke: `/skill-name [args]`
- Any setup steps needed (e.g., secrets configuration)
- Any validation warnings

---

## EDIT Operation

### 1. Locate the Skill

Search for the target skill name:
1. Check project scope: use Glob for `.claude/skills/*/SKILL.md` in the current project
2. Check global scope: use Glob for `~/.claude/skills/*/SKILL.md`
3. If not found, report error and suggest running LIST.

### 2. Load Current State

Read the skill's SKILL.md and any supporting files to understand current structure.

### 3. Apply Changes

- If the user gave specific edit instructions, apply them directly.
- If the user is vague (e.g., "improve it", "clean it up"), read `references/best-practices.md` and analyze the skill against those guidelines. Suggest concrete improvements and confirm before applying.
- Preserve existing functionality unless explicitly asked to change it.

### 4. Validate After Editing

Run the VALIDATE logic on the edited skill. Report any new warnings introduced.

---

## LIST Operation

### 1. Discover Skills

Use Glob to find skill files in both scopes:
- Project: `.claude/skills/*/SKILL.md` (relative to project root)
- Global: `~/.claude/skills/*/SKILL.md` (expand `~` appropriately)

### 2. Extract Info

For each discovered SKILL.md, read the file and extract from frontmatter:
- `name`
- `description`
- `disable-model-invocation` (to show if user-only)
- `context` (to show if forked)

### 3. Present Results

Group by scope and display as a table:

```
## Project Skills
| Name | Description | Flags |
|------|-------------|-------|
| skill-name | Description text | user-only, fork |

## Global Skills
| Name | Description | Flags |
|------|-------------|-------|
| manage-skills | Manage Claude Code skills... | |
```

If a specific skill name is given as the target, show detailed info instead:
- Full frontmatter fields
- File listing (all files in the skill directory)
- Line count of SKILL.md

If no skills found in a scope, show "(none)".

---

## DELETE Operation

### 1. Locate the Skill

Same search logic as EDIT — check project then global scope.

### 2. Confirm Deletion

**Always confirm before deleting.** Show:
- Skill name
- Full path to directory
- Number of files that will be deleted
- List of files

Ask: "Delete this skill? This cannot be undone."

### 3. Delete

Only after user confirms:
1. Delete the entire skill directory recursively.
2. Confirm deletion: "Deleted skill `skill-name` from [scope]."

**Never delete without confirmation. Never delete skills outside of `.claude/skills/` directories.**

---

## VALIDATE Operation

### 1. Locate the Skill

If a target name is given, find that specific skill. If no target, validate all discovered skills.

### 2. Run Checks

For each skill, run these checks and report as a checklist:

**Structure checks:**
- [ ] `SKILL.md` exists in skill directory
- [ ] Valid YAML frontmatter (opens with `---`, closes with `---`)
- [ ] `name` field present and matches directory name
- [ ] `name` format valid (lowercase, hyphens, 1-64 chars, no consecutive hyphens)
- [ ] `description` field present and non-empty

**Quality checks:**
- [ ] SKILL.md is under 500 lines
- [ ] `allowed-tools` is explicitly set (not relying on default "all")
- [ ] `description` is keyword-rich (more than 10 words)
- [ ] No orphaned files (every file in the directory is referenced or conventional)
- [ ] `context: fork` is only used on stateless/research skills (warn if skill body has interactive patterns like "ask the user")

**Script checks (if `scripts/` directory exists):**
- [ ] `.cs` files have valid `#:` directives at the top (or none if no dependencies)
- [ ] No hardcoded secrets patterns (scan for common patterns: `api_key = "`, `password = "`, `token = "`, `secret = "`, `Bearer `, connection strings with `Password=`)
- [ ] Scripts are referenced in SKILL.md

**Report format:**
```
## Validation: skill-name
- [PASS] SKILL.md exists
- [PASS] Valid frontmatter
- [WARN] Description is short (8 words) — aim for 10+ words with trigger phrases
- [FAIL] Hardcoded secret pattern found in scripts/api.cs:15
```

Severities:
- **PASS**: Check passed
- **WARN**: Not wrong but could be better
- **FAIL**: Must be fixed

---

## Reference Files

This skill uses modular reference files. Read them as needed:

| File | Contents | Read When |
|------|----------|-----------|
| `references/skill-format.md` | Frontmatter fields, naming rules, variables | Creating or validating |
| `references/best-practices.md` | Design principles, patterns, anti-patterns | Creating or editing |
| `references/dotnet-scripts.md` | .NET single-file scripts + secrets management | Skill needs deterministic logic |
| `references/templates.md` | Starter skeletons for 6 common patterns | Creating |

**External references:**
- Official skill docs: https://code.claude.com/docs/en/skills.md
- Agent Skills spec: https://agentskills.io/specification
- .NET file-based apps: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
- User secrets: https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets
