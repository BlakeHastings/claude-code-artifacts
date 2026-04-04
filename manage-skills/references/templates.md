# Skill Templates

Starter skeletons for common skill patterns. Pick the closest match and adapt.

---

## 1. Simple Reference Skill

For conventions, style guides, coding standards. SKILL.md only, no tools needed.

```
skill-name/
└── SKILL.md
```

```yaml
---
name: skill-name
description: >-
  [Project/team] conventions for [topic]. Use when writing [context]
  or reviewing [context] for compliance with team standards.
---
# [Topic] Conventions

## Rules

1. [Rule with rationale]
2. [Rule with rationale]

## Examples

### Good
[example]

### Bad
[example]
```

---

## 2. User-Invoked Task Skill

For deployments, workflows, operations that should only run when explicitly requested.

```
skill-name/
└── SKILL.md
```

```yaml
---
name: skill-name
description: >-
  [Action description]. Use when the user wants to [trigger scenario].
argument-hint: "[required-arg] [optional-arg]"
disable-model-invocation: true
allowed-tools: Bash, Read
---
# [Task Name]

## Argument Parsing
Parse `$ARGUMENTS`: expect [describe expected format].

## Steps
1. [Validate preconditions]
2. [Perform action]
3. [Report results]

## Error Handling
- If [condition]: [recovery action]
```

---

## 3. Subagent Research Skill

For analysis, exploration, research tasks that don't need conversation history.

```
skill-name/
└── SKILL.md
```

```yaml
---
name: skill-name
description: >-
  Analyze [subject] for [purpose]. Use when investigating [scenarios]
  or exploring [domain].
argument-hint: "[target]"
context: fork
allowed-tools: Read, Glob, Grep, WebFetch, WebSearch
---
# [Research Task]

## Objective
Analyze `$ARGUMENTS` and produce [deliverable].

## Process
1. [Discovery step]
2. [Analysis step]
3. [Synthesis step]

## Output Format
[Describe expected output structure]
```

---

## 4. Scripted Automation Skill

For deterministic operations that use .NET single-file scripts. Ideal when operations must be reproducible.

```
skill-name/
├── SKILL.md
└── scripts/
    └── action.cs
```

**SKILL.md:**
```yaml
---
name: skill-name
description: >-
  [Deterministic action]. Use when the user wants to [trigger scenario].
argument-hint: "[args]"
disable-model-invocation: true
allowed-tools: Bash(dotnet run *), Read
---
# [Task Name]

## Usage
`/skill-name [args]`

## What It Does
[Brief description of the script's behavior]

## Execution
Run the script:
```
dotnet run scripts/action.cs -- $ARGUMENTS
```

## Setup
[If the script needs secrets:]
```
dotnet user-secrets set "ApiKey" "your-key" --id skill-name-secrets
```

## Error Handling
- If the script exits with code 1: [describe meaning]
```

**scripts/action.cs:**
```csharp
// #:package directives here if needed

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/action.cs -- <arg>");
    return 1;
}

// [Implementation]
Console.WriteLine($"Result: {args[0]}");
return 0;
```

---

## 5. Complex Multi-File Skill

For large orchestrations with reference material and scripts.

```
skill-name/
├── SKILL.md
├── references/
│   ├── config-ref.md
│   └── api-ref.md
└── scripts/
    └── process.cs
```

```yaml
---
name: skill-name
description: >-
  [Comprehensive action]. Use when [broad trigger scenarios].
  Handles [sub-capability 1], [sub-capability 2], and [sub-capability 3].
argument-hint: "[operation] [target]"
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
---
# [Skill Name]

## Argument Parsing
Parse `$ARGUMENTS` to determine operation:
- `[op1]` — [description]
- `[op2]` — [description]

## [Operation 1] (~X lines)
1. Read `references/config-ref.md` for [context]
2. [Steps]

## [Operation 2] (~X lines)
1. Run `dotnet run scripts/process.cs -- [args]`
2. [Steps]

## Reference Files
- `references/config-ref.md` — [what it contains]
- `references/api-ref.md` — [what it contains]
- `scripts/process.cs` — [what it does]
```

---

## 6. Dynamic Context Skill

For skills that inject live data into the prompt at invocation time.

```
skill-name/
└── SKILL.md
```

```yaml
---
name: skill-name
description: >-
  [Action] with awareness of [live context]. Use when [trigger scenario]
  and current [state] matters.
argument-hint: "[target]"
allowed-tools: Read, Bash
---
# [Task Name]

## Current State
Git branch:
!`git branch --show-current`

Last commit:
!`git log --oneline -1`

Uncommitted changes:
!`git diff --stat`

## Instructions
Given the current state above, [do what with $ARGUMENTS].
```

---

## Choosing a Template

| If the skill... | Use Template |
|----------------|-------------|
| Defines rules/conventions only | 1. Simple Reference |
| Runs a user-triggered workflow | 2. User-Invoked Task |
| Explores/analyzes without needing chat history | 3. Subagent Research |
| Wraps a deterministic script | 4. Scripted Automation |
| Has multiple operations + reference files | 5. Complex Multi-File |
| Needs live environment data in prompt | 6. Dynamic Context |
