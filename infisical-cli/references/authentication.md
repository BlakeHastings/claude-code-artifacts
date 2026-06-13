# Authentication

Infisical supports multiple auth methods. Pick by *who* is authenticating:

| Method | Who | When |
|--------|-----|------|
| `user` | A human | Local development, interactive use |
| `universal-auth` | A machine identity | CI/CD, scripts, daemons |
| `kubernetes` | A pod | Workloads in k8s with service-account tokens |
| `azure`, `gcp-id-token`, `gcp-iam`, `aws-iam` | A cloud workload | VMs / functions with cloud-native identity |
| `oidc-auth` | An OIDC provider | GitHub Actions, GitLab CI with OIDC |
| `jwt-auth` | Anything that mints a JWT | Custom integrations |

Once logged in, the access token is cached in the OS keychain (Windows Credential Manager / macOS Keychain / Linux Secret Service) via the **vault** subsystem.

## User login (interactive)

```bash
infisical login                                 # opens browser; pick org
infisical login --interactive                   # type email/password at the terminal
infisical login --domain https://infisical.example.com/api    # self-hosted
```

After login, future commands run as that user until `infisical reset` or token expiry.

### Multiple profiles

```bash
infisical user                                  # show subcommands
infisical user get                              # inspect current profile
infisical user get token                        # print current access token
infisical user switch                           # change active profile
infisical user update                           # update profile properties
```

`infisical login` against a new domain or email creates a new profile; `infisical user switch` toggles between them.

## Machine identity / Universal Auth (CI)

The recommended pattern for non-interactive contexts:

```bash
export INFISICAL_TOKEN=$(infisical login \
  --method=universal-auth \
  --client-id="$INFISICAL_CLIENT_ID" \
  --client-secret="$INFISICAL_CLIENT_SECRET" \
  --plain --silent)
```

- `--plain` strips formatting so only the bare token is printed.
- `--silent` suppresses tips/info messages.
- The token is short-lived; renew or re-login as needed.

### Pickup environment variables

If `INFISICAL_TOKEN` is already set, subsequent commands authenticate with it automatically — no need to pass `--token` everywhere. The CLI also reads:

| Variable | Purpose |
|----------|---------|
| `INFISICAL_TOKEN` | Pre-issued access token; bypasses login |
| `INFISICAL_UNIVERSAL_AUTH_CLIENT_ID` | Used by `login --method=universal-auth` if `--client-id` not passed |
| `INFISICAL_UNIVERSAL_AUTH_CLIENT_SECRET` | Same, for `--client-secret` |
| `INFISICAL_API_URL` | Sets the base URL; equivalent to `--domain` |

### Renewing a universal-auth token

```bash
infisical token renew <access-token>
```

For long-running daemons, prefer `infisical agent` (see `advanced.md`) which handles refresh automatically.

### Other machine login methods

```bash
infisical login --method=kubernetes \
  --machine-identity-id=<id> \
  --service-account-token-path=/var/run/secrets/kubernetes.io/serviceaccount/token

infisical login --method=aws-iam --machine-identity-id=<id>
infisical login --method=gcp-id-token --machine-identity-id=<id>
infisical login --method=gcp-iam --machine-identity-id=<id> \
  --service-account-key-file-path=/etc/gcp-sa.json
infisical login --method=azure --machine-identity-id=<id>

infisical login --method=oidc-auth --machine-identity-id=<id> --jwt="$OIDC_JWT"
infisical login --method=jwt-auth   --machine-identity-id=<id> --jwt="$SIGNED_JWT"
```

`--organization-slug` scopes the session to a sub-organization the machine identity is granted access to.

## Domain configuration

The CLI defaults to `https://app.infisical.com/api` (US Cloud). For EU Cloud or self-hosted:

```bash
# Method 1: per-command
infisical secrets --domain https://eu.infisical.com/api --env=prod

# Method 2: environment variable (preferred; survives subshells)
export INFISICAL_API_URL=https://infisical.homelab.example/api
infisical secrets --env=prod

# Method 3: stored in profile (set during login)
infisical login --domain https://infisical.homelab.example/api
```

If you used `--domain` during login, the docs explicitly note you must continue using it (or `INFISICAL_API_URL`) on subsequent commands or the CLI may default back to US Cloud.

Clear stored domains:

```bash
infisical login --clear-domains
```

## Token storage (vault backend)

```bash
infisical vault set file    # store token in a plain file (CI containers, headless)
infisical vault set auto    # use OS keychain when available; fall back to file
```

Use `file` when running in containers without a keychain. Use `auto` (default) on developer workstations.

## Wiping local state

```bash
infisical reset             # deletes ALL Infisical data on the machine
```

This removes tokens, profiles, vault state, and remembered domains. There is no per-profile logout — `reset` is the nuclear option, or `infisical user switch` to a different profile.

## Account / org introspection

```bash
infisical user get          # current profile metadata
infisical user get token    # print access token (handle with care)
```

## Auth troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `401 unauthorized` after login | Token expired; or `INFISICAL_TOKEN` set to stale value | `unset INFISICAL_TOKEN` then re-login |
| Commands hit US Cloud despite `--domain` at login | New shell didn't inherit profile | `export INFISICAL_API_URL=…` in shell rc |
| `infisical login` opens browser but never returns | Firewall blocking localhost callback | Use `--interactive` for email/password instead |
| `permission denied: …/infisical` in git-bash on Windows | npm shim's POSIX bit broken | Use PowerShell or invoke `infisical.exe` directly |
| `x509: certificate signed by unknown authority` against self-hosted | OS doesn't trust the CA | Install the CA in the OS trust store; don't disable TLS verification |
| Universal-auth login succeeds but token works once then fails | Token's TTL too short or single-use | Increase TTL on the identity in Infisical UI |
