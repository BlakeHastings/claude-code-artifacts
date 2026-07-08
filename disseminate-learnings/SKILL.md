---
name: disseminate-learnings
description: >-
  Capture the durable learnings from a work session and route each one into the
  right Agent Skill instead of a dump file: augment an existing skill or create a
  new one, global-and-reusable vs repo-local, written to skill-authoring best
  practices. Use at the end of a substantive session, or when asked to "capture
  learnings", "store what we did in skills", "disseminate knowledge", "wrap up",
  or after solving something non-obvious worth reusing. Covers what is worth
  keeping, classifying by scope and type, finding the existing home before
  creating, writing concrete trigger-first skills, the global-vs-committed
  reference conventions, and avoiding the monolithic learnings file trap.
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, WebSearch, WebFetch
---

# Disseminate learnings into skills

Turn what a session discovered into knowledge the next session inherits. The core
move is **routing**: each learning goes into the skill that owns its topic, so it
loads only when relevant and never bloats unrelated context. Do not pour everything
into one growing `learnings.md`; that is the trap most setups then have to manage.
A learning lives in its own lane, the same way a skill's instructions do.

## When to run it

Run at the end of a substantive session, or on "capture learnings", "store what we
did in skills", "disseminate knowledge", or "wrap up". Worth doing when the session
produced something a future session would otherwise re-derive: a non-obvious fix, a
hard-won gotcha, a convention, a tooling trick. Skip trivial sessions. Signal over
volume: a few sharp entries beat a long bland list.

## Step 1: Harvest

Pull candidate learnings from the session. Favor:

- A solution that took several attempts, and why the wrong ones failed.
- A recurring error and its exact fix.
- Surprising library, API, tool, or platform behavior.
- A convention or architectural decision and the reason behind it.
- A tooling trick (a flag, a command, a verification technique).
- Negative learnings ("do not do X, it causes Y"); these save the most time.
- Open questions worth picking up next time.

Do not capture what is already recorded elsewhere: facts in git history, the diff,
`CLAUDE.md`/`AGENTS.md`, or the code itself; or one-off conversational details that
will not recur. If asked to "remember" one of those, capture instead what was
non-obvious about it.

## Step 2: Classify each learning

Two axes:

- **Scope.** Project-agnostic and reusable across repos, or specific to this repo?
  Prefer global and reusable when the learning is a general technique; keep it local
  only when it is genuinely tied to this codebase's structure, names, or decisions.
- **Type.** Technique / gotcha, convention, tooling, project fact, or external
  reference. Type hints at the home and the wording.

## Step 3: Find the home (augment before you create)

Search before writing. List skills in both scopes and grep their descriptions for the
topic:

```bash
ls ~/.claude/skills */SKILL.md            # global (personal) skills
ls .claude/skills */SKILL.md              # repo-local skills (committed)
```

- If a skill already covers the topic, **augment it** (a new section or a few bullets).
  Augmenting keeps related knowledge together and improves discovery.
- Only **create a new skill** when no existing one fits and the topic is substantial
  enough to stand alone. Splitting a coherent topic across two skills is worse than
  one well-organized skill.
- Place global, reusable learnings in `~/.claude/skills`; repo-specific ones in the
  repo's `.claude/skills` (committed). When a learning has both a general core and a
  repo-specific application, put the general part in the global skill and a thin
  repo-specific pointer in the local one.

## Step 4: Write it well

Follow skill-authoring best practices so the entry actually fires and actually helps:

- **The description is a trigger, not a summary.** Third person, "use when X", with the
  concrete vocabulary someone would phrase the task in. Keep it under ~1024 characters.
  When augmenting, add the new topic's keywords to the existing description.
- **Be concrete (the specificity rule).** A cold reader must know exactly what to do or
  avoid without re-investigating. Weak: "promises can be tricky". Strong: "`Promise.all`
  rejects on the first failure; use `Promise.allSettled` and batch to ~10 to avoid the
  30-item timeout." Include real names, flags, and error text.
- **Assume agent competence.** Cut explanation the reader does not need. Use one term per
  concept, consistently.
- **Write for an unknown future reader.** A skill may not be read again for months or
  years. Before writing any claim, ask: "Will a reader be misled if this is no longer
  true?" Separate two categories:
  - *Mechanisms* -- how a system works at an architectural or OS level (e.g., how
    Windows DLL search order works, how a state machine is wired). These are stable;
    write them as direct statements.
  - *World-state* -- which version of a library requires what, what behavior a package
    exhibits, what Python version a wheel supports. These decay. Write them as
    observations with explicit scope: "as of version X, Y behaved this way -- verify
    before relying on it." Point the reader toward how to check rather than asserting
    the current truth.
  - Avoid date-anchored "as of 2025" language; it reads as stale faster than version
    anchors do. Prefer "the version in use at the time this was written" or let the
    version number carry the scope.
- **Keep the body under ~500 lines.** Split with progressive disclosure: link supporting
  files one level deep from `SKILL.md`, with forward slashes. Add a short table of
  contents to long reference files.
- **Scripts vs instructions.** Use a script for fragile, deterministic, or repeated
  operations; use prose for flexible, judgment-based work. Say whether a script is to be
  run or read.
- **Checklists** for multi-step processes, so the reader can copy and track them.

## Step 5: Conventions for committed (repo-local) skills

- **Do not hard-name personal global skills** in a committed file; point to how they are
  distributed instead (e.g. "available via the protostar CLI"). Naming a personal skill
  in a shared repo is a dangling reference for anyone without it.
- **Match the repo's standards.** Run the project's formatter/linter on the changed skill
  before committing.
- **Scope the commit.** Stage only the skill files you changed. Do not sweep unrelated
  working-tree changes (another session's in-progress work, generated files) into the
  commit. Check `git diff --cached --stat` before committing.

## Step 6: Verify, then review

- Re-read each touched skill: no duplicated entry, references resolve, the description
  covers the new content.
- **Present a short plan before writing or committing** when the routing is non-trivial
  (which skills get touched, augment vs create, global vs local) so a human can steer.
  Treat captured learnings as a draft: a quick human pass catches the ~10% that are wrong
  or already stale.

## Anti-patterns

- **The monolithic dump.** One ever-growing learnings file that loads regardless of task.
  Route to lanes instead.
- **Vague entries.** "Be careful with X" teaches nothing. Make it actionable or drop it.
- **Re-recording the obvious.** Anything git, the diff, `CLAUDE.md`, or the code already
  says is noise here.
- **Capturing everything.** Trivial sessions produce nothing worth keeping; quality beats
  coverage.
- **Letting files bloat.** Prune stale or contradicting entries; scope or split a skill
  before it sprawls.
- **Hard-naming personal skills in committed files.** Use a distribution pointer.
- **Asserting world-state as permanent fact.** Writing "library X requires version Y of Z"
  or "this API behaves this way" without scoping it to the version observed is one of
  the most common ways a skill misleads a future reader. The trap: the entry is precise
  and confident, so the reader trusts it and skips verification. Scope every world-state
  claim to the version or era it was observed in, and tell the reader where to verify.

## Iterating on this skill

This skill is meant to grow. When a capture session reveals a better routing rule, a new
category, or a recurring mistake in how learnings get written, add it here, concretely.
