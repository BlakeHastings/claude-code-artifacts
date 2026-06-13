# Technitium API quick reference

Authoritative docs: <https://github.com/TechnitiumSoftware/DnsServer/blob/master/APIDOCS.md>
(8200 lines; this file is just a cheatsheet for the endpoints the CLI uses and
the ones you'd extend it with.)

## Auth

- Base URL: `http://<host>:5380`
- Header: `Authorization: Bearer <token>` (preferred since v15)
- Alternative: `?token=<token>` query param (legacy, still works)
- All responses are JSON with a top-level `status: "ok" | "error" | "invalid-token" | "2fa-required"`
  and a `response: { ... }` body on success.

## Endpoints wired into `technitium.py`

| Command | Endpoint |
|---------|----------|
| `whoami` | `GET /api/user/session/get` |
| `leases` | `GET /api/dhcp/leases/list` |
| `scopes` | `GET /api/dhcp/scopes/list` |
| `scope <name>` | `GET /api/dhcp/scopes/get?name=` |
| `lease remove` | `GET /api/dhcp/leases/remove?name=&hardwareAddress=` |
| `lease reserve` | `GET /api/dhcp/leases/convertToReserved?name=&hardwareAddress=` |
| `lease dynamic` | `GET /api/dhcp/leases/convertToDynamic?name=&hardwareAddress=` |
| `zones` | `GET /api/zones/list` |
| `records <zone>` | `GET /api/zones/records/get?domain=&zone=&listZone=true` |
| `record add` | `GET /api/zones/records/add?zone=&domain=&type=&...` |
| `record delete` | `GET /api/zones/records/delete?zone=&domain=&type=&...` |
| `stats` | `GET /api/dashboard/stats/get?type=&utc=true` |
| `top` | `GET /api/dashboard/stats/getTop?type=&statsType=&limit=` |
| `logs` | `GET /api/logs/query?name=&classPath=&...` |
| `cache list/flush/delete` | `GET /api/cache/{list,flush,delete}` |
| `allowed` | `GET /api/allowed/list?domain=` |
| `blocked` | `GET /api/blocked/list?domain=` |
| `resolve` | `GET /api/dnsClient/resolve?server=&domain=&type=&protocol=` |

## Record type → parameter name

The add/delete endpoint takes the record value in a type-specific parameter:

| Type | Param |
|------|-------|
| A, AAAA | `ipAddress` |
| CNAME, DNAME | `cname` |
| NS | `nameServer` |
| PTR | `ptrName` |
| TXT | `text` |
| MX | `exchange` + `preference` |
| SRV | `target` + `port` + `priority` + `weight` |
| SOA | many fields — use the UI |
| FWD | `forwarder` + `protocol` |
| APP | `appName` + `classPath` + `recordData` |

`technitium.py` currently shortcuts A/AAAA/CNAME/NS/PTR/TXT/DNAME. For MX/SRV/
SOA/FWD/APP records, either extend `_record_value_params` or call `curl`
directly against `/api/zones/records/add`.

## Endpoints NOT wired up (extend the CLI if you need them)

- Zone create/delete/import/export/sign/unsign — `/api/zones/{create,delete,import,export,sign,unsign}`
- DNSSEC key management — `/api/zones/dnssec/*`
- Scope create/set/delete — `/api/dhcp/scopes/{set,delete,enable,disable}`
- Reserved lease add/remove (scope-level) — `/api/dhcp/scopes/{addReservedLease,removeReservedLease}`
- Settings get/set — `/api/settings/{get,set}`
- Force-update block lists — `/api/settings/forceUpdateBlockLists`
- Apps install/update/uninstall — `/api/apps/*`
- Logs list/download/delete — `/api/logs/{list,download,delete,deleteAll}`
- Administration: users, groups, sessions, permissions, SSO, clustering — `/api/admin/*`

For one-offs, the pattern is:

```bash
curl -sH "Authorization: Bearer $TECHNITIUM_TOKEN" \
  "http://$TECHNITIUM_HOST:5380/api/<path>?<params>" | jq
```

## Error shapes

```json
{"status": "error",         "errorMessage": "...", "stackTrace": "...", "innerErrorMessage": "..."}
{"status": "invalid-token"}
{"status": "2fa-required"}
```

The CLI maps all three to exit code 4 with a one-line message on stderr.

## Query Logs gotcha

`/api/logs/query` is a wrapper that delegates to a DNS app — there is no
built-in log table at the server level. Technitium ships with the
**Query Logs (Sqlite)** app pre-installed; the CLI defaults to:

- `name=Query Logs (Sqlite)`
- `classPath=QueryLogsSqlite.App`

If those defaults are wrong on your install (someone uninstalled it, or you
installed a different log app like `LogExporter`), use `--app` and `--class`.
You can list installed apps via `GET /api/apps/list` to find the right values.
