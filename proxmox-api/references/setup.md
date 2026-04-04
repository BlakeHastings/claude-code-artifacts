# Proxmox API Token Setup

This guide explains how to create a Proxmox VE API token and configure credentials
for the `proxmox-api` skill.

---

## 1. Create a Proxmox API Token

### Via the Web UI

1. Log in to the Proxmox web interface at `https://<host>:8006`
2. Navigate to **Datacenter** → **Permissions** → **API Tokens**
3. Click **Add**
4. Fill in:
   - **User**: the user account the token belongs to (e.g., `root@pam`)
   - **Token ID**: a short name for the token (e.g., `claude`)
   - **Privilege Separation**: uncheck to inherit all user permissions, or leave checked and assign roles explicitly (recommended)
5. Click **Add** — the token secret UUID is shown **once**. Copy it immediately.

### Via CLI (on a Proxmox node)

```bash
# Create token for root@pam with token ID "claude" (privilege separation disabled)
pveum user token add root@pam claude --privsep 0

# Output includes the secret UUID — copy it now
```

---

## 2. Assign Permissions (if Privilege Separation is enabled)

If you created the token with **Privilege Separation** enabled, assign roles explicitly.

**Read-only access** (sufficient for `nodes`, `vms`, `vm status`, `nextid`, `storage`, `resources`):
```bash
pveum acl modify / --token 'root@pam!claude' --role PVEAuditor
```

**Read + VM start/stop access**:
```bash
pveum acl modify / --token 'root@pam!claude' --role PVEVMUser
```

**Full administrative access**:
```bash
pveum acl modify / --token 'root@pam!claude' --role Administrator
```

Assigning at `/` (datacenter root) applies the role to all nodes and VMs.

---

## 3. Configure Credentials

Run the configure command **yourself in your terminal** — do not ask the agent to run it, as this would expose credentials in the conversation log.

```bash
! uv run .claude/skills/proxmox-api/scripts/proxmox.py configure
```

The `!` prefix runs the command in-session in Claude Code. You will be prompted for:
- **Proxmox host**: IP address or hostname (e.g., `192.168.1.10` or `pve.local`)
- **Token ID**: in the format `user@realm!tokenname` (e.g., `root@pam!claude`)
- **Token secret**: the UUID you copied when creating the token

Credentials are stored in your system keyring and never written to disk or logs.

### Override with Environment Variables

If you need to override keyring credentials (e.g., in CI or for a different Proxmox instance), set environment variables before running:

```bash
# Separate variables
export PROXMOX_HOST="192.168.1.10"
export PROXMOX_TOKEN_ID="root@pam!claude"
export PROXMOX_TOKEN_SECRET="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"

# Or combined
export PROXMOX_HOST="192.168.1.10"
export PROXMOX_API_TOKEN="root@pam!claude=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
```

Environment variables always take precedence over keyring values.

---

## 4. Notes on Host Format

- **Do not include `https://`** — the script adds it automatically.
- **Port 8006 is the default.** To use a different port: `192.168.1.10:8007`
- Both IPs and hostnames work: `192.168.1.10`, `pve`, `pve.local`

---

## 5. TLS / Self-Signed Certificates

Proxmox ships with a self-signed certificate by default. The script sets `verify_ssl=False`
so connections succeed without a trusted CA. If you later configure a valid certificate
(e.g., via Proxmox's built-in Let's Encrypt ACME support), connections continue to work.

---

## 6. Minimal Permissions Reference

| Operation | Minimum Role |
|-----------|-------------|
| `nodes`, `vms`, `resources`, `storage`, `nextid` | `PVEAuditor` |
| `vm status` | `PVEAuditor` |
| `vm start`, `vm stop`, `vm shutdown` | `PVEVMUser` |
| All operations | `Administrator` |
