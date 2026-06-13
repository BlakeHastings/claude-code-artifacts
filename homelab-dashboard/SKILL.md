---
name: homelab-dashboard
description: >-
  Operational knowledge for the homelab dashboard (gethomepage/homepage at
  dashboard.lan:3000). Use when adding or rearranging service tiles, fixing
  broken icons, troubleshooting "Host validation failed" errors, tweaking
  layout/columns, wiring up widget API integrations, or understanding why
  a config change hasn't shown up on the page yet. Covers the specific
  homelab deployment in homelab-services/services/dashboard/, not general
  Homepage usage.
allowed-tools: Read, Edit, Write, Bash, Grep, Glob
---

# Homelab Dashboard (Homepage)

The landing page at **http://dashboard.lan:3000** — runs
gethomepage/homepage on `dashboard-vm`. Source of truth:
[`homelab-services/services/dashboard/`](https://github.com/BlakeHastings/homelab-services/tree/main/services/dashboard).

DNS: `dashboard.lan` is a **CNAME → dashboard-vm.lan** (which the router
auto-registers as an A record per current DHCP lease). Either name works,
and the chain self-heals on DHCP renewal — see [[feedback-dns-record-cname-pattern]]
for why.

---

## File layout

```
services/dashboard/
├── docker-compose.yml          # Homepage container + HOMEPAGE_ALLOWED_HOSTS
├── config/
│   ├── settings.yaml           # title, theme, layout (column counts)
│   ├── services.yaml           # tile groups & individual tiles
│   ├── widgets.yaml            # top-of-page widgets (datetime, search)
│   ├── bookmarks.yaml          # empty by default — bottom list looks bad
│   ├── docker.yaml             # empty stub — no socket discovery
│   └── kubernetes.yaml         # empty stub
├── terraform/                  # VM + DNS CNAME (uses dns-record module)
└── README.md
```

---

## Hot reload

Homepage **hot-reloads YAML files** when the bind-mounted `./config/` dir
changes. After the deploy workflow rsyncs new config to the VM, the page
updates within a few seconds — **no container restart needed**.

A container restart IS needed only when:
- `docker-compose.yml` changes (env vars, image, ports, volumes)
- You change the bind mount itself
- You explicitly want to pull a newer `latest` image

The deploy workflow handles both cases idempotently (`docker compose pull
&& up -d` is a no-op if nothing's changed).

---

## Adding or editing a tile

The 90% case. Edit `services/dashboard/config/services.yaml` and add an
entry under the appropriate group:

```yaml
- Infrastructure:
    - My new service:
        href: http://my-service.lan:8080
        description: One short line
        icon: my-service.svg
```

Then commit + push. The `deploy-dashboard` workflow auto-triggers on
`services/dashboard/config/**` changes, rsyncs the file, hot-reload picks
it up within seconds.

**Tips:**
- Keep `description` to ~3 words. Long text wraps awkwardly and looks bad.
- Group order matches `layout:` in `settings.yaml` — adjust there if you
  want a different order.
- Adding a new group? You must add BOTH a section in `services.yaml` AND
  a row in `settings.yaml`'s `layout:` block (with `style: row` and a
  `columns:` count) — otherwise the group appears but with default
  styling.

---

## Icon resolution — known-good name table

Homepage resolves icon names via this chain:

1. `mdi-<name>` → Material Design Icons (always works; full set at
   [pictogrammers.com/library/mdi](https://pictogrammers.com/library/mdi/))
2. `si-<name>` → Simple Icons (brand logos; full set at
   [simpleicons.org](https://simpleicons.org/))
3. `<name>.svg` (or `.png`) → [homarr-labs/dashboard-icons](https://github.com/homarr-labs/dashboard-icons)
4. Full URL (`https://...`) → fetched directly; use for icons missing
   from the above sets, e.g. selfhst CDN

**Verified names for services we deploy** (don't guess — these tripped
us up before):

| Service | Correct name | Notes |
|---|---|---|
| Proxmox | `proxmox.svg` | dashboard-icons |
| Technitium DNS | `technitium.svg` | dashboard-icons (404 but Homepage falls back gracefully) |
| Nginx Proxy Manager | `nginx-proxy-manager.svg` | dashboard-icons |
| Infisical | `infisical.svg` | dashboard-icons |
| Grafana | `grafana.svg` | dashboard-icons |
| Prometheus | `prometheus.svg` | dashboard-icons |
| Loki | `loki.svg` | NOT `grafana-loki.svg` |
| Tempo | `tempo.svg` | NOT `grafana-tempo.svg` |
| GitHub (Actions) | `github.svg` | NOT `github-actions.svg` |
| OpenWebUI | `open-webui.svg` | NOT `openwebui.svg` (hyphen matters) |
| LiteLLM | `https://cdn.jsdelivr.net/gh/selfhst/icons/svg/litellm.svg` | Not in dashboard-icons; selfhst direct URL |
| Generic router | `mdi-router-wireless` | Material Design |
| Generic robot/AI placeholder | `mdi-robot-happy-outline` | Material Design |

**Verification trick:** before adding any icon, curl it to confirm it
exists (no auth, ~50ms):

```bash
# dashboard-icons
curl -s -o /dev/null -w "%{http_code}\n" \
  "https://raw.githubusercontent.com/homarr-labs/dashboard-icons/main/svg/<name>.svg"
# selfhst
curl -s -o /dev/null -w "%{http_code}\n" \
  "https://cdn.jsdelivr.net/gh/selfhst/icons/svg/<name>.svg"
```

200 means use it, 404 means try a different name or set.

**Symptom of a broken icon:** tile renders with the text "logo" as a
placeholder where the icon should be. Refresh-cache (Ctrl+F5) after
fixing — Homepage caches resolved icon URLs aggressively.

---

## HOMEPAGE_ALLOWED_HOSTS — host validation

Homepage rejects requests whose `Host` header isn't on the list with
**"Host validation failed. See logs for more details."** in a red banner.

Current value (in `docker-compose.yml`):

```yaml
- HOMEPAGE_ALLOWED_HOSTS=dashboard.lan:3000,dashboard.lan,dashboard-vm.lan:3000,dashboard-vm.lan
```

**Rules:**
- Comma-separated, no spaces
- Port suffix MAY be required — browsers send `host:3000` when the port
  isn't `:80`/`:443`, so include both with-port and without-port for each
  name
- `localhost:3000` and `127.0.0.1:3000` are always implicitly allowed
- `*` disables the check entirely (docs discourage, but acceptable for
  LAN-only homelab)

**Use hostname-only entries, not IP entries.** IP entries go stale every
time DHCP renews and gives the VM a different address (the cost of not
having a router-side reservation). Hostnames are stable because of the
CNAME → router-A chain.

If you ever expose this through a reverse proxy on `:80`/`:443`, add
`dashboard.lan:80` and/or `dashboard.lan:443` (or the proxy's hostname)
to the list. Homepage matches the literal `Host` header.

---

## Layout (settings.yaml)

The `layout:` block controls group order AND column count per group:

```yaml
layout:
  Infrastructure:
    style: row
    columns: 5      # max tiles per row; wraps to next row when exceeded
  AI:
    style: row
    columns: 4
```

- `style: row` is the default (tiles flow left-to-right). `style: column`
  stacks tiles vertically, useful when a group has only one tile and
  shouldn't be in a tiny corner of a wide row.
- `columns:` is the max per row. If group has fewer tiles, they don't
  stretch — they sit at the natural tile width.
- Groups not listed in `layout:` still render but with default settings.

The `target: _blank` setting in `settings.yaml` opens tile clicks in a
new tab — useful when the dashboard is your start page.

---

## Widget integrations (not yet wired up)

Homepage supports widget integrations for many of the services we link
to: Proxmox CPU graphs, Grafana dashboard counts, Technitium QPS,
GitHub Actions run status, etc. **Not done in v1** — would need:

1. Create API tokens in each source service
2. Store under `/dashboard/` path in Infisical
3. Add a step to `deploy-dashboard.yml` that loads `/dashboard/`
4. Use Homepage's `{{HOMEPAGE_VAR_<NAME>}}` placeholder substitution
   in `services.yaml` to inject the tokens
5. Add the `widget:` block to the relevant service entry per
   [gethomepage.dev/widgets](https://gethomepage.dev/widgets/)

The Infisical machine identity for homelab-services would need read on
`/dashboard/` — add when wiring this up.

---

## Troubleshooting decision tree

| Symptom | Likely cause | Fix |
|---|---|---|
| Tile shows "logo" text | Icon name not found | Verify with curl trick, switch to correct name or full URL |
| "Host validation failed" red banner | Host header not on allowlist | Add to `HOMEPAGE_ALLOWED_HOSTS` in docker-compose.yml — include port suffix |
| Tile click goes to dead address | Backing service moved (DHCP, port change) | Update `href:` in services.yaml; if the target was a VM, consider switching to its `<name>-vm.lan` form for self-healing |
| Config change didn't show up | Workflow hasn't run / hot-reload didn't trigger | Check `gh run list --workflow=deploy-dashboard.yml --limit 1`; if completed-green and still not showing, Ctrl+F5 to bust browser cache |
| Group missing or unstyled | Forgot `layout:` row | Add a `<group name>:` block to `settings.yaml` with `style:` and `columns:` |
| Long description wraps weird | Description too long | Shorten to ≤3 words |

---

## Related

- [[project-homelab-dns]] — DNS architecture (dashboard.lan resolves via CNAME)
- [[feedback-dns-record-cname-pattern]] — why dashboard uses CNAME, not pinned A
- `homelab-bootstrap` skill — broader homelab gotchas (provider env-direct, Infisical OIDC, runner zombie recovery)
- `technitium-api` skill — for verifying DNS records when troubleshooting tile URLs
- Upstream docs: [gethomepage.dev](https://gethomepage.dev/)
