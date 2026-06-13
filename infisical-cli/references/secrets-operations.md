# Secrets, folders, and runtime injection

Every secret in Infisical is addressed by **`(environment, path, key)`**. Almost every command takes `--env` (default `dev`) and `--path` (default `/`). Many also take `--projectId` when not using a linked `.infisical.json`.

## `.infisical.json` (project linking)

`infisical init` writes `.infisical.json` in the current directory. It pins the workspace ID and a default environment, so subsequent commands don't need `--projectId`. Commit it — it contains no secrets.

```json
{
  "workspaceId": "abc123…",
  "defaultEnvironment": "dev"
}
```

If you're in a subdirectory and need to point at a different config: `--project-config-dir /path/to/dir` (on `run` only). Otherwise, run commands from a directory at or below the one containing `.infisical.json`.

## Listing secrets — `infisical secrets`

```bash
infisical secrets                              # list dev secrets at /
infisical secrets --env=prod --path=/api       # list prod/api
infisical secrets --recursive                  # include all sub-folders
infisical secrets -o json                      # output formats: yaml | json | dotenv
infisical secrets --tags db,critical           # filter by tag slugs
infisical secrets --include-imports=false      # exclude imported (linked) secrets
```

Default behavior **expands references** (`${OTHER_SECRET}`-style) and **prioritizes personal secrets** over shared ones if both exist with the same key. Override:

```bash
infisical secrets --expand=false               # raw values, no expansion
infisical secrets --secret-overriding=false    # show shared, ignore personal
```

## Reading a single secret — `infisical secrets get`

```bash
infisical secrets get DATABASE_URL
infisical secrets get DATABASE_URL API_KEY     # multiple at once
infisical secrets get DATABASE_URL --plain     # print value only (one per line)
infisical secrets get DATABASE_URL -o json     # structured output
infisical secrets get DATABASE_URL --env=prod --path=/database
```

Use `--plain` when piping into another command:

```bash
PG_URL=$(infisical secrets get DATABASE_URL --plain)
```

## Writing secrets — `infisical secrets set`

```bash
# Inline key=value pairs (positional args)
infisical secrets set DATABASE_URL=postgres://... API_KEY=abc123

# Read value from a file (note the @ prefix)
infisical secrets set TLS_CERT=@/path/to/cert.pem

# Bulk from a file (.env or YAML; mutually exclusive with inline args)
infisical secrets set --file .env.template

# Targeting
infisical secrets set FOO=bar --env=prod --path=/database
infisical secrets set DEBUG=true --type=personal     # default is "shared"
```

`set` is **upsert**: existing keys are overwritten silently. There is no dry-run flag — verify `--env` and `--path` before running.

### `--file` formats

- **.env** (`KEY=value` per line; `#` and `//` comments allowed)
- **YAML** (`KEY: value`)

The CLI auto-detects based on content; explicit extension isn't required.

## Deleting secrets — `infisical secrets delete`

```bash
infisical secrets delete OLD_KEY                       # WARNING: default --type=personal
infisical secrets delete OLD_KEY --type=shared         # team-wide secret
infisical secrets delete A B C --env=prod              # multiple at once
```

**Footgun:** `delete` defaults to `--type personal` (opposite of `set` which defaults to `shared`). To delete the shared/team secret, pass `--type=shared` explicitly. Always confirm with the user.

## Folders — `infisical secrets folders`

Folders organize secrets within an environment.

```bash
infisical secrets folders get                              # list folders at /
infisical secrets folders get --path=/api                  # list folders inside /api
infisical secrets folders create -p / -n services          # create /services
infisical secrets folders create -p /services -n database  # create /services/database
infisical secrets folders delete -p /services -n database  # delete /services/database
infisical secrets folders create -n staging --env=prod -o json
```

Flags:
- `-p`, `--path` — parent path the folder lives in (or, for delete, is in)
- `-n`, `--name` — folder name
- `--env` — environment

There is no `mv` or `cp` for folders — recreate at the target path and delete the source.

## Generating an example `.env`

```bash
infisical secrets generate-example-env > .example-env
```

Produces a file with all current secret keys and empty/placeholder values — safe to commit, useful for onboarding contributors.

## Injecting secrets into a process — `infisical run`

This is the highest-value command. It fetches secrets, sets them as env vars, and execs your process. Secrets never touch disk.

```bash
# Anything after `--` is the command
infisical run -- npm run dev
infisical run --env=prod -- ./server
infisical run --env=prod --path=/api -- node index.js

# Chained shell commands (avoids needing `--`)
infisical run --command "npm install && npm run build && npm start"

# Multi-path injection
infisical run --path=/api --path=/db -- ./server

# Live reload on secret change (polls every --watch-interval seconds)
infisical run --watch --watch-interval 10 -- ./server

# Force a project ID (overrides .infisical.json)
infisical run --projectId=<id> --env=staging -- ./script.sh

# Use a pre-issued token (machine identity)
infisical run --token=$INFISICAL_TOKEN --projectId=<id> --env=prod -- ./deploy

# Filter by tag
infisical run --tags backend,critical -- ./server
```

Flags worth knowing:
- `--recursive` — include secrets from all sub-folders below `--path`
- `--include-imports=false` — skip imported (linked) secrets
- `--secret-overriding=false` — ignore personal overrides
- `--expand=false` — don't process `${reference}` expansion
- `--project-config-dir <dir>` — explicit `.infisical.json` directory

## Exporting secrets to a file — `infisical export`

Use sparingly — exported files are sensitive. Prefer `run` for runtime injection.

```bash
infisical export --env=prod --format=dotenv > .env.prod
infisical export --env=prod --format=json -o secrets.json
infisical export --env=prod --format=csv -o secrets.csv
infisical export --env=prod --path=/api --format=dotenv -o api.env
infisical export --template=./template.tmpl -o config.yaml    # Go-template rendering
```

Formats: `dotenv` (default), `json`, `csv`.

**Always** add the exported filename pattern to `.gitignore` *before* running `export`. A common pattern:

```gitignore
.env
.env.*
!.env.example
secrets.json
secrets.csv
```

### Templates

The `--template` flag renders secrets through a Go text/template. Example template:

```
database:
  host: {{ .DB_HOST }}
  port: {{ .DB_PORT }}
  password: {{ .DB_PASSWORD }}
```

Useful for generating config files (YAML, TOML, JSON, INI) directly from secrets without a build step.

## Env naming conventions

`dev`, `staging`, `prod` are *conventions*, not enforced. Each workspace defines its own env slugs in the Infisical UI. If `--env=prod` returns "environment not found", check the workspace's actual slugs — they might be `production`, `live`, `release`, etc.

```bash
# When unsure, log into the UI and check Settings → Environments,
# or use the API to list environments programmatically.
```

## Path conventions

Treat paths like a filesystem — `/`, `/api`, `/api/database`, etc. Useful organization patterns:

- `/` for app-wide config (`LOG_LEVEL`, `NODE_ENV`)
- `/services/<svc>` for per-service secrets
- `/integrations/<vendor>` for third-party API keys

Paths are per-environment: `/api` in `dev` and `/api` in `prod` are unrelated namespaces.

## Personal vs shared secrets

Each secret is either:
- **shared** — visible to everyone with access to the env/path (default for `set`)
- **personal** — your override of a shared secret, visible only to you

`infisical secrets` and `run` apply personal overrides by default. Pass `--secret-overriding=false` to ignore them.

## Output format reference

`-o` / `--output` on most commands:

| Value | Use for |
|-------|---------|
| `dotenv` | Pipe into another shell as env vars |
| `json` | Programmatic consumption |
| `yaml` | Human-readable config dumps |

Plus `--plain` on `secrets` / `secrets get` for raw values one-per-line (deprecated on `secrets` itself in favor of `-o`, but still works on `get`).
