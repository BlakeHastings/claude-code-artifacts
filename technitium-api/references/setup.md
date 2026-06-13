# Technitium API token setup

The CLI needs two values: `TECHNITIUM_HOST` (e.g. `dns-vm.lan` or `192.168.0.250`)
and `TECHNITIUM_TOKEN` (a non-expiring API token).

## Option A — reuse the existing Infisical secrets (preferred)

The homelab already stores a token for the Terraform `kenske/technitium` provider
at Infisical path `/technitium/`. Any shell launched with

```bash
infisical run --env=prod --path=/technitium -- bash
```

will have `TECHNITIUM_HOST` and `TECHNITIUM_TOKEN` exported. The CLI picks them
up from the environment automatically — nothing else to do.

**Caveat:** that token belongs to the `terraform` user and has full DhcpServer /
Zones / Settings permissions. Fine for read-only exploration; for anything
mutating, mint a dedicated token (Option B) so the audit trail is clean.

## Option B — mint a fresh token in the Technitium UI

1. Open the UI: <http://192.168.0.250:5380>
2. Log in as `admin` (or your own user account).
3. Top-right user menu → **Administration → Sessions** tab.
4. Click **Create Token**.
   - Pick the user the token will act as (create a dedicated read-only user
     first if you only need read access — Administration → Users → Create).
   - Give the token a name like `claude-cli`.
5. Copy the generated token string immediately — it is shown only once.
6. Store it via the CLI's `configure` command (run in your own terminal, not
   inside a Claude conversation):

```bash
uv run C:/Users/Blake/.claude/skills/technitium-api/scripts/technitium.py configure
```

You'll be prompted for the host and token; both are stored in your OS keyring
under service name `technitium-api`.

## Verify

```bash
uv run .../technitium.py whoami
```

Should print the username, server version, and the permission matrix for the
token. If it prints `invalid-token`, the token was revoked or copied wrong —
mint a new one.

## Revoking a token

UI → Administration → Sessions → find the row by name → **Delete**. Any further
API calls with that token return `invalid-token`.
