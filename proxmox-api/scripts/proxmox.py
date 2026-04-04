# /// script
# requires-python = ">=3.9"
# dependencies = ["proxmoxer", "requests", "keyring"]
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
  proxmox.py vm <status|start|stop|shutdown> <vmid>  [--json]
  proxmox.py nextid     [--json]
  proxmox.py storage    [--json]
  proxmox.py resources  [--json]
"""
from __future__ import annotations

import argparse
import getpass
import json
import os
import sys

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
    """
    Returns (host, token_id, token_secret).
    Resolution order: env var → keyring → error.
    """
    # Check combined env var first
    combined = os.environ.get("PROXMOX_API_TOKEN", "").strip()
    env_token_id = ""
    env_token_secret = ""
    if combined and "=" in combined:
        env_token_id, env_token_secret = combined.split("=", 1)

    host         = (os.environ.get("PROXMOX_HOST", "").strip()
                    or _keyring_get("host"))
    token_id     = (os.environ.get("PROXMOX_TOKEN_ID", "").strip()
                    or env_token_id
                    or _keyring_get("token_id"))
    token_secret = (os.environ.get("PROXMOX_TOKEN_SECRET", "").strip()
                    or env_token_secret
                    or _keyring_get("token_secret"))

    # Strip protocol prefix if user included it
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
        print(
            "ERROR: Credentials not found. Missing:\n"
            + "\n".join(f"  {v}" for v in missing)
            + "\n\nRun configure to store credentials in your keyring:\n"
            "  uv run .claude/skills/proxmox-api/scripts/proxmox.py configure\n\n"
            "Or set the environment variables listed above.\n"
            "See references/setup.md for how to create a Proxmox API token.",
            file=sys.stderr,
        )
        sys.exit(EXIT_USAGE)

    return host, token_id, token_secret


# ── Proxmox client factory ───────────────────────────────────────────────────────

def make_client():
    try:
        from proxmoxer import ProxmoxAPI
    except ImportError:
        print(
            "ERROR: proxmoxer is not installed.\n"
            "Install uv and run the script via: uv run proxmox.py\n"
            "uv will install dependencies automatically.",
            file=sys.stderr,
        )
        sys.exit(EXIT_DEP_ERROR)

    host, token_id, token_secret = resolve_credentials()

    port = 8006
    if ":" in host:
        host, port_str = host.rsplit(":", 1)
        try:
            port = int(port_str)
        except ValueError:
            print(f"ERROR: Invalid port in host: {port_str!r}", file=sys.stderr)
            sys.exit(EXIT_USAGE)

    # proxmoxer token auth: user=user@realm, token_name=tokenname, token_value=uuid
    if "!" in token_id:
        user_part, token_name = token_id.split("!", 1)
    else:
        print(
            f"ERROR: PROXMOX_TOKEN_ID must be in format user@realm!tokenname, got: {token_id!r}",
            file=sys.stderr,
        )
        sys.exit(EXIT_USAGE)

    try:
        client = ProxmoxAPI(
            host,
            port=port,
            user=user_part,
            token_name=token_name,
            token_value=token_secret,
            verify_ssl=False,
        )
        # Eagerly verify connectivity
        client.version.get()
        return client
    except Exception as exc:
        _handle_connection_error(exc)


def _handle_connection_error(exc: Exception) -> None:
    msg = str(exc)
    if any(x in msg for x in ("401", "403", "Unauthorized", "forbidden")):
        print(
            "ERROR: Authentication failed.\n"
            "Check that PROXMOX_TOKEN_ID and PROXMOX_TOKEN_SECRET are correct\n"
            "and that the token has appropriate permissions.",
            file=sys.stderr,
        )
    elif any(x in msg for x in ("Connection refused", "ConnectionRefused")):
        print(
            "ERROR: Connection refused. Verify PROXMOX_HOST is correct\n"
            "and the Proxmox API is reachable on port 8006.",
            file=sys.stderr,
        )
    elif any(x in msg for x in ("Name or service not known", "getaddrinfo", "nodename")):
        print(
            "ERROR: Host not found. Check that PROXMOX_HOST is correct.",
            file=sys.stderr,
        )
    elif "timed out" in msg.lower():
        print(
            "ERROR: Connection timed out. Verify PROXMOX_HOST is reachable.",
            file=sys.stderr,
        )
    else:
        print(f"ERROR: Could not connect to Proxmox API: {exc}", file=sys.stderr)
    sys.exit(EXIT_CONNECT)


# ── Output helpers ───────────────────────────────────────────────────────────────

def print_table(headers: list[str], rows: list[list]) -> None:
    if not rows:
        print("(no results)")
        return
    col_widths = [len(h) for h in headers]
    for row in rows:
        for i, cell in enumerate(row):
            col_widths[i] = max(col_widths[i], len(str(cell)))
    fmt = "  ".join(f"{{:<{w}}}" for w in col_widths)
    separator = "  ".join("-" * w for w in col_widths)
    print(fmt.format(*headers))
    print(separator)
    for row in rows:
        print(fmt.format(*[str(c) for c in row]))


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


# ── Command: configure ───────────────────────────────────────────────────────────

def cmd_configure(args) -> int:
    print("Proxmox API credential setup")
    print("Credentials will be stored in your system keyring.\n")

    host = args.host or input("Proxmox host (IP or hostname, e.g. 192.168.1.10): ").strip()
    token_id = args.token_id or input("Token ID (user@realm!tokenname, e.g. root@pam!claude): ").strip()
    token_secret = args.token_secret or getpass.getpass("Token secret (UUID): ").strip()

    if not host or not token_id or not token_secret:
        print("ERROR: All three values are required.", file=sys.stderr)
        return EXIT_USAGE

    # Strip protocol prefix
    for prefix in ("https://", "http://"):
        if host.lower().startswith(prefix):
            host = host[len(prefix):]

    _keyring_set("host", host)
    _keyring_set("token_id", token_id)
    _keyring_set("token_secret", token_secret)

    print("\nCredentials saved to keyring.")
    print("Verifying connectivity...\n")

    # Verify by listing nodes
    try:
        from proxmoxer import ProxmoxAPI
        port = 8006
        if ":" in host:
            host, port_str = host.rsplit(":", 1)
            port = int(port_str)
        user_part, token_name = token_id.split("!", 1) if "!" in token_id else (token_id, "")
        client = ProxmoxAPI(
            host, port=port,
            user=user_part, token_name=token_name, token_value=token_secret,
            verify_ssl=False,
        )
        nodes = client.nodes.get()
        print(f"Connected. Found {len(nodes)} node(s):")
        for n in nodes:
            print(f"  {n.get('node')}  ({n.get('status', '?')})")
        return EXIT_OK
    except Exception as exc:
        print(f"\nWARNING: Credentials saved but connectivity check failed: {exc}", file=sys.stderr)
        print("Verify your host and token, or check network access.", file=sys.stderr)
        return EXIT_CONNECT


# ── Command: nodes ───────────────────────────────────────────────────────────────

def cmd_nodes(client, args) -> int:
    try:
        nodes = client.nodes.get()
    except Exception as exc:
        print(f"ERROR: Failed to list nodes: {exc}", file=sys.stderr)
        return EXIT_API_ERROR

    if args.json:
        print(json.dumps(nodes, indent=2, default=str))
        return EXIT_OK

    headers = ["NODE", "STATUS", "CPU%", "MEM USED", "MEM TOTAL", "UPTIME"]
    rows = [
        [
            n.get("node", "?"),
            n.get("status", "?"),
            fmt_pct(n.get("cpu", 0)),
            fmt_bytes(n.get("mem", 0)),
            fmt_bytes(n.get("maxmem", 0)),
            fmt_uptime(n.get("uptime", 0)),
        ]
        for n in sorted(nodes, key=lambda x: x.get("node", ""))
    ]
    print_table(headers, rows)
    return EXIT_OK


# ── Command: vms ─────────────────────────────────────────────────────────────────

def cmd_vms(client, args) -> int:
    try:
        resources = client.cluster.resources.get(type="vm")
    except Exception as exc:
        print(f"ERROR: Failed to list VMs: {exc}", file=sys.stderr)
        return EXIT_API_ERROR

    if args.json:
        print(json.dumps(resources, indent=2, default=str))
        return EXIT_OK

    headers = ["VMID", "NAME", "STATUS", "NODE", "CPU%", "MEM USED", "MEM TOTAL"]
    rows = [
        [
            vm.get("vmid", "?"),
            vm.get("name", "?"),
            vm.get("status", "?"),
            vm.get("node", "?"),
            fmt_pct(vm.get("cpu", 0)),
            fmt_bytes(vm.get("mem", 0)),
            fmt_bytes(vm.get("maxmem", 0)),
        ]
        for vm in sorted(resources, key=lambda x: int(x.get("vmid", 0)))
    ]
    print_table(headers, rows)
    return EXIT_OK


# ── Command: vm ──────────────────────────────────────────────────────────────────

def _find_vm(client, vmid: str) -> tuple[str, dict] | None:
    """Locate a VM by VMID across all nodes. Returns (node, vm_info) or None."""
    try:
        resources = client.cluster.resources.get(type="vm")
    except Exception as exc:
        print(f"ERROR: Failed to query cluster resources: {exc}", file=sys.stderr)
        sys.exit(EXIT_API_ERROR)

    try:
        vmid_int = int(vmid)
    except ValueError:
        print(f"ERROR: VMID must be an integer, got: {vmid!r}", file=sys.stderr)
        sys.exit(EXIT_USAGE)

    for vm in resources:
        if vm.get("vmid") == vmid_int:
            return vm.get("node", ""), vm
    return None


def cmd_vm_status(client, vmid: str, args) -> int:
    result = _find_vm(client, vmid)
    if result is None:
        print(f"ERROR: VM {vmid} not found.", file=sys.stderr)
        return EXIT_NOT_FOUND

    node, vm_info = result
    try:
        status = client.nodes(node).qemu(vmid).status.current.get()
    except Exception as exc:
        print(f"ERROR: Failed to get VM status: {exc}", file=sys.stderr)
        return EXIT_API_ERROR

    if args.json:
        print(json.dumps(status, indent=2, default=str))
        return EXIT_OK

    name   = status.get("name", vm_info.get("name", "?"))
    state  = status.get("status", "?")
    cpus   = status.get("cpus", "?")
    uptime = fmt_uptime(status.get("uptime", 0))

    width = 52
    print(f"{name}  (VMID {vmid})")
    print("─" * width)
    print(f"  Status:   {state}")
    print(f"  Node:     {node}")
    print(f"  CPU:      {fmt_pct(status.get('cpu', 0))}  ({cpus} vCPU{'s' if int(cpus or 1) != 1 else ''})")
    print(f"  Memory:   {fmt_bytes(status.get('mem', 0))} / {fmt_bytes(status.get('maxmem', 0))}")
    print(f"  Uptime:   {uptime}")
    return EXIT_OK


def cmd_vm_start(client, vmid: str, args) -> int:
    result = _find_vm(client, vmid)
    if result is None:
        print(f"ERROR: VM {vmid} not found.", file=sys.stderr)
        return EXIT_NOT_FOUND

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "running":
        print(f"VM {vmid} ({name}) is already running.")
        return EXIT_OK

    try:
        task = client.nodes(node).qemu(vmid).status.start.post()
        print(f"Start queued for VM {vmid} ({name}). Task: {task}")
        return EXIT_OK
    except Exception as exc:
        print(f"ERROR: Failed to start VM {vmid}: {exc}", file=sys.stderr)
        return EXIT_API_ERROR


def cmd_vm_stop(client, vmid: str, args) -> int:
    result = _find_vm(client, vmid)
    if result is None:
        print(f"ERROR: VM {vmid} not found.", file=sys.stderr)
        return EXIT_NOT_FOUND

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "stopped":
        print(f"VM {vmid} ({name}) is already stopped.")
        return EXIT_OK

    try:
        task = client.nodes(node).qemu(vmid).status.stop.post()
        print(f"Stop queued for VM {vmid} ({name}). Task: {task}")
        return EXIT_OK
    except Exception as exc:
        print(f"ERROR: Failed to stop VM {vmid}: {exc}", file=sys.stderr)
        return EXIT_API_ERROR


def cmd_vm_shutdown(client, vmid: str, args) -> int:
    result = _find_vm(client, vmid)
    if result is None:
        print(f"ERROR: VM {vmid} not found.", file=sys.stderr)
        return EXIT_NOT_FOUND

    node, vm_info = result
    name = vm_info.get("name", vmid)
    if vm_info.get("status") == "stopped":
        print(f"VM {vmid} ({name}) is already stopped.")
        return EXIT_OK

    try:
        task = client.nodes(node).qemu(vmid).status.shutdown.post()
        print(f"Shutdown (ACPI) queued for VM {vmid} ({name}). Task: {task}")
        return EXIT_OK
    except Exception as exc:
        print(f"ERROR: Failed to shut down VM {vmid}: {exc}", file=sys.stderr)
        return EXIT_API_ERROR


def cmd_vm(client, args) -> int:
    dispatch = {
        "status":   cmd_vm_status,
        "start":    cmd_vm_start,
        "stop":     cmd_vm_stop,
        "shutdown": cmd_vm_shutdown,
    }
    handler = dispatch.get(args.vm_subcommand)
    if handler is None:
        print(f"ERROR: Unknown vm subcommand: {args.vm_subcommand!r}", file=sys.stderr)
        return EXIT_USAGE
    return handler(client, args.vmid, args)


# ── Command: nextid ──────────────────────────────────────────────────────────────

def cmd_nextid(client, args) -> int:
    try:
        nextid = client.cluster.nextid.get()
    except Exception as exc:
        print(f"ERROR: Failed to get next VMID: {exc}", file=sys.stderr)
        return EXIT_API_ERROR

    if args.json:
        print(json.dumps({"nextid": nextid}, indent=2))
    else:
        print(f"Next available VMID: {nextid}")
    return EXIT_OK


# ── Command: storage ─────────────────────────────────────────────────────────────

def cmd_storage(client, args) -> int:
    try:
        pools = client.storage.get()
    except Exception as exc:
        print(f"ERROR: Failed to list storage: {exc}", file=sys.stderr)
        return EXIT_API_ERROR

    if args.json:
        print(json.dumps(pools, indent=2, default=str))
        return EXIT_OK

    headers = ["STORAGE", "TYPE", "CONTENT", "SHARED", "ACTIVE"]
    rows = [
        [
            p.get("storage", "?"),
            p.get("type", "?"),
            p.get("content", "").replace(",", " "),
            "yes" if p.get("shared", 0) else "no",
            "yes" if p.get("active", p.get("enabled", 1)) else "no",
        ]
        for p in sorted(pools, key=lambda x: x.get("storage", ""))
    ]
    print_table(headers, rows)
    return EXIT_OK


# ── Command: resources ───────────────────────────────────────────────────────────

def cmd_resources(client, args) -> int:
    try:
        resources = client.cluster.resources.get()
    except Exception as exc:
        print(f"ERROR: Failed to list cluster resources: {exc}", file=sys.stderr)
        return EXIT_API_ERROR

    if args.json:
        print(json.dumps(resources, indent=2, default=str))
        return EXIT_OK

    headers = ["TYPE", "ID", "NAME", "STATUS", "NODE"]
    rows = [
        [
            r.get("type", "?"),
            r.get("id", r.get("vmid", "?")),
            r.get("name", ""),
            r.get("status", "?"),
            r.get("node", ""),
        ]
        for r in sorted(resources, key=lambda x: (x.get("type", ""), str(x.get("id", ""))))
    ]
    print_table(headers, rows)
    return EXIT_OK


# ── Argument parser ──────────────────────────────────────────────────────────────

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="proxmox.py",
        description="Proxmox VE REST API CLI",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Credentials (env var → keyring → error):
  PROXMOX_HOST          Hostname or IP (e.g. 192.168.1.10 or pve.local:8006)
  PROXMOX_TOKEN_ID      API token ID  (user@realm!tokenname)
  PROXMOX_TOKEN_SECRET  API token secret (UUID)
  PROXMOX_API_TOKEN     Combined: user@realm!tokenname=uuid

Keyring service name: proxmox-api  (keys: host, token_id, token_secret)

Examples:
  proxmox.py configure
  proxmox.py nodes
  proxmox.py vms
  proxmox.py vm status 100
  proxmox.py vm start 101
  proxmox.py vm shutdown 101
  proxmox.py nextid
  proxmox.py storage
  proxmox.py resources --json
        """,
    )

    sub = parser.add_subparsers(dest="command", metavar="command")

    # configure
    p_cfg = sub.add_parser("configure", help="Store credentials in system keyring (run this yourself, not via agent)")
    p_cfg.add_argument("--host",          help="Proxmox hostname or IP")
    p_cfg.add_argument("--token-id",      dest="token_id",     help="API token ID (user@realm!tokenname)")
    p_cfg.add_argument("--token-secret",  dest="token_secret", help="API token secret UUID")

    # nodes
    p_nodes = sub.add_parser("nodes", help="List all cluster nodes")
    p_nodes.add_argument("--json", action="store_true")

    # vms
    p_vms = sub.add_parser("vms", help="List all VMs across cluster")
    p_vms.add_argument("--json", action="store_true")

    # vm
    p_vm = sub.add_parser("vm", help="VM operations: status, start, stop, shutdown")
    p_vm.add_argument("vm_subcommand", choices=["status", "start", "stop", "shutdown"],
                      metavar="status|start|stop|shutdown")
    p_vm.add_argument("vmid", metavar="VMID")
    p_vm.add_argument("--json", action="store_true", help="JSON output (status only)")

    # nextid
    p_nid = sub.add_parser("nextid", help="Get next available VMID")
    p_nid.add_argument("--json", action="store_true")

    # storage
    p_stor = sub.add_parser("storage", help="List storage pools")
    p_stor.add_argument("--json", action="store_true")

    # resources
    p_res = sub.add_parser("resources", help="List all cluster resources")
    p_res.add_argument("--json", action="store_true")

    return parser


# ── Entry point ──────────────────────────────────────────────────────────────────

def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        return EXIT_USAGE

    if args.command == "configure":
        return cmd_configure(args)

    client = make_client()

    dispatch = {
        "nodes":     cmd_nodes,
        "vms":       cmd_vms,
        "vm":        cmd_vm,
        "nextid":    cmd_nextid,
        "storage":   cmd_storage,
        "resources": cmd_resources,
    }

    return dispatch[args.command](client, args)


if __name__ == "__main__":
    sys.exit(main())
