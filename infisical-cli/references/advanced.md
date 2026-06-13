# Advanced operations

Less-common commands. Most are operator/admin features for self-hosted instances or CI integration.

## Service tokens — `infisical service-token create`

Service tokens are scoped, expiring credentials for CI and external services. Prefer **machine identities** (universal-auth) when possible — service tokens are the older mechanism and have less granular RBAC.

```bash
infisical service-token create \
  --name "deploy-prod" \
  --scope prod:/ \
  --access-level read,write \
  --expiry-seconds 86400        # 1 day; 0 = never expires (avoid)

# Multiple scopes
infisical service-token create \
  --name "ci-staging" \
  --scope "staging:/" --scope "staging:/api" \
  --access-level read

# Print only the token (for capturing into a CI secret)
infisical service-token create --name foo --scope dev:/ --token-only
```

**Scope syntax:** `<env-slug>:<folder-path>`. Use `--scope` multiple times for multi-path access.

**Access levels:** `read`, `write`, or both (comma-separated).

Tokens are shown **once** — capture them into the consumer's secret store immediately. Store in CI secret manager or another vault; never commit.

## Long-running secret refresh — `infisical agent`

Daemon that maintains a valid access token and refreshes secret templates on disk. Use when:

- A workload needs secrets refreshed without restarts
- You want to write a templated config file (e.g., `nginx.conf` with embedded creds) and have it re-rendered on rotation
- Multiple processes on one host share an access token

```bash
infisical agent --config ./agent-config.yaml
```

Config format documented at https://infisical.com/docs/cli/commands/agent. Typical structure:

```yaml
infisical:
  address: https://app.infisical.com
auth:
  type: universal-auth
  config:
    client-id: /path/to/client-id
    client-secret: /path/to/client-secret
    remove_client_secret_on_read: false
sinks:
  - type: file
    config:
      path: /var/run/infisical/token
templates:
  - source-path: ./nginx.conf.tmpl
    destination-path: /etc/nginx/conf.d/app.conf
    config:
      polling-interval: 60s
      execute:
        command: nginx -s reload
```

## Dynamic secrets — `infisical dynamic-secrets`

Dynamic secrets are short-lived credentials issued on demand (e.g., per-session Postgres users). The CLI lists them and manages leases.

```bash
infisical dynamic-secrets --env=prod --path=/db          # list
infisical dynamic-secrets lease --help                   # lease subcommands
```

Lease subcommands manage issued credential lifetimes. Most lease creation is done through the UI/API; the CLI is primarily for inspection and revocation.

## Secret scanning — `infisical scan`

Find leaked credentials in code. Powered by a gitleaks-style ruleset.

```bash
infisical scan                                     # scan full git history
infisical scan git-changes                         # only uncommitted/staged changes
infisical scan --no-git --source ./dir             # plain directory scan
infisical scan --pipe < some_file                  # scan stdin
infisical scan -v                                  # verbose; show file/location of each match
infisical scan -f sarif -r report.sarif            # SARIF for GitHub code-scanning
infisical scan -f json -r report.json
infisical scan --redact                            # mask secret values in output
infisical scan --baseline-path baseline.json       # ignore matches already in baseline
infisical scan -c .infisical-scan.toml             # custom rules / allowlists
```

Set up a pre-commit hook:

```bash
infisical scan install pre-commit                  # writes the hook
```

`--exit-code` controls the exit status when leaks are found (default 1 — fails CI).

Custom config (`.infisical-scan.toml`) lookup order: `--config` flag → `INFISICAL_SCAN_CONFIG` env var → `<source>/.infisical-scan.toml` → built-in defaults.

## Self-hosted instance bootstrap — `infisical bootstrap`

Initializes a *brand-new* self-hosted Infisical instance: creates the root organization and admin user. Run **once** against an empty deployment.

```bash
infisical bootstrap \
  --domain https://infisical.homelab.example \
  --email admin@example.com \
  --password "$(openssl rand -base64 32)" \
  --organization "Homelab"

# Kubernetes integration: write admin creds into a Secret
infisical bootstrap \
  --domain https://infisical.homelab.example \
  --email admin@example.com \
  --password "$ADMIN_PASS" \
  --organization "Homelab" \
  --output k8-secret \
  --k8-secret-namespace infisical \
  --k8-secret-name infisical-admin

# Make the call idempotent in pipelines
infisical bootstrap … --ignore-if-bootstrapped
```

**Confirm with the user before running bootstrap** — calling it against the wrong instance is destructive in the sense that it creates a root identity that may be hard to remove.

## Self-hosted networking

These are operator commands for a self-hosted Infisical deployment, not typical developer usage.

| Command | Purpose |
|---------|---------|
| `infisical gateway` | Run the Infisical gateway; also manages its systemd service |
| `infisical relay` | Run a relay node for connecting private networks |
| `infisical proxy` | Run the Infisical proxy server |

Each has subcommands for `install` / `uninstall` / `start` / `stop` of the underlying systemd unit on Linux. Consult `infisical <cmd> --help` for the exact subcommand surface on the installed version (this changes between releases).

## Certificates — `infisical cert-manager`

Issue and manage TLS certificates from Infisical's PKI module. Useful when you've configured Infisical as an internal CA.

```bash
infisical cert-manager --help
```

Subcommands typically include issuance, renewal, and listing. Specifics vary by version.

## KMIP — `infisical kmip`

Manage KMIP (Key Management Interoperability Protocol) servers and clients for cryptographic operations against external HSMs or KMS providers. Enterprise feature.

## PAM — `infisical pam`

Privileged Access Management. Manages just-in-time elevated credentials. Enterprise feature.

## SSH — `infisical ssh`

Issue short-lived SSH credentials (certificates) for bastion/jump-host access. Replaces persistent SSH keys with per-session certs.

```bash
infisical ssh --help
```

Subcommands cover certificate issuance and host configuration. See https://infisical.com/docs/documentation/platform/ssh.

## Putting it together: homelab/CI patterns

### GitHub Actions with universal-auth

```yaml
- name: Install Infisical CLI
  run: npm install -g @infisical/cli

- name: Login
  run: |
    echo "INFISICAL_TOKEN=$(infisical login \
      --method=universal-auth \
      --client-id=${{ secrets.INFISICAL_CLIENT_ID }} \
      --client-secret=${{ secrets.INFISICAL_CLIENT_SECRET }} \
      --domain=${{ vars.INFISICAL_URL }}/api \
      --plain --silent)" >> $GITHUB_ENV

- name: Deploy with injected secrets
  env:
    INFISICAL_API_URL: ${{ vars.INFISICAL_URL }}/api
  run: |
    infisical run --projectId=${{ vars.INFISICAL_PROJECT_ID }} --env=prod -- ./deploy.sh
```

Only two GitHub secrets remain: `INFISICAL_CLIENT_ID` and `INFISICAL_CLIENT_SECRET`. Everything else lives in Infisical.

### Self-hosted runner with persistent agent

```bash
# On the runner, once at provisioning time
infisical agent --config /etc/infisical/agent.yaml &

# Jobs read from the token sink
INFISICAL_TOKEN=$(cat /var/run/infisical/token) \
  infisical run --projectId=... --env=prod -- ./job
```

### Local dev with personal overrides

```bash
# Team member sets a personal override of DATABASE_URL to point at their local DB
infisical secrets set DATABASE_URL=postgres://localhost:5432/myapp --type=personal

# Their `infisical run` picks up the personal value, the team's shared value is unchanged
infisical run -- npm run dev
```
