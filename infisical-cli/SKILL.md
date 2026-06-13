---
name: infisical-cli
description: >-
  Infisical CLI reference for managing secrets, projects, environments, and
  service tokens. Use when working with the `infisical` CLI — logging in with
  user or machine-identity (universal-auth), linking a folder to a project via
  `infisical init`, reading/writing/deleting secrets across environments
  (dev/staging/prod) and folder paths, injecting secrets into a process with
  `infisical run`, exporting to dotenv/JSON/CSV, creating service tokens for
  CI, scanning a repo for leaked secrets, bootstrapping a self-hosted instance,
  or pointing the CLI at a self-hosted domain. Covers Infisical Cloud
  (US/EU/self-hosted) and the npm-installed `@infisical/cli` binary.
allowed-tools: Bash, Read, Write, Edit, Grep, Glob
---

# Infisical CLI

Practical guide to driving Infisical from the command line. Always confirm with the user before any destructive operation (`secrets delete`, `secrets folders delete`, `reset`, `bootstrap` against a populated instance).

## When to read which reference

| File | Read when |
|------|-----------|
| `references/authentication.md` | Logging in, picking an auth method, configuring `INFISICAL_TOKEN`, self-hosted domain, profile/vault management |
| `references/secrets-operations.md` | Any `infisical secrets …` or `infisical run/export` operation, folder management, env paths, output formats |
| `references/advanced.md` | `bootstrap`, `scan`, `gateway`, `agent`, `dynamic-secrets`, `service-token`, `cert-manager`, `kmip`, `pam`, `ssh` |

Always read the reference for the operation at hand before issuing commands — flag semantics differ between subcommands (e.g., `--type` defaults to `shared` for `set` but `personal` for `delete`).

## Installation check

Before running any command, verify the CLI is available:

!`infisical --version 2>&1 || echo "infisical not installed; run: npm install -g @infisical/cli"`

If missing, install: `npm install -g @infisical/cli`. Other installers (Homebrew, Scoop, Winget, apt/yum) are documented at https://infisical.com/docs/cli/overview.

**Windows note:** on git-bash / MSYS the npm-installed `infisical` shim may fail with `Permission denied`. Use PowerShell, or invoke `infisical.exe` directly. The `infisical.ps1` shim in `%APPDATA%\npm\` works from PowerShell.

## Core mental model

1. **Login** produces a token cached in the OS keychain (`vault`). For interactive use, `infisical login` is enough; for CI/automation, use `universal-auth` and export `INFISICAL_TOKEN`.
2. **`infisical init`** writes `.infisical.json` linking the current folder to a workspace + default environment. Most subsequent commands read this file. Commit `.infisical.json` to the repo.
3. **Secrets live at `(environment, path)`** — e.g., `prod` env, `/database` path. `--env` (default `dev`) and `--path` (default `/`) appear on nearly every command.
4. **`infisical run -- <cmd>`** is the primary integration point: it fetches secrets, injects them as env vars, and execs your process. Prefer this over `export` to disk.
5. **Self-hosted** users must pass `--domain https://your-host/api` or set `INFISICAL_API_URL` once. Once you log in with `--domain`, it's remembered per profile.

## Common workflows

### First-time setup for a repo

```bash
cd path/to/repo
infisical login                    # interactive; pick org, opens browser
infisical init                     # pick workspace + default env; writes .infisical.json
git add .infisical.json && git commit -m "Link repo to Infisical workspace"
```

### Run an app with injected secrets

```bash
infisical run --env=dev -- npm run dev
infisical run --env=prod --path=/api -- ./server
infisical run --command "migrate && start"     # chained shell commands
```

### Read / write / delete a secret

```bash
infisical secrets                              # list dev secrets at /
infisical secrets --env=prod --path=/database  # list prod secrets at /database
infisical secrets get DATABASE_URL --plain     # value only
infisical secrets set DATABASE_URL=postgres://... API_KEY=abc
infisical secrets delete OLD_KEY --type shared # default --type for delete is "personal"
```

See `references/secrets-operations.md` for `--file`, `@/path/to/file`, tags, recursion, output formats.

### Export to a file

```bash
infisical export --env=prod --format=dotenv > .env.prod
infisical export --env=prod --format=json -o secrets.json
infisical export --env=prod --template=./template.tmpl
```

### CI / machine identity

```bash
export INFISICAL_TOKEN=$(infisical login \
  --method=universal-auth \
  --client-id="$INFISICAL_CLIENT_ID" \
  --client-secret="$INFISICAL_CLIENT_SECRET" \
  --plain --silent)

infisical secrets --projectId=<id> --env=prod
infisical run --projectId=<id> --env=prod -- ./deploy.sh
```

`INFISICAL_UNIVERSAL_AUTH_CLIENT_ID` / `INFISICAL_UNIVERSAL_AUTH_CLIENT_SECRET` env vars are also picked up — see `references/authentication.md`.

### Self-hosted endpoint

```bash
export INFISICAL_API_URL=https://infisical.homelab.example/api
infisical login                    # remembers --domain for this profile
# OR per-command:
infisical secrets --domain=https://infisical.homelab.example/api --env=dev
```

### Pre-commit secret scanning

```bash
infisical scan                                 # scan git history
infisical scan git-changes                     # scan only uncommitted changes
infisical scan --no-git --source ./dir         # scan plain directory
infisical scan install pre-commit              # set up a pre-commit hook
```

## Project creation

Creating *new* projects is **not** a CLI operation — Infisical projects are created via the web UI or API. The CLI can:

- **Link** an existing project to a folder: `infisical init`
- **Bootstrap** a brand-new self-hosted *instance* (organization + admin): `infisical bootstrap` (see `references/advanced.md`)
- **Issue credentials** for an existing project: `infisical service-token create`, `infisical login --method=universal-auth`

If the user asks "create a project", clarify whether they mean (a) link this repo to an existing workspace (`init`), (b) bootstrap a new self-hosted instance (`bootstrap`), or (c) actually create a workspace inside an existing instance (web UI / API — point them to https://infisical.com/docs/api-reference).

## Top-level command map

| Command | Purpose | Detail in |
|---------|---------|-----------|
| `login` | Authenticate; cache token in keychain | `authentication.md` |
| `logout` *(via `reset`)* | `infisical reset` clears all local data | `authentication.md` |
| `init` | Link current folder to a workspace (`.infisical.json`) | this file |
| `user` | Switch / inspect local profiles | `authentication.md` |
| `vault` | Where the login token is stored (`file` / `auto`) | `authentication.md` |
| `token renew` | Renew a universal-auth access token | `authentication.md` |
| `secrets` | CRUD secrets and folders | `secrets-operations.md` |
| `run` | Exec a process with secrets as env vars | `secrets-operations.md` |
| `export` | Write secrets to dotenv/JSON/CSV/template | `secrets-operations.md` |
| `service-token` | Create scoped tokens for CI | `advanced.md` |
| `dynamic-secrets` | List dynamic secrets; manage leases | `advanced.md` |
| `scan` | Find leaked secrets in repo / history | `advanced.md` |
| `bootstrap` | Initialize a self-hosted instance | `advanced.md` |
| `agent` | Long-running daemon to refresh secrets | `advanced.md` |
| `gateway`, `relay`, `proxy` | Network components for self-hosted | `advanced.md` |
| `cert-manager`, `kmip`, `pam`, `ssh` | Specialized issuance/access features | `advanced.md` |
| `reset` | Wipe all local Infisical state | `authentication.md` |

## Safety rules

1. **Never paste a `--client-secret` or service token into the conversation transcript.** Read from an env var or a file the user controls.
2. **Confirm before any `delete` / `reset` / `bootstrap --ignore-if-bootstrapped`.**
3. **Check `git status` before `infisical export`** — never commit an exported secrets file. Add `.env*` patterns to `.gitignore` before exporting.
4. **`secrets set` writes to remote immediately.** There is no staging. Verify `--env` and `--path` before running.
5. **`secrets delete` defaults to `--type personal`**, not `shared`. Pass `--type shared` to delete the team-wide secret.
6. **For self-hosted with custom CA**, set `INFISICAL_API_URL` and ensure the cert chain is trusted by the OS, or curl will fail before infisical ever sees it.

## Error patterns to recognize

- `unauthorized` / `token expired` → re-run `infisical login`, or `infisical token renew <token>` for universal-auth
- `project not found` → `.infisical.json` missing or `--projectId` wrong; run `infisical init` or pass `--projectId`
- `environment "xxx" not found` → env slug mismatch; check the workspace's actual env slugs in the UI (`dev`/`staging`/`prod` are conventions, not guarantees)
- `secret already exists` on `set` → `set` is upsert; this typically means a permissions issue
- `permission denied` on `secrets ...` → token scope too narrow; check the service token's `--scope` or machine identity's role
