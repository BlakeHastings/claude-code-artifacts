# proxmox-api

A Claude Code skill for interacting with the [Proxmox VE](https://www.proxmox.com/en/proxmox-virtual-environment) REST API. List VMs, check status, start/stop machines, browse storage, and find the next available VMID — all from within your Claude Code session.

---

## Prerequisites

- [uv](https://docs.astral.sh/uv/getting-started/installation/) must be installed. It handles Python and all dependencies automatically — no separate `pip install` needed.
- A Proxmox VE instance (v7+) reachable on your network.
- A Proxmox API token (see setup below).

---

## Installation

Copy the `proxmox-api` directory into your Claude Code skills folder:

```bash
# Global (available in all projects)
cp -r proxmox-api ~/.claude/skills/proxmox-api

# Or project-level (available in one repo)
cp -r proxmox-api .claude/skills/proxmox-api
```

---

## Setup

### Step 1 — Create a Proxmox API Token

API tokens are the recommended authentication method. They are scoped to a user and can have their own permission set.

#### Via the Proxmox Web UI

1. Log in to your Proxmox web interface: `https://<your-proxmox-ip>:8006`
2. Go to **Datacenter** → **Permissions** → **API Tokens**
3. Click **Add**
4. Fill in:
   - **User**: the Proxmox user the token belongs to (e.g., `root@pam`)
   - **Token ID**: a short name for this token (e.g., `claude`)
   - **Privilege Separation**: leave checked (recommended) — assign roles explicitly in the next step
5. Click **Add**

> **Important:** The token secret UUID is shown **only once**. Copy it before closing the dialog.

#### Via CLI (SSH into the Proxmox node)

```bash
# Create token for root@pam named "claude"
pveum user token add root@pam claude --privsep 0
```

The output includes the secret UUID — copy it.

---

### Step 2 — Assign Permissions

If you created the token with **Privilege Separation** enabled, assign a role.

**Read-only** (list VMs, nodes, storage, get next VMID):
```bash
pveum acl modify / --token 'root@pam!claude' --role PVEAuditor
```

**Read + start/stop VMs**:
```bash
pveum acl modify / --token 'root@pam!claude' --role PVEVMUser
```

**Full access**:
```bash
pveum acl modify / --token 'root@pam!claude' --role Administrator
```

The `/` path applies the role at the datacenter level, covering all nodes and VMs.

| Operation | Minimum Role |
|-----------|-------------|
| `nodes`, `vms`, `resources`, `storage`, `nextid`, `vm status` | `PVEAuditor` |
| `vm start`, `vm stop`, `vm shutdown` | `PVEVMUser` |

---

### Step 3 — Configure Credentials

**Run this yourself** in your terminal — do not ask Claude to run it. Credentials must be entered by you directly so they never appear in conversation logs.

In a Claude Code session, use the `!` prefix to run in-session:

```
! uv run ~/.claude/skills/proxmox-api/scripts/proxmox.py configure
```

Or in a regular terminal:

```bash
uv run ~/.claude/skills/proxmox-api/scripts/proxmox.py configure
```

You will be prompted for:
- **Proxmox host** — IP or hostname, no `https://` prefix (e.g., `192.168.1.10` or `pve.local`)
- **Token ID** — format: `user@realm!tokenname` (e.g., `root@pam!claude`)
- **Token secret** — the UUID from Step 1

Credentials are stored in your **system keyring** and never written to disk or logs. After saving, the command verifies connectivity by listing your cluster nodes.

#### Override with Environment Variables

Environment variables take precedence over keyring values — useful for CI or switching between Proxmox instances:

```bash
# Separate variables
export PROXMOX_HOST="192.168.1.10"
export PROXMOX_TOKEN_ID="root@pam!claude"
export PROXMOX_TOKEN_SECRET="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"

# Or as a combined string
export PROXMOX_HOST="192.168.1.10"
export PROXMOX_API_TOKEN="root@pam!claude=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
```

---

## Usage

Once credentials are configured, use the skill in Claude Code:

```
/proxmox-api nodes
/proxmox-api vms
/proxmox-api vm status 100
/proxmox-api vm start 101
/proxmox-api vm shutdown 101
/proxmox-api nextid
/proxmox-api storage
/proxmox-api resources
/proxmox-api vms --json
```

### Available Commands

| Command | Description |
|---------|-------------|
| `nodes` | List all cluster nodes with CPU, memory, and uptime |
| `vms` | List all VMs across the cluster |
| `vm status <vmid>` | Detailed status for a single VM |
| `vm start <vmid>` | Start a VM |
| `vm stop <vmid>` | Immediately stop a VM (hard power off) |
| `vm shutdown <vmid>` | Graceful ACPI shutdown |
| `nextid` | Get the next available VMID |
| `storage` | List storage pools |
| `resources` | List all cluster resources (VMs, nodes, storage) |

Append `--json` to any command for raw JSON output.

---

## Notes

- **Self-signed TLS**: Proxmox uses a self-signed certificate by default. The script disables certificate verification (`verify_ssl=False`) so connections work out of the box. If you configure a valid certificate via Proxmox's built-in ACME/Let's Encrypt support, connections continue to work.
- **Host format**: Do not include `https://`. Port defaults to `8006`. To use a different port: `192.168.1.10:8007`.
- **uv handles dependencies**: `proxmoxer`, `requests`, and `keyring` are declared as inline script dependencies and installed automatically by `uv run` in an isolated environment.
