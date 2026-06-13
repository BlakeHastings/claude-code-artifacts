---
name: technitium-api
description: >-
  Interact with the Technitium DNS server on the homelab (dns-vm.lan).
  Use when the user wants to see what devices are on the network (DHCP leases),
  list DNS zones or records, add/delete a record, query the DNS query log,
  view server stats or top clients/domains, flush the resolver cache, inspect
  allow/block lists, or do an ad-hoc resolve from the server's perspective.
  Wraps the Technitium HTTP API at http://dns-vm.lan:5380 with Bearer-token auth.
argument-hint: "<configure|whoami|leases|scopes|zones|records|stats|top|logs|cache|resolve|...> [args]"
allowed-tools: Bash(uv *), Read
---

# Technitium API

Use `scripts/technitium.py` (via `uv run`) to talk to the Technitium DNS server
on `dns-vm.lan` (192.168.0.250:5380). The UI is at <http://192.168.0.250:5380>.

## Credentials

Resolved in order: **environment variable → keyring → error**.

| Env var | Keyring key | Description |
|---------|-------------|-------------|
| `TECHNITIUM_HOST`  | `host`  | Hostname/IP, optional `:port` (defaults to `:5380`) |
| `TECHNITIUM_TOKEN` | `token` | Non-expiring API token (UI → Administration → Sessions → Create Token) |

These are the same env var names the `kenske/technitium` Terraform provider
reads, so any shell already wired up with `infisical run -- ...` from the
`/technitium/` path in Infisical will Just Work — no keyring setup needed.

### First-time setup

**NEVER run `configure` yourself — the user must enter the token directly so it
does not appear in conversation logs.**

If credentials are missing, ask the user to run this in their terminal (the
`!` prefix runs it in-session so the output lands here):

```
! uv run C:/Users/Blake/.claude/skills/technitium-api/scripts/technitium.py configure
```

See `references/setup.md` for how to mint a token before running configure.

## Argument routing

Parse `$ARGUMENTS`. The first token selects the operation:

| Arguments | What it does |
|-----------|--------------|
| `whoami` | Validate token, show user + version + permissions |
| `leases [--scope NAME] [--sort ip\|host\|mac\|expires]` | **Devices on the network** (DHCP leases) |
| `scopes` | List DHCP scopes |
| `scope <name>` | Full DHCP scope config (DNS, gateway, lease time, reservations) |
| `lease remove   <scope> <mac>` | Free a dynamic/reserved lease (careful — IP conflict risk) |
| `lease reserve  <scope> <mac>` | Convert dynamic → reserved |
| `lease dynamic  <scope> <mac>` | Convert reserved → dynamic |
| `zones` | List authoritative zones |
| `records <zone>` | List records in a zone |
| `record add    <zone> <name> <type> <value> [--ttl N] [--overwrite]` | Add A/AAAA/CNAME/NS/PTR/TXT/DNAME |
| `record delete <zone> <name> <type> <value>` | Delete the matching record |
| `stats [LastHour\|LastDay\|LastWeek\|LastMonth\|LastYear]` | Dashboard query stats |
| `top <TopClients\|TopDomains\|TopBlockedDomains> [period] [--limit N]` | Top stats |
| `logs [--client IP] [--qname D] [--qtype T] [--response R] [--start T] [--end T] [--page N]` | Query DNS query logs |
| `cache list [--domain D]` / `cache flush` / `cache delete <d>` | Resolver cache |
| `allowed` / `blocked` | View allow/block list entries |
| `resolve <domain> [type] [--server S] [--protocol P]` | Ad-hoc resolve from the server |

Append `--json` to any read operation for raw JSON.

If `$ARGUMENTS` is empty or unrecognized, show the table above and stop.

## Execution

```bash
uv run C:/Users/Blake/.claude/skills/technitium-api/scripts/technitium.py $ARGUMENTS
```

Examples:
```bash
uv run .../technitium.py leases --sort ip
uv run .../technitium.py leases --scope HomeLan --json
uv run .../technitium.py zones
uv run .../technitium.py records lan
uv run .../technitium.py record add lan printer.lan A 192.168.0.42 --ttl 300
uv run .../technitium.py stats LastDay
uv run .../technitium.py top TopBlockedDomains LastDay --limit 20
uv run .../technitium.py logs --client 192.168.0.42 --per-page 100
uv run .../technitium.py resolve dns-vm.lan A
```

## Output interpretation

- **`leases`** is the workhorse for "what's on my network?" — match hostnames to
  MAC vendor / IP. Devices with `hostName: null` are unidentified clients; the
  MAC prefix (first 3 octets) is an OUI you can look up. Reserved entries are
  intentional pins; dynamic are leased from the pool.
- **`zones`** and **`records lan`**: the `lan` zone is managed declaratively
  from `homelab-services/services/dns/terraform-config/`. If the user wants to
  add a permanent record, point them at the Terraform variable map (see the
  `project_homelab_dns` memory) instead of using `record add` here — `record
  add` is for one-off / debugging records that Terraform would otherwise drift
  on the next apply.
- **`stats`** and **`top`**: useful for "is the DNS server healthy?" and "what's
  generating the most traffic?".
- **`logs`**: requires the built-in "Query Logs (Sqlite)" app to be installed
  (it is, by default). If the call returns "app not found", the user has
  renamed or uninstalled it — pass `--app` and `--class`.
- **Mutating ops** (`lease *`, `record *`, `cache flush`, `cache delete`):
  Report success plainly. On error, surface the `errorMessage` from the API.

## Gotchas

### A new PUBLIC DNS record can look "broken" until Technitium's negative cache expires
Blake's workstation (and any LAN device) resolves through this server (192.168.0.250).
When a brand-new public record is created (a fresh Cloudflare A/CNAME, an Azure
custom-domain binding, etc.), Technitium may still be holding a cached NODATA /
NXDOMAIN answer it fetched BEFORE the record existed, so the name fails locally
even though it is live on the internet. The negative TTL is the authoritative
zone's SOA minimum (Cloudflare = 1800s / 30 min), so it self-heals within ~30 min.
- Symptom: `curl`/browser says "could not resolve host" or returns the wrong/old
  answer on the LAN, but `Resolve-DnsName <name> -Server 8.8.8.8` returns the
  correct record. The split between local resolver and 8.8.8.8 is the tell.
- Confirm it: `cache list --domain <name>` shows a record whose
  `rData.dataType` is `DnsSpecialCacheRecordData` with `NegativeCache: ...`.
- Fix: `cache delete <name>` (surgical, preferred) or `cache flush` (nukes the
  whole resolver cache). Then `resolve <name> A` to verify a real answer returns.
  Tell the user other LAN devices clear on their own once the 30-min TTL lapses.

## Exit codes

| Code | Meaning | Action |
|------|---------|--------|
| 0 | Success | — |
| 1 | Usage error or missing credentials | Show setup instructions |
| 2 | Connection failure | Verify `TECHNITIUM_HOST` and that dns-vm.lan is reachable |
| 3 | Not found | (reserved) |
| 4 | API error (incl. `invalid-token`) | Surface the message; re-mint token if expired |
| 5 | Missing `uv` or dependency error | Tell user to install `uv` |

## Reference files

- `references/setup.md` — how to mint a Technitium API token in the UI
- `references/api-reference.md` — endpoint cheatsheet + link to upstream APIDOCS.md

## Related

- `homelab-bootstrap` skill — broader homelab gotchas
- `infisical-cli` skill — fetching `/technitium/` secrets
- Memory: `project_homelab_dns`, `feedback_technitium_provider_env_direct`
