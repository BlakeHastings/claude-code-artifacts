---
name: proxmox-api
description: >-
  Interact with the Proxmox VE REST API. Use when the user wants to list VMs,
  check VM status, start or stop a VM, view cluster nodes, browse storage pools,
  get the next available VMID, or query Proxmox cluster resources. Handles API
  token authentication, self-signed TLS, and tabular or JSON output.
argument-hint: "<configure|nodes|vms|vm|nextid|storage|resources> [args]"
allowed-tools: Bash(uv *), Read
---

# Proxmox API

Use `scripts/proxmox.py` (via `uv run`) to interact with the Proxmox VE REST API.

## Credentials

Credentials are read in this order: **environment variable → keyring → error**.

| Env var | Keyring key | Description |
|---------|-------------|-------------|
| `PROXMOX_HOST` | `host` | Hostname or IP (no `https://`, port optional) |
| `PROXMOX_TOKEN_ID` | `token_id` | API token ID: `user@realm!tokenname` |
| `PROXMOX_TOKEN_SECRET` | `token_secret` | UUID secret |
| `PROXMOX_API_TOKEN` | — | Combined: `user@realm!tokenname=uuid` (env only) |

Keyring entries use service name `proxmox-api`.

### First-time setup

**NEVER run `configure` yourself — credentials must be entered by the user directly so they do not appear in conversation logs.**

If credentials are missing, tell the user to run this command themselves in their terminal (using `!` to run it in-session):

```
! uv run .claude/skills/proxmox-api/scripts/proxmox.py configure
```

The `configure` command prompts interactively for host, token ID, and token secret, stores them in the system keyring, then verifies connectivity. Direct the user to `references/setup.md` for how to create a Proxmox API token before running configure.

## Argument Routing

Parse `$ARGUMENTS`. The first token selects the operation:

| Arguments | CLI invocation | Description |
|-----------|----------------|-------------|
| `nodes` | `uv run ... nodes` | List all cluster nodes |
| `vms` | `uv run ... vms` | List all VMs across cluster |
| `vm status <vmid>` | `uv run ... vm status <vmid>` | Detailed VM status |
| `vm start <vmid>` | `uv run ... vm start <vmid>` | Start a VM |
| `vm stop <vmid>` | `uv run ... vm stop <vmid>` | Immediate stop |
| `vm shutdown <vmid>` | `uv run ... vm shutdown <vmid>` | ACPI graceful shutdown |
| `vm delete <vmid>` | `uv run ... vm delete <vmid>` | Delete a VM (stop first if running) |
| `nextid` | `uv run ... nextid` | Next available VMID |
| `storage` | `uv run ... storage` | List storage pools |
| `resources` | `uv run ... resources` | All cluster resources |

Append `--json` to any command for raw JSON output.

If `$ARGUMENTS` is empty or unrecognized, show the table above and stop.

## Execution

Run from the repo root:

```bash
uv run .claude/skills/proxmox-api/scripts/proxmox.py $ARGUMENTS
```

Examples:
```bash
uv run .claude/skills/proxmox-api/scripts/proxmox.py nodes
uv run .claude/skills/proxmox-api/scripts/proxmox.py vms
uv run .claude/skills/proxmox-api/scripts/proxmox.py vm status 100
uv run .claude/skills/proxmox-api/scripts/proxmox.py nextid
uv run .claude/skills/proxmox-api/scripts/proxmox.py resources --json
```

## Output Interpretation

- **List operations** (`nodes`, `vms`, `storage`, `resources`): Summarize key facts. Call out anomalies — stopped VMs that may need attention, storage pools near capacity, nodes in unknown state.
- **`vm status <vmid>`**: Describe the VM's name, state, CPU, memory, and uptime in a natural sentence.
- **`nextid`**: State the VMID and confirm it is safe to use when creating a new VM in Terraform.
- **Mutating operations** (`vm start`, `vm stop`, `vm shutdown`, `vm delete`): Report success or failure clearly. On non-zero exit, explain the error.
- **`vm delete <vmid>`**: Confirm the task ID returned by Proxmox. The deletion is async — the VM disappears within a few seconds.

## Exit Codes

| Code | Meaning | Action |
|------|---------|--------|
| 0 | Success | — |
| 1 | Usage error or missing credentials | Show credential setup instructions |
| 2 | Connection failure | Verify `PROXMOX_HOST` and network access |
| 3 | VM not found | Confirm the VMID with `/proxmox-api vms` |
| 4 | API error | Show the error message from the script |
| 5 | Missing `uv` or dependency error | Tell user to install `uv` |

## Reference Files

- `references/setup.md` — How to create a Proxmox API token and configure credentials
