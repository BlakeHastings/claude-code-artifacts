# /// script
# requires-python = ">=3.9"
# dependencies = ["proxmoxer", "requests", "keyring", "click"]
# ///
"""
proxmox.py — Proxmox VE REST API CLI

Credentials are resolved in order: environment variable → keyring → error.

Environment variables:
  PROXMOX_HOST          Hostname or IP (no https://, port optional, default 8006)
  PROXMOX_TOKEN_ID      API token ID: user@realm!tokenname
  PROXMOX_TOKEN_SECRET  UUID secret
  PROXMOX_API_TOKEN     Combined: user@realm!tokenname=uuid  (alternative)

Keyring service: proxmox-api
  Keys: host, token_id, token_secret

Usage:
  proxmox.py configure [--host HOST] [--token-id ID] [--token-secret SECRET]
  proxmox.py nodes      [--json]
  proxmox.py vms        [--json]
  proxmox.py vm status   <vmid>  [--json]
  proxmox.py vm start    <vmid>
  proxmox.py vm stop     <vmid>
  proxmox.py vm shutdown <vmid>
  proxmox.py vm delete   <vmid>
  proxmox.py nextid     [--json]
  proxmox.py storage    [--json]
  proxmox.py resources  [--json]
"""
from __future__ import annotations

import getpass
import json
import os
import sys

import click

KEYRING_SERVICE = "proxmox-api"

# ── Exit codes ──────────────────────────────────────────────────────────────────

EXIT_OK        = 0
EXIT_USAGE     = 1
EXIT_CONNECT   = 2
EXIT_NOT_FOUND = 3
EXIT_API_ERROR = 4
EXIT_DEP_ERROR = 5


# ── Credential resolution ────────────────────────────────────────────────────────

def _keyring_get(key: str) -> str:
    try:
        import keyring as kr
        value = kr.get_password(KEYRING_SERVICE, key)
        return value or ""
    except Exception:
        return ""


def _keyring_set(key: str, value: str) -> None:
    import keyring as kr
    kr.set_password(KEYRING_SERVICE, key, value)


def resolve_credentials() -> tuple[str, str, str]:
    """Returns (host, token_id, token_secret). Resolution order: env var → keyring → error."""
    combined = os.environ.get("PROXMOX_API_TOKEN", "").strip()
    env_token_id = ""
    env_token_secret = ""
    if combined and "=" in combined:
        env_token_id, env_token_secret = combined.split("=", 1)

    host         = (os.environ.get("PROXMOX_HOST", "").strip()         or _keyring_get("host"))
    token_id     = (os.environ.get("PROXMOX_TOKEN_ID", "").strip()     or env_token_id     or _keyring_get("token_id"))
    token_secret = (os.environ.get("PROXMOX_TOKEN_SECRET", "").strip() or env_token_secret or _keyring_get("token_secret"))

    for prefix in ("https://", "http://"):
        if host.lower().startswith(prefix):
            host = host[len(prefix):]

    missing = []
    if not host:
        missing.append("PROXMOX_HOST")
    if not token_id:
        missing.append("PROXMOX_TOKEN_ID  (or PROXMOX_API_TOKEN)")
    if not token_secret:
        missing.append("PROXMOX_TOKEN_SECRET  (or PROXMOX_API_TOKEN)")

    if missing:
        raise click.ClickException(
            "Credentials not found. Missing:\n"
            + "\n".join(f"  {v}" for v in missing)
            + "\n\nRun configure to store credentials in your keyring:\n"
            "  uv run .claude/skills/proxmox-api/scripts/proxmox.py configure\n\n"
            "Or set the environment variables listed above.\n"
            "See references/setup.md for how to create a Proxmox API token."
        )

    return host, token_id, token_secret


# ── Proxmox client factory ───────────────────────────────────────────────────────

def make_client():
    try:
        from proxmoxer import ProxmoxAPI
    except ImportError:
        raise click.ClickException(
            "proxmoxer is not installed.\n"
            "Install uv and run the script via: uv run proxmox.py\n"
            "uv will install dependencies automatically."
        )

    host, token_id, token_secret = resolve_credentials()

    port = 8006
    if ":" in host:
        host, port_str = host.rsplit(":", 1)
        try:
            port = int(port_str)
        except ValueError:
            raise click.BadParameter(f"Invalid port in host: {port_str!r}")

    if "!" not in token_id:
        raise click.ClickException(
            f"PROXMOX_TOKEN_ID must be in format user@realm!tokenname, got: {token_id!r}"
        )

    user_part, token_name = token_id.split("!", 1)

    try:
        client = ProxmoxAPI(
            host, port=port,
            user=user_part, token_name=token_name, token_value=token_secret,
            verify_ssl=False,
        )
        client.version.get()
        return client
    except Exception as exc:
        _handle_connection_error(exc)


def _handle_connection_error(exc: Exception) -> None:
    msg = str(exc)
    if any(x in msg for x in ("401", "403", "Unauthorized", "forbidden")):
        raise click.ClickException(
            "Authentication failed.\n"
            "Check that PROXMOX_TOKEN_ID and PROXMOX_TOKEN_SECRET are correct\n"
            "and that the token has appropriate permissions."
        )
    elif any(x in msg for x in ("Connection refused", "ConnectionRefused")):
        raise click.ClickException(
            "Connection refused. Verify PROXMOX_HOST is correct\n"
            "and the Proxmox API is reachable on port 8006."
        )
    elif any(x in msg for x in ("Name or service not known", "getaddrinfo", "nodename")):
        raise click.ClickException("Host not found. Check that PROXMOX_HOST is correct.")
    elif "timed out" in msg.lower():
        raise click.ClickException("Connection timed out. Verify PROXMOX_HOST is reachable.")
    else:
        raise click.ClickException(f"Could not connect to Proxmox API: {exc}")


# ── Output helpers ───────────────────────────────────────────────────────────────

def print_table(headers: list[str], rows: list[list]) -> None:
    if not rows:
        click.echo("(no results)")
        return
    col_widths = [len(h) for h in headers]
    for row in rows:
        for i, cell in enumerate(row):
            col_widths[i] = max(col_widths[i], len(str(cell)))
    fmt = "  ".join(f"{{:<{w}}}" for w in col_widths)
    separator = "  ".join("-" * w for w in col_widths)
    click.echo(fmt.format(*headers))
    click.echo(separator)
    for row in rows:
        click.echo(fmt.format(*[str(c) for c in row]))


def fmt_bytes(value) -> str:
    try:
        b = float(value)
    except (TypeError, ValueError):
        return str(value)
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if b < 1024:
            return f"{b:.1f} {unit}"
        b /= 1024
    return f"{b:.1f} PB"


def fmt_uptime(seconds) -> str:
    try:
        s = int(seconds)
    except (TypeError, ValueError):
        return str(seconds)
    d, rem = divmod(s, 86400)
    h, rem = divmod(rem, 3600)
    m, sec = divmod(rem, 60)
    if d:
        return f"{d}d {h:02d}:{m:02d}:{sec:02d}"
    return f"{h:02d}:{m:02d}:{sec:02d}"


def fmt_pct(value) -> str:
    try:
        return f"{float(value) * 100:.1f}%"
    except (TypeError, ValueError):
        return str(value)


def _find_vm(client, vmid: str) -> tuple[str, dict] | None:
    """Locate a VM by VMID across all nodes. Returns (node, vm_info) or None."""
    try:
        resources = client.cluster.resources.get(type="vm")
    except Exception as exc:
        raise click.ClickException(f"Failed to query cluster resources: {exc}")

    try:
        vmid_int = int(vmid)
    except ValueError:
        raise click.BadParameter(f"VMID must be an integer, got: {vmid!r}", param_hint="VMID")

    for vm in resources:
        if vm.get("vmid") == vmid_int:
            return vm.get("node", ""), vm
    return None


# ── CLI ──────────────────────────────────────────────────────────────────────────

@click.group()
def cli():
    """Proxmox VE REST API CLI."""
    pass


# ── configure ────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--host",          default=None, help="Proxmox hostname or IP")
@click.option("--token-id",      default=None, help="API token ID (user@realm!tokenname)")
@click.option("--token-secret",  default=None, help="API token secret UUID")
def configure(host, token_id, token_secret):
    """Store credentials in system keyring. Run this yourself — do not invoke via agent."""
    click.echo("Proxmox API credential setup")
    click.echo("Credentials will be stored in your system keyring.\n")

    host         = host         or input("Proxmox host (IP or hostname, e.g. 192.168.1.10): ").strip()
    token_id     = token_id     or input("Token ID (user@realm!tokenname, e.g. root@pam!claude): ").strip()
    token_secret = token_secret or getpass.getpass("Token secret (UUID): ").strip()

    if not host or not token_id or not token_secret:
        raise click.ClickException("All three values are required.")

    for prefix in ("https://", "http://"):
        if host.lower().startswith(prefix):
            host = host[len(prefix):]

    _keyring_set("host", host)
    _keyring_set("token_id", token_id)
    _keyring_set("token_secret", token_secret)

    click.echo("\nCredentials saved to keyring.")
    click.echo("Verifying connectivity...\n")

    try:
        from proxmoxer import ProxmoxAPI
        port = 8006
        if ":" in host:
            host, port_str = host.rsplit(":", 1)
            port = int(port_str)
        user_part, token_name = token_id.split("!", 1) if "!" in token_id else (token_id, "")
        client = ProxmoxAPI(host, port=port, user=user_part, token_name=token_name,
                            token_value=token_secret, verify_ssl=False)
        nodes = client.nodes.get()
        click.echo(f"Connected. Found {len(nodes)} node(s):")
        for n in nodes:
            click.echo(f"  {n.get('node')}  ({n.get('status', '?')})")
    except Exception as exc:
        click.echo(f"\nWARNING: Credentials saved but connectivity check failed: {exc}", err=True)
        click.echo("Verify your host and token, or check network access.", err=True)
        sys.exit(EXIT_CONNECT)


# ── nodes ─────────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--json", "as_json", is_flag=True, help="Output raw JSON")
def nodes(as_json):
    """List all cluster nodes."""
    client = make_client()
    try:
        result = client.nodes.get()
    except Exception as exc:
        raise click.ClickException(f"Failed to list nodes: {exc}")

    if as_json:
        click.echo(json.dumps(result, indent=2, default=str))
        return

    headers = ["NODE", "STATUS", "CPU%", "MEM USED", "MEM TOTAL", "UPTIME"]
    rows = [
        [
            n.get("node", "?"), n.get("status", "?"),
            fmt_pct(n.get("cpu", 0)), fmt_bytes(n.get("mem", 0)),
            fmt_bytes(n.get("maxmem", 0)), fmt_uptime(n.get("uptime", 0)),
        ]
        for n in sorted(result, key=lambda x: x.get("node", ""))
    ]
    print_table(headers, rows)


# ── vms ───────────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--json", "as_json", is_flag=True, help="Output raw JSON")
def vms(as_json):
    """List all VMs across the cluster."""
    client = make_client()
    try:
        result = client.cluster.resources.get(type="vm")
    except Exception as exc:
        raise click.ClickException(f"Failed to list VMs: {exc}")

    if as_json:
        click.echo(json.dumps(result, indent=2, default=str))
        return

    headers = ["VMID", "NAME", "STATUS", "NODE", "CPU%", "MEM USED", "MEM TOTAL"]
    rows = [
        [
            vm.get("vmid", "?"), vm.get("name", "?"), vm.get("status", "?"),
            vm.get("node", "?"), fmt_pct(vm.get("cpu", 0)),
            fmt_bytes(vm.get("mem", 0)), fmt_bytes(vm.get("maxmem", 0)),
        ]
        for vm in sorted(result, key=lambda x: int(x.get("vmid", 0)))
    ]
    print_table(headers, rows)


# ── vm ────────────────────────────────────────────────────────────────────────────

@cli.group()
def vm():
    """VM operations: status, start, stop, shutdown, delete."""
    pass


@vm.command()
@click.argument("vmid")
@click.option("--json", "as_json", is_flag=True, help="Output raw JSON")
def status(vmid, as_json):
    """Show detailed status for a VM."""
    client = make_client()
    result = _find_vm(client, vmid)
    if result is None:
        raise click.ClickException(f"VM {vmid} not found.")

    node, vm_info = result
    try:
        s = client.nodes(node).qemu(vmid).status.current.get()
    except Exception as exc:
        raise click.ClickException(f"Failed to get VM status: {exc}")

    if as_json:
        click.echo(json.dumps(s, indent=2, default=str))
        return

    name   = s.get("name", vm_info.get("name", "?"))
    cpus   = s.get("cpus", "?")
    width  = 52
    click.echo(f"{name}  (VMID {vmid})")
    click.echo("─" * width)
    click.echo(f"  Status:   {s.get('status', '?')}")
    click.echo(f"  Node:     {node}")
    click.echo(f"  CPU:      {fmt_pct(s.get('cpu', 0))}  ({cpus} vCPU{'s' if int(cpus or 1) != 1 else ''})")
    click.echo(f"  Memory:   {fmt_bytes(s.get('mem', 0))} / {fmt_bytes(s.get('maxmem', 0))}")
    click.echo(f"  Uptime:   {fmt_uptime(s.get('uptime', 0))}")


@vm.command()
@click.argument("vmid")
def start(vmid):
    """Start a VM."""
    client = make_client()
    result = _find_vm(client, vmid)
    if result is None:
        raise click.ClickException(f"VM {vmid} not found.")

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "running":
        click.echo(f"VM {vmid} ({name}) is already running.")
        return

    try:
        task = client.nodes(node).qemu(vmid).status.start.post()
        click.echo(f"Start queued for VM {vmid} ({name}). Task: {task}")
    except Exception as exc:
        raise click.ClickException(f"Failed to start VM {vmid}: {exc}")


@vm.command()
@click.argument("vmid")
def stop(vmid):
    """Immediately stop a VM (hard power-off)."""
    client = make_client()
    result = _find_vm(client, vmid)
    if result is None:
        raise click.ClickException(f"VM {vmid} not found.")

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "stopped":
        click.echo(f"VM {vmid} ({name}) is already stopped.")
        return

    try:
        task = client.nodes(node).qemu(vmid).status.stop.post()
        click.echo(f"Stop queued for VM {vmid} ({name}). Task: {task}")
    except Exception as exc:
        raise click.ClickException(f"Failed to stop VM {vmid}: {exc}")


@vm.command()
@click.argument("vmid")
def shutdown(vmid):
    """Gracefully shut down a VM via ACPI."""
    client = make_client()
    result = _find_vm(client, vmid)
    if result is None:
        raise click.ClickException(f"VM {vmid} not found.")

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "stopped":
        click.echo(f"VM {vmid} ({name}) is already stopped.")
        return

    try:
        task = client.nodes(node).qemu(vmid).status.shutdown.post()
        click.echo(f"Shutdown (ACPI) queued for VM {vmid} ({name}). Task: {task}")
    except Exception as exc:
        raise click.ClickException(f"Failed to shut down VM {vmid}: {exc}")


@vm.command()
@click.argument("vmid")
def delete(vmid):
    """Permanently delete a VM and its disks."""
    client = make_client()
    result = _find_vm(client, vmid)
    if result is None:
        raise click.ClickException(f"VM {vmid} not found.")

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "running":
        raise click.ClickException(f"VM {vmid} ({name}) is running. Stop it first.")

    try:
        task = client.nodes(node).qemu(vmid).delete(purge=1)
        click.echo(f"Delete queued for VM {vmid} ({name}). Task: {task}")
    except Exception as exc:
        raise click.ClickException(f"Failed to delete VM {vmid}: {exc}")


# ── nextid ────────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--json", "as_json", is_flag=True, help="Output raw JSON")
def nextid(as_json):
    """Get the next available VMID."""
    client = make_client()
    try:
        result = client.cluster.nextid.get()
    except Exception as exc:
        raise click.ClickException(f"Failed to get next VMID: {exc}")

    if as_json:
        click.echo(json.dumps({"nextid": result}, indent=2))
    else:
        click.echo(f"Next available VMID: {result}")


# ── storage ───────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--json", "as_json", is_flag=True, help="Output raw JSON")
def storage(as_json):
    """List storage pools."""
    client = make_client()
    try:
        result = client.storage.get()
    except Exception as exc:
        raise click.ClickException(f"Failed to list storage: {exc}")

    if as_json:
        click.echo(json.dumps(result, indent=2, default=str))
        return

    headers = ["STORAGE", "TYPE", "CONTENT", "SHARED", "ACTIVE"]
    rows = [
        [
            p.get("storage", "?"), p.get("type", "?"),
            p.get("content", "").replace(",", " "),
            "yes" if p.get("shared", 0) else "no",
            "yes" if p.get("active", p.get("enabled", 1)) else "no",
        ]
        for p in sorted(result, key=lambda x: x.get("storage", ""))
    ]
    print_table(headers, rows)


# ── resources ─────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--json", "as_json", is_flag=True, help="Output raw JSON")
def resources(as_json):
    """List all cluster resources."""
    client = make_client()
    try:
        result = client.cluster.resources.get()
    except Exception as exc:
        raise click.ClickException(f"Failed to list cluster resources: {exc}")

    if as_json:
        click.echo(json.dumps(result, indent=2, default=str))
        return

    headers = ["TYPE", "ID", "NAME", "STATUS", "NODE"]
    rows = [
        [
            r.get("type", "?"), r.get("id", r.get("vmid", "?")),
            r.get("name", ""), r.get("status", "?"), r.get("node", ""),
        ]
        for r in sorted(result, key=lambda x: (x.get("type", ""), str(x.get("id", ""))))
    ]
    print_table(headers, rows)


# ── Entry point ──────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    cli()
