# /// script
# requires-python = ">=3.9"
# dependencies = ["requests", "keyring", "click"]
# ///
"""
technitium.py — Technitium DNS Server REST API CLI

Credentials are resolved in order: environment variable → keyring → error.

Environment variables:
  TECHNITIUM_HOST   Hostname or IP (no https://, port optional, default 5380)
  TECHNITIUM_TOKEN  Non-expiring API token created via the UI or createToken

Keyring service: technitium-api
  Keys: host, token

Note: the env var names match those already consumed by the kenske/technitium
Terraform provider, so an `infisical run` shell with those exported will work
without any keyring setup.

Usage:
  technitium.py configure [--host HOST] [--token TOKEN]
  technitium.py whoami                              Validate token, show user/permissions

  # DHCP — devices on the network
  technitium.py leases [--scope NAME] [--sort ip|host|mac|expires] [--json]
  technitium.py scopes [--json]
  technitium.py scope <name> [--json]
  technitium.py lease remove   <scope> <mac>
  technitium.py lease reserve  <scope> <mac>            (converts existing lease)
  technitium.py lease dynamic  <scope> <mac>
  technitium.py lease add      <scope> <mac> <ip> [--hostname H] [--comment C]
  technitium.py lease unreserve <scope> <mac>           (removes a scope reservation)

  # DNS zones & records
  technitium.py zones [--json]
  technitium.py records <zone> [--json]
  technitium.py record add    <zone> <name> <type> <value> [--ttl N] [--overwrite]
  technitium.py record delete <zone> <name> <type> <value>

  # Server stats
  technitium.py stats [LastHour|LastDay|LastWeek|LastMonth|LastYear] [--json]
  technitium.py top   <TopClients|TopDomains|TopBlockedDomains> [period] [--limit N] [--json]

  # Query logs (needs a query-log app installed; defaults to the built-in Sqlite app)
  technitium.py logs [--client IP] [--qname DOMAIN] [--qtype TYPE]
                     [--response Authoritative|Recursive|Cached|Blocked]
                     [--start ISO8601] [--end ISO8601]
                     [--page N] [--per-page N] [--app NAME] [--class CLASSPATH] [--json]

  # Cache, allow/block lists
  technitium.py cache list   [--domain D] [--json]
  technitium.py cache flush
  technitium.py cache delete <domain>
  technitium.py allowed [--json]
  technitium.py blocked [--json]

  # Ad-hoc DNS resolution from the server's perspective
  technitium.py resolve <domain> [type] [--server SERVER] [--protocol Udp|Tcp|Tls|Https|Quic] [--json]
"""
from __future__ import annotations

import getpass
import json
import os
import sys
from typing import Any

import click

KEYRING_SERVICE = "technitium-api"
DEFAULT_PORT = 5380

# Built-in query-log app shipped with Technitium. Override with --app/--class
# if you've renamed the app or installed a different one.
DEFAULT_LOG_APP   = "Query Logs (Sqlite)"
DEFAULT_LOG_CLASS = "QueryLogsSqlite.App"

# ── Exit codes ──────────────────────────────────────────────────────────────────

EXIT_OK         = 0
EXIT_USAGE      = 1
EXIT_CONNECT    = 2
EXIT_NOT_FOUND  = 3
EXIT_API_ERROR  = 4
EXIT_DEP_ERROR  = 5


# ── Credential resolution ───────────────────────────────────────────────────────

def _ensure_session_bus() -> None:
    """Point keyring at the login session's D-Bus when the variable is unset.

    keyring's SecretService backend talks to gnome-keyring/KWallet over the
    session bus. A tmux pane, an ssh session, or any shell not started by the
    desktop inherits no DBUS_SESSION_BUS_ADDRESS, so keyring finds no viable
    backend and raises NoKeyringError -- which reads like "keyring is not
    installed" but actually means "cannot reach the daemon". The socket is at
    a predictable path, so fall back to it rather than making the caller
    export the variable by hand.
    """
    if os.environ.get("DBUS_SESSION_BUS_ADDRESS"):
        return
    try:
        sock = f"/run/user/{os.getuid()}/bus"
    except AttributeError:      # non-POSIX
        return
    if os.path.exists(sock):
        os.environ["DBUS_SESSION_BUS_ADDRESS"] = f"unix:path={sock}"


def _keyring_get(key: str) -> str:
    _ensure_session_bus()
    try:
        import keyring as kr
        return kr.get_password(KEYRING_SERVICE, key) or ""
    except Exception as exc:
        # Distinguish "no stored value" from "keyring unreachable" -- the
        # second used to look identical to missing credentials.
        print(f"Warning: keyring unavailable ({type(exc).__name__}: {exc})", file=sys.stderr)
        return ""


def _keyring_set(key: str, value: str) -> None:
    _ensure_session_bus()
    import keyring as kr
    kr.set_password(KEYRING_SERVICE, key, value)


def resolve_credentials() -> tuple[str, str]:
    """Returns (host, token). Resolution order: env var → keyring → error."""
    host  = (os.environ.get("TECHNITIUM_HOST", "").strip()  or _keyring_get("host"))
    token = (os.environ.get("TECHNITIUM_TOKEN", "").strip() or _keyring_get("token"))

    # Strip any accidental scheme prefix — we add it ourselves below.
    for prefix in ("https://", "http://"):
        if host.lower().startswith(prefix):
            host = host[len(prefix):]
    host = host.rstrip("/")

    missing = []
    if not host:
        missing.append("TECHNITIUM_HOST")
    if not token:
        missing.append("TECHNITIUM_TOKEN")
    if missing:
        raise click.ClickException(
            "Credentials not found. Missing:\n"
            + "\n".join(f"  {v}" for v in missing)
            + "\n\nRun configure to store credentials in your keyring:\n"
            "  uv run .claude/skills/technitium-api/scripts/technitium.py configure\n\n"
            "Or set the environment variables above (e.g. via `infisical run`).\n"
            "See references/setup.md for how to create a Technitium API token."
        )
    return host, token


def base_url(host: str) -> str:
    # Default to http; Technitium's web UI is plain HTTP on 5380 unless the user
    # set up reverse-proxy TLS. If the user gave host:port we honor it as-is.
    scheme = "http"
    hostport = host if ":" in host else f"{host}:{DEFAULT_PORT}"
    return f"{scheme}://{hostport}"


# ── HTTP helpers ────────────────────────────────────────────────────────────────

def _api_call(path: str, params: dict[str, Any] | None = None, method: str = "GET") -> dict:
    try:
        import requests
    except ImportError:
        click.echo("ERROR: 'requests' not installed. Run via `uv run`.", err=True)
        sys.exit(EXIT_DEP_ERROR)

    host, token = resolve_credentials()
    url = f"{base_url(host)}{path}"
    headers = {"Authorization": f"Bearer {token}"}

    try:
        if method == "GET":
            r = requests.get(url, params=params or {}, headers=headers, timeout=15)
        else:
            r = requests.post(url, data=params or {}, headers=headers, timeout=15)
    except requests.exceptions.ConnectionError as e:
        click.echo(f"ERROR: Cannot connect to {url}: {e}", err=True)
        sys.exit(EXIT_CONNECT)
    except requests.exceptions.Timeout:
        click.echo(f"ERROR: Timeout calling {url}", err=True)
        sys.exit(EXIT_CONNECT)

    if r.status_code >= 500:
        click.echo(f"ERROR: HTTP {r.status_code} from {url}\n{r.text[:500]}", err=True)
        sys.exit(EXIT_API_ERROR)

    try:
        data = r.json()
    except ValueError:
        click.echo(f"ERROR: Non-JSON response from {url}\n{r.text[:500]}", err=True)
        sys.exit(EXIT_API_ERROR)

    status = data.get("status")
    if status == "ok":
        return data
    if status == "invalid-token":
        click.echo("ERROR: Token is invalid or expired. Re-run configure or mint a new token in the UI.", err=True)
        sys.exit(EXIT_API_ERROR)
    if status == "error":
        click.echo(f"ERROR: {data.get('errorMessage', 'unknown')}", err=True)
        sys.exit(EXIT_API_ERROR)
    click.echo(f"ERROR: Unexpected status '{status}': {json.dumps(data)[:500]}", err=True)
    sys.exit(EXIT_API_ERROR)


def _emit(payload: Any, as_json: bool, formatter=None) -> None:
    if as_json:
        click.echo(json.dumps(payload, indent=2, default=str))
        return
    if formatter is None:
        click.echo(json.dumps(payload, indent=2, default=str))
    else:
        formatter(payload)


# ── Tabular formatters ──────────────────────────────────────────────────────────

def _table(rows: list[dict], cols: list[tuple[str, str]]) -> None:
    """Simple aligned table. cols = [(header, key), ...]."""
    if not rows:
        click.echo("(no entries)")
        return
    widths = [len(h) for h, _ in cols]
    for row in rows:
        for i, (_, k) in enumerate(cols):
            widths[i] = max(widths[i], len(str(row.get(k, "") or "")))
    header = "  ".join(h.ljust(widths[i]) for i, (h, _) in enumerate(cols))
    click.echo(header)
    click.echo("  ".join("-" * w for w in widths))
    for row in rows:
        click.echo("  ".join(str(row.get(k, "") or "").ljust(widths[i]) for i, (_, k) in enumerate(cols)))


# ── CLI ─────────────────────────────────────────────────────────────────────────

@click.group(context_settings={"help_option_names": ["-h", "--help"]})
def cli() -> None:
    """Technitium DNS Server CLI."""


@cli.command()
@click.option("--host", default=None)
@click.option("--token", default=None)
def configure(host: str | None, token: str | None) -> None:
    """Store host + API token in the system keyring and verify connectivity."""
    if host is None:
        host = click.prompt("Technitium host (e.g. dns-vm.lan or 192.168.0.250)", default=_keyring_get("host") or "")
    if token is None:
        token = getpass.getpass("API token (input hidden): ").strip()
    host = host.strip()
    for prefix in ("https://", "http://"):
        if host.lower().startswith(prefix):
            host = host[len(prefix):]
    host = host.rstrip("/")

    if not host or not token:
        raise click.ClickException("Host and token are both required.")
    _keyring_set("host", host)
    _keyring_set("token", token)
    click.echo(f"Saved to keyring service '{KEYRING_SERVICE}': host={host}, token=***")

    # Verify
    data = _api_call("/api/user/session/get")
    info = data.get("response", {})
    click.echo(f"OK — authenticated as '{info.get('username', '?')}' on Technitium {info.get('version', '?')}")


@cli.command()
@click.option("--json", "as_json", is_flag=True)
def whoami(as_json: bool) -> None:
    """Validate token, show user + version + permissions."""
    data = _api_call("/api/user/session/get")
    info = data.get("response", {})
    if as_json:
        click.echo(json.dumps(info, indent=2))
        return
    click.echo(f"User:    {info.get('username')}")
    click.echo(f"Version: {info.get('version')}")
    click.echo(f"Server:  {info.get('dnsServerDomain')}")
    perms = info.get("permissions", {})
    if perms:
        click.echo("Permissions:")
        for section, p in perms.items():
            flags = ",".join(k.replace("can", "").lower() for k, v in p.items() if v)
            click.echo(f"  {section:<14} {flags}")


# ── DHCP ───────────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--scope", default=None, help="Filter to one scope by name.")
@click.option("--sort", "sort_key", type=click.Choice(["ip", "host", "mac", "expires", "scope"]), default="ip")
@click.option("--json", "as_json", is_flag=True)
def leases(scope: str | None, sort_key: str, as_json: bool) -> None:
    """List DHCP leases — i.e. devices currently on the network."""
    data = _api_call("/api/dhcp/leases/list")
    rows = data["response"]["leases"]
    if scope:
        rows = [r for r in rows if (r.get("scope") or "").lower() == scope.lower()]

    def _ipkey(r: dict) -> tuple:
        try:
            return tuple(int(p) for p in (r.get("address") or "0.0.0.0").split("."))
        except ValueError:
            return (0, 0, 0, 0)
    sort_fns = {
        "ip":      _ipkey,
        "host":    lambda r: (r.get("hostName") or "").lower(),
        "mac":     lambda r: (r.get("hardwareAddress") or "").lower(),
        "expires": lambda r: r.get("leaseExpires") or "",
        "scope":   lambda r: (r.get("scope") or "").lower(),
    }
    rows = sorted(rows, key=sort_fns[sort_key])

    def fmt(_: Any) -> None:
        cols = [
            ("ADDRESS",  "address"),
            ("HOSTNAME", "hostName"),
            ("MAC",      "hardwareAddress"),
            ("TYPE",     "type"),
            ("SCOPE",    "scope"),
            ("EXPIRES",  "leaseExpires"),
        ]
        _table(rows, cols)
        click.echo(f"\n{len(rows)} lease(s)")
    _emit(rows, as_json, fmt)


@cli.command()
@click.option("--json", "as_json", is_flag=True)
def scopes(as_json: bool) -> None:
    """List DHCP scopes."""
    data = _api_call("/api/dhcp/scopes/list")
    rows = data["response"]["scopes"]
    def fmt(_: Any) -> None:
        for s in rows:
            click.echo(f"{s['name']:<20} {s['startingAddress']} – {s['endingAddress']}  "
                       f"mask={s['subnetMask']}  enabled={s['enabled']}")
        click.echo(f"\n{len(rows)} scope(s)")
    _emit(rows, as_json, fmt)


@cli.command()
@click.argument("name")
@click.option("--json", "as_json", is_flag=True)
def scope(name: str, as_json: bool) -> None:
    """Get full configuration for a DHCP scope."""
    data = _api_call("/api/dhcp/scopes/get", {"name": name})
    _emit(data["response"], as_json)


@cli.command("scope-set")
@click.argument("name")
@click.option("--start", "starting_address", required=True, help="Pool start IP.")
@click.option("--end", "ending_address", required=True, help="Pool end IP.")
@click.option("--mask", "subnet_mask", default="255.255.255.0", help="Subnet mask.")
@click.option("--router", "router_address", required=True, help="Gateway/router IP handed to clients.")
@click.option("--dns", "dns_servers", default=None,
              help="Comma-separated DNS servers handed to clients. Sets useThisDnsServer=false.")
@click.option("--domain", "domain_name", default=None, help="DHCP domain; auto-registers leases as <host>.<domain>.")
@click.option("--dns-updates/--no-dns-updates", "dns_updates", default=True,
              help="Auto-create/update forward+reverse DNS records for leases (default on).")
@click.option("--dns-ttl", "dns_ttl", default=900, type=int)
@click.option("--lease-days", "lease_days", default=1, type=int)
@click.option("--ping-check/--no-ping-check", "ping_check", default=True,
              help="Ping an address before offering it, to avoid handing out an in-use IP (default on).")
@click.option("--exclusions", "exclusions", default=None,
              help="Raw exclusions string passed verbatim as the 'exclusions' API param (format-test via read-back).")
def scope_set(name, starting_address, ending_address, subnet_mask, router_address,
              dns_servers, domain_name, dns_updates, dns_ttl, lease_days, ping_check, exclusions) -> None:
    """Create or update a DHCP scope (POST /api/dhcp/scopes/set). Run scope-enable afterwards."""
    params: dict[str, Any] = {
        "name":              name,
        "startingAddress":   starting_address,
        "endingAddress":     ending_address,
        "subnetMask":        subnet_mask,
        "routerAddress":     router_address,
        "leaseTimeDays":     str(lease_days),
        "leaseTimeHours":    "0",
        "leaseTimeMinutes":  "0",
        "dnsTtl":            str(dns_ttl),
        "dnsUpdates":        "true" if dns_updates else "false",
        "pingCheckEnabled":  "true" if ping_check else "false",
    }
    if dns_servers:
        params["useThisDnsServer"] = "false"
        params["dnsServers"]       = dns_servers
    if domain_name:
        params["domainName"] = domain_name
    if exclusions:
        params["exclusions"] = exclusions
    _api_call("/api/dhcp/scopes/set", params)
    click.echo(f"OK — scope '{name}' set: {starting_address}–{ending_address} "
               f"router={router_address} domain={domain_name or '(unchanged)'} dnsUpdates={dns_updates}. "
               f"Run 'scope-enable {name}' to activate.")


@cli.command("scope-enable")
@click.argument("name")
def scope_enable(name: str) -> None:
    """Enable (activate) a DHCP scope."""
    _api_call("/api/dhcp/scopes/enable", {"name": name})
    click.echo(f"OK — scope '{name}' enabled")


@cli.command("scope-disable")
@click.argument("name")
def scope_disable(name: str) -> None:
    """Disable a DHCP scope (stops serving leases; does not delete it)."""
    _api_call("/api/dhcp/scopes/disable", {"name": name})
    click.echo(f"OK — scope '{name}' disabled")


@cli.command("scope-delete")
@click.argument("name")
def scope_delete(name: str) -> None:
    """Delete a DHCP scope entirely."""
    _api_call("/api/dhcp/scopes/delete", {"name": name})
    click.echo(f"OK — scope '{name}' deleted")


@cli.group()
def lease() -> None:
    """Manage a single DHCP lease."""


@lease.command("remove")
@click.argument("scope_name")
@click.argument("mac")
def lease_remove(scope_name: str, mac: str) -> None:
    """Remove a dynamic or reserved lease (free the IP)."""
    _api_call("/api/dhcp/leases/remove", {"name": scope_name, "hardwareAddress": mac})
    click.echo(f"OK — removed lease {mac} from scope {scope_name}")


@lease.command("reserve")
@click.argument("scope_name")
@click.argument("mac")
def lease_reserve(scope_name: str, mac: str) -> None:
    """Convert a dynamic lease into a reserved (sticky) lease."""
    _api_call("/api/dhcp/leases/convertToReserved", {"name": scope_name, "hardwareAddress": mac})
    click.echo(f"OK — {mac} in {scope_name} is now reserved")


@lease.command("dynamic")
@click.argument("scope_name")
@click.argument("mac")
def lease_dynamic(scope_name: str, mac: str) -> None:
    """Convert a reserved lease back to dynamic."""
    _api_call("/api/dhcp/leases/convertToDynamic", {"name": scope_name, "hardwareAddress": mac})
    click.echo(f"OK — {mac} in {scope_name} is now dynamic")


@lease.command("add")
@click.argument("scope_name")
@click.argument("mac")
@click.argument("ip")
@click.option("--hostname", default=None, help="Optional hostname to override the client-supplied one.")
@click.option("--comment",  default=None, help="Free-text comment for the reservation.")
def lease_add(scope_name: str, mac: str, ip: str, hostname: str | None, comment: str | None) -> None:
    """Add a new scope-level reserved lease (the device need not have an existing lease)."""
    params: dict[str, str] = {"name": scope_name, "hardwareAddress": mac, "ipAddress": ip}
    if hostname: params["hostName"] = hostname
    if comment:  params["comments"] = comment
    _api_call("/api/dhcp/scopes/addReservedLease", params)
    click.echo(f"OK — reserved {ip} for {mac} in scope {scope_name}"
               + (f" (hostname={hostname})" if hostname else ""))


@lease.command("unreserve")
@click.argument("scope_name")
@click.argument("mac")
def lease_unreserve(scope_name: str, mac: str) -> None:
    """Remove a scope-level reservation (the reservedLeases entry, not the active lease)."""
    _api_call("/api/dhcp/scopes/removeReservedLease", {"name": scope_name, "hardwareAddress": mac})
    click.echo(f"OK — removed reservation for {mac} from scope {scope_name}")


# ── Zones & records ────────────────────────────────────────────────────────────

@cli.command()
@click.option("--json", "as_json", is_flag=True)
def zones(as_json: bool) -> None:
    """List all authoritative zones."""
    data = _api_call("/api/zones/list")
    rows = data["response"]["zones"]
    def fmt(_: Any) -> None:
        cols = [
            ("NAME",      "name"),
            ("TYPE",      "type"),
            ("DNSSEC",    "dnssecStatus"),
            ("SERIAL",    "soaSerial"),
            ("DISABLED",  "disabled"),
            ("INTERNAL",  "internal"),
        ]
        _table(rows, cols)
        click.echo(f"\n{len(rows)} zone(s)")
    _emit(rows, as_json, fmt)


@cli.command()
@click.argument("zone_name")
@click.option("--json", "as_json", is_flag=True)
def records(zone_name: str, as_json: bool) -> None:
    """List all records in a zone."""
    data = _api_call("/api/zones/records/get", {
        "domain":   zone_name,
        "zone":     zone_name,
        "listZone": "true",
    })
    rows = data["response"]["records"]
    def fmt(_: Any) -> None:
        for r in rows:
            rdata = r.get("rData", {})
            # rData has type-specific fields; render the most interesting one.
            value = rdata.get("ipAddress") or rdata.get("cname") or rdata.get("text") \
                    or rdata.get("nameServer") or rdata.get("ptrName") or json.dumps(rdata)
            click.echo(f"{r.get('name', ''):<40} {r.get('type', ''):<8} ttl={r.get('ttl', '?'):<6} {value}")
        click.echo(f"\n{len(rows)} record(s)")
    _emit(rows, as_json, fmt)


@cli.group()
def record() -> None:
    """Add or delete a single DNS record."""


def _record_value_params(rtype: str, value: str) -> dict[str, str]:
    """Map a record type + value to the right Technitium parameter name."""
    rtype = rtype.upper()
    if rtype in ("A", "AAAA"):    return {"ipAddress": value}
    if rtype == "CNAME":          return {"cname":     value}
    if rtype == "NS":             return {"nameServer": value}
    if rtype == "PTR":            return {"ptrName":   value}
    if rtype == "TXT":            return {"text":      value}
    if rtype == "DNAME":          return {"cname":     value}  # uses cname param per docs
    raise click.ClickException(
        f"Record type '{rtype}' not wired up in this CLI shortcut. Use the full API "
        "via 'curl' or extend this script — see references/api-reference.md."
    )


@record.command("add")
@click.argument("zone_name")
@click.argument("name")
@click.argument("rtype")
@click.argument("value")
@click.option("--ttl", default=None, type=int)
@click.option("--overwrite", is_flag=True)
def record_add(zone_name: str, name: str, rtype: str, value: str, ttl: int | None, overwrite: bool) -> None:
    """Add a DNS record. NAME is the FQDN (e.g. host.lan), not just the leaf label."""
    params: dict[str, Any] = {
        "domain": name,
        "zone":   zone_name,
        "type":   rtype.upper(),
    }
    params.update(_record_value_params(rtype, value))
    if ttl is not None:
        params["ttl"] = str(ttl)
    if overwrite:
        params["overwrite"] = "true"
    _api_call("/api/zones/records/add", params)
    click.echo(f"OK — added {rtype.upper()} {name} → {value} in zone {zone_name}")


@record.command("delete")
@click.argument("zone_name")
@click.argument("name")
@click.argument("rtype")
@click.argument("value")
def record_delete(zone_name: str, name: str, rtype: str, value: str) -> None:
    """Delete a DNS record. Match on the exact value."""
    params: dict[str, Any] = {
        "domain": name,
        "zone":   zone_name,
        "type":   rtype.upper(),
    }
    params.update(_record_value_params(rtype, value))
    _api_call("/api/zones/records/delete", params)
    click.echo(f"OK — deleted {rtype.upper()} {name} → {value} from zone {zone_name}")


# ── Dashboard stats ────────────────────────────────────────────────────────────

PERIODS = ["LastHour", "LastDay", "LastWeek", "LastMonth", "LastYear"]


@cli.command()
@click.argument("period", type=click.Choice(PERIODS), default="LastHour")
@click.option("--json", "as_json", is_flag=True)
def stats(period: str, as_json: bool) -> None:
    """Dashboard stats for the given period."""
    data = _api_call("/api/dashboard/stats/get", {"type": period, "utc": "true"})
    resp = data["response"]
    if as_json:
        click.echo(json.dumps(resp, indent=2))
        return
    s = resp.get("stats", {})
    click.echo(f"Period:           {period}")
    click.echo(f"Total queries:    {s.get('totalQueries')}")
    click.echo(f"  No error:       {s.get('totalNoError')}")
    click.echo(f"  Server failure: {s.get('totalServerFailure')}")
    click.echo(f"  NX domain:      {s.get('totalNxDomain')}")
    click.echo(f"  Refused:        {s.get('totalRefused')}")
    click.echo(f"Authoritative:    {s.get('totalAuthoritative')}")
    click.echo(f"Recursive:        {s.get('totalRecursive')}")
    click.echo(f"Cached:           {s.get('totalCached')}")
    click.echo(f"Blocked:          {s.get('totalBlocked')}")
    click.echo(f"Dropped:          {s.get('totalDropped')}")
    click.echo(f"Clients:          {s.get('totalClients')}")
    click.echo(f"Zones (auth):     {s.get('zones')}")
    click.echo(f"Cached entries:   {s.get('cachedEntries')}")
    click.echo(f"Allowed/Blocked:  {s.get('allowedZones')}/{s.get('blockedZones')}")


@cli.command()
@click.argument("stats_type", type=click.Choice(["TopClients", "TopDomains", "TopBlockedDomains"]))
@click.argument("period", type=click.Choice(PERIODS), default="LastHour")
@click.option("--limit", default=10, type=int)
@click.option("--json", "as_json", is_flag=True)
def top(stats_type: str, period: str, limit: int, as_json: bool) -> None:
    """Top clients / domains / blocked domains."""
    data = _api_call("/api/dashboard/stats/getTop", {
        "type":      period,
        "statsType": stats_type,
        "limit":     str(limit),
    })
    rows = data["response"].get(stats_type[0].lower() + stats_type[1:], []) or \
           data["response"].get("topClients") or data["response"].get("topDomains") or \
           data["response"].get("topBlockedDomains") or []
    if as_json:
        click.echo(json.dumps(rows, indent=2))
        return
    for r in rows:
        # TopClients: {name, domain, hits}; TopDomains: {name, hits}
        click.echo(f"{r.get('hits', 0):>10}  {r.get('name', '')}  {r.get('domain', '') or ''}")
    click.echo(f"\n{len(rows)} entries")


# ── Query logs ─────────────────────────────────────────────────────────────────

@cli.command()
@click.option("--client",   "client_ip", default=None)
@click.option("--qname",    default=None)
@click.option("--qtype",    default=None)
@click.option("--response", "response_type",
              type=click.Choice(["Authoritative", "Recursive", "Cached", "Blocked",
                                 "UpstreamBlocked", "CacheBlocked"]),
              default=None)
@click.option("--start", default=None, help="Start datetime (ISO 8601, UTC).")
@click.option("--end",   default=None, help="End datetime (ISO 8601, UTC).")
@click.option("--page",     default=1, type=int)
@click.option("--per-page", default=50, type=int)
@click.option("--app",   default=DEFAULT_LOG_APP)
@click.option("--class", "class_path", default=DEFAULT_LOG_CLASS)
@click.option("--json", "as_json", is_flag=True)
def logs(client_ip, qname, qtype, response_type, start, end, page, per_page, app, class_path, as_json):
    """Query DNS query logs (filter by client, qname, type, etc.)."""
    params = {
        "name":            app,
        "classPath":       class_path,
        "pageNumber":      str(page),
        "entriesPerPage":  str(per_page),
        "descendingOrder": "true",
    }
    if client_ip:     params["clientIpAddress"] = client_ip
    if qname:         params["qname"]           = qname
    if qtype:         params["qtype"]           = qtype
    if response_type: params["responseType"]    = response_type
    if start:         params["start"]           = start
    if end:           params["end"]             = end

    data = _api_call("/api/logs/query", params)
    resp = data["response"]
    rows = resp.get("entries", [])
    if as_json:
        click.echo(json.dumps(resp, indent=2))
        return
    for r in rows:
        click.echo(f"{r.get('timestamp')}  {r.get('clientIpAddress'):<15}  "
                   f"{r.get('responseType'):<14} {r.get('rcode'):<12} "
                   f"{r.get('qtype'):<6} {r.get('qname')}  → {r.get('answer', '')[:80]}")
    click.echo(f"\nPage {resp.get('pageNumber')}/{resp.get('totalPages')} "
               f"({resp.get('totalEntries')} total entries)")


# ── Cache & lists ──────────────────────────────────────────────────────────────

@cli.group()
def cache() -> None:
    """DNS resolver cache."""


@cache.command("list")
@click.option("--domain", default="")
@click.option("--json", "as_json", is_flag=True)
def cache_list(domain: str, as_json: bool) -> None:
    """List cached zones under a domain."""
    data = _api_call("/api/cache/list", {"domain": domain})
    _emit(data["response"], as_json)


@cache.command("flush")
def cache_flush() -> None:
    """Flush the entire DNS resolver cache."""
    _api_call("/api/cache/flush")
    click.echo("OK — cache flushed")


@cache.command("delete")
@click.argument("domain")
def cache_delete(domain: str) -> None:
    """Delete a single cached zone."""
    _api_call("/api/cache/delete", {"domain": domain})
    click.echo(f"OK — removed {domain} from cache")


@cli.command()
@click.option("--json", "as_json", is_flag=True)
def allowed(as_json: bool) -> None:
    """List entries in the Allowed zones list."""
    data = _api_call("/api/allowed/list", {"domain": ""})
    _emit(data["response"], as_json)


@cli.command()
@click.option("--json", "as_json", is_flag=True)
def blocked(as_json: bool) -> None:
    """List entries in the Blocked zones list."""
    data = _api_call("/api/blocked/list", {"domain": ""})
    _emit(data["response"], as_json)


# ── DNS client (resolve from the server's perspective) ─────────────────────────

@cli.command()
@click.argument("domain")
@click.argument("rtype", default="A")
@click.option("--server",   default="this-server",
              help="Resolver to query: 'this-server', 'recursive-resolver', or a DNS server address.")
@click.option("--protocol", default="Udp", type=click.Choice(["Udp", "Tcp", "Tls", "Https", "Quic"]))
@click.option("--json", "as_json", is_flag=True)
def resolve(domain: str, rtype: str, server: str, protocol: str, as_json: bool) -> None:
    """Resolve a name using the DNS server's built-in client."""
    data = _api_call("/api/dnsClient/resolve", {
        "server":   server,
        "domain":   domain,
        "type":     rtype.upper(),
        "protocol": protocol,
    })
    _emit(data["response"], as_json)


if __name__ == "__main__":
    cli()
