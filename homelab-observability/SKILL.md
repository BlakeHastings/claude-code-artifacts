---
name: homelab-observability
description: >-
  Operational knowledge for the homelab LGTM stack (Grafana, Loki, Tempo,
  Prometheus on observability-vm). Use when adding a Prometheus scrape
  target, adding or modifying a Grafana dashboard or data source,
  troubleshooting why a VM's logs aren't reaching Loki, fixing Alloy
  label issues, tweaking Tempo config, wiring up trace pipelines,
  scraping LiteLLM /metrics with bearer-token auth, configuring LiteLLM
  OTLP push to Tempo, debugging missing traces or stale Tempo search
  results, building dashboards via the Python generator pattern, or
  understanding what runs where in the metrics/logs/traces stack.
  Covers the specific homelab deployment in
  homelab-services/services/observability/ + the Alloy + Node Exporter
  install in homelab-platform/ansible/base-vm.yml + the LiteLLM
  telemetry wiring in local-inference-infrastructure, not general LGTM
  usage.
allowed-tools: Read, Edit, Write, Bash, Grep, Glob
---

# Homelab Observability

The LGTM stack on `observability-vm`. **Grafana UI: http://observability-vm.lan:3000** (admin/changeme on first login). Source of truth: `homelab-services/services/observability/`.

DNS: `observability.lan` is a Terraform-managed CNAME → `observability-vm.lan` (router-auto-registered, follows DHCP). All clients ship to `observability-vm.lan:3100` and survive DHCP renewals — see [[feedback-dns-record-cname-pattern]].

---

## What runs where

```
On every VM (installed by homelab-platform/ansible/base-vm.yml)
  ├── Node Exporter (host network)    :9100   → metrics, scraped by Prometheus
  └── Grafana Alloy (bridge)          :12345  → ships container logs + journal to Loki
                                              (bound to 127.0.0.1 only — debug UI)

On observability-vm (docker-compose in services/observability/)
  ├── Grafana        :3000           → unified UI, dashboards, alerting
  ├── Prometheus     :9090           → metrics store (bound 127.0.0.1)
  ├── Loki           :3100           → log store (bound 0.0.0.0)
  └── Tempo          :3200, 4317-4318 → traces (HTTP API + OTLP gRPC + OTLP HTTP)
```

Data sources + the Node Exporter Full dashboard auto-load on Grafana startup — see "Declarative provisioning" below.

---

## File layout

```
services/observability/
├── docker-compose.yml            # LGTM stack
├── prometheus.yml                # scrape targets — add VMs here
├── grafana/
│   └── provisioning/
│       ├── datasources/
│       │   └── datasources.yaml  # Prometheus, Loki, Tempo (pinned UIDs)
│       └── dashboards/
│           ├── dashboards.yaml   # file-provider — scans for .json
│           └── node-exporter-full.json  # 31-panel dashboard (id 1860)
└── terraform/                    # VM provisioning + DNS CNAME
```

---

## Adding a Prometheus scrape target

Edit `services/observability/prometheus.yml`, add an entry under `scrape_configs`. Use the `.lan` hostname (resolves via Technitium, survives DHCP):

```yaml
- job_name: 'my-new-vm'
  static_configs:
    - targets: ['my-new-vm.lan:9100']
      labels:
        node: 'my-new-vm'
```

Push → `Deploy observability stack` auto-triggers → Prometheus container restarts → new target appears within ~15s. Verify:

```bash
curl -s -u admin:changeme \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/prometheus/api/v1/targets?state=any" \
  | python -c "import sys,json; [print(f'  {t[\"labels\"][\"job\"]:20} {t[\"health\"]}') for t in json.load(sys.stdin)['data']['activeTargets']]"
```

For GPU nodes add a second entry on `:9400` for DCGM exporter (not installed by base-vm.yml — needs project-specific Ansible).

### When the service is behind UFW

Node Exporter (`:9100`) and DCGM (`:9400`) on every VM accept any LAN source, so adding a scrape job is enough. But **application-tier `/metrics` endpoints often sit behind a tight UFW rule** that only allows the service's primary client (e.g. llama-server on the inference hosts is locked to the gateway IP `192.168.0.5`). Adding a scrape job while UFW still blocks observability-vm produces a target that shows `health=down` with `connection refused` / `i/o timeout` in Grafana, despite the service responding fine to its real client.

The fix: in the playbook that opens the service's port to its primary client, add a parallel `ufw allow` rule with `from_ip: {{ observability_server_ip }}` (a var pinned to `192.168.0.23` in each project's `ansible/vars/main.yml`). Example from `local-inference-infrastructure/ansible/inference-setup.yml`:

```yaml
- name: "[llama-server] UFW: allow gateway → llama-server port"
  community.general.ufw:
    rule: allow
    from_ip: "{{ litellm_gateway_ip }}"      # primary caller
    port: "{{ item.engine_args.port | default(8080) }}"

- name: "[llama-server] UFW: allow observability-vm → llama-server port"
  community.general.ufw:
    rule: allow
    from_ip: "{{ observability_server_ip }}" # Prometheus scrape
    port: "{{ item.engine_args.port | default(8080) }}"
```

Same pattern for TTS gateway, SearXNG, Playwright, future services. The moment you add a scrape job for a port that's UFW-restricted, audit whether the playbook also lets observability-vm in.

### Don't be misled by local repo `configs/prometheus/` folders

`local-inference-infrastructure/configs/prometheus/scrape-targets.yaml` **isn't loaded by anything** — it's a stale seed file from the pre-May-2026 single-machine era. The first lines literally read "Add this block to your central Prometheus prometheus.yml under scrape_configs:". The only file that actually controls scrape config is `homelab-services/services/observability/prometheus.yml`. If you find a similar `configs/prometheus/` folder in another homelab repo, verify it's not also dead documentation before editing.

---

## Adding a Grafana dashboard (declarative)

1. Find or build a dashboard. For [grafana.com](https://grafana.com/grafana/dashboards/) dashboards:

   ```bash
   curl -sL -o services/observability/grafana/provisioning/dashboards/<name>.json \
     "https://grafana.com/api/dashboards/<ID>/revisions/latest/download"
   ```

2. **Replace data source placeholders.** Provisioned dashboards don't go through the UI's "Import" templating step, so `${DS_PROMETHEUS}`-style placeholders stay literal and break the panels. Pin to the data source UIDs we provision (`prometheus`, `loki`, `tempo`):

   ```bash
   python -c "
   import json, pathlib
   p = pathlib.Path('services/observability/grafana/provisioning/dashboards/<name>.json')
   j = json.loads(p.read_text())
   s = json.dumps(j).replace('\${DS_PROMETHEUS}', 'prometheus').replace('\${DS_LOKI}', 'loki').replace('\${DS_TEMPO}', 'tempo')
   j = json.loads(s)
   j['id'] = None   # required: Grafana assigns a new id on load
   p.write_text(json.dumps(j, indent=2))
   "
   ```

3. Commit + push. `Deploy observability stack` auto-triggers; Grafana's file provider sees the new JSON within 30s and loads it.

The Node Exporter Full dashboard (`node-exporter-full.json`) is the reference example — Prometheus UID `prometheus`, id set to null, panels rendering without UI interaction.

---

## Adding a data source

Edit `grafana/provisioning/datasources/datasources.yaml`. Pin the UID so future dashboards can hardcode it.

```yaml
- name: My DB
  uid: mydb
  type: postgres
  access: proxy
  url: my-db.lan:5432
  jsonData: {sslmode: disable}
  secureJsonData: {password: ${MY_DB_PASSWORD}}   # use HOMEPAGE-style env injection
```

Restart Grafana (`docker compose restart grafana`) or just dispatch deploy-observability — the `--force-recreate` step picks it up. Data source visible in **Connections → Data Sources** immediately, no UI clicks.

For secrets — `secureJsonData` accepts Grafana's env-var interpolation `${ENV_VAR}` at runtime. The plumbing for `/grafana/` secrets in Infisical isn't wired up yet (Phase 4 from the obs setup).

---

## Container UIDs (data dir ownership)

LGTM containers run as non-root and bind-mount data dirs. Wrong perms → CrashLoop on startup. Deploy workflow chowns each one:

| Service | UID | GID |
|---|---|---|
| Grafana | 472 | 472 |
| Prometheus | 65534 (nobody) | 65534 |
| Loki | 10001 | 10001 |
| Tempo | 10001 | 10001 |

If you add a service that crashloops on first deploy, this is the first thing to check. See `deploy-observability.yml` for the chown pattern.

---

## Alloy — log shipping config

Template: `homelab-platform/ansible/templates/alloy-config.j2`. Rendered per-VM by Ansible at provision time. Key bits:

- **`node` label** uses `{{ vm_name | default(ansible_hostname) }}` so Loki streams are labeled by the VM's authoritative name (passed from the workflow input), not the Alloy container's random hostname AND not the VM's drifted system hostname. Three earlier bugs hit this surface:
  1. Original template used `constants.hostname` → in bridge networking, that's the random container ID → all VMs labeled with hex strings.
  2. First fix used `ansible_hostname` → but searxng-gateway's `/etc/hostname` had drifted to `terraform-runner` → its logs landed under a wrong VM's label silently.
  3. Current fix uses `vm_name` from the workflow input → always correct. See `homelab-bootstrap` skill's "ansible_hostname can drift" section for the three-layered defense (workflow → hostname enforcement → template fallback).
- **Loki endpoint** comes from the `observability_server_ip` workflow input — pass `observability-vm.lan` (hostname-based, survives DHCP) rather than an IP literal.
- **Restart on config change**: `base-vm.yml`'s template task notifies the `restart alloy` handler. The container doesn't auto-restart on bind-mount file content changes (see `homelab-bootstrap` skill's "Ansible docker_container idempotency" section).

**Inference hosts use a separate Alloy config** at `local-inference-infrastructure/configs/alloy/alloy-config.alloy.j2` — physical machines (`ubuntu-tower`, `ubuntu-tower-02`), not Proxmox VMs, so they're outside the `base-vm.yml` flow. That config is rendered by `local-inference-infrastructure/ansible/deploy-alloy.yml` (uses `template:` — must do, since the file has Jinja placeholders) and dispatched by the matrix-based `.github/workflows/deploy-alloy.yml`. The `node` label there is set to the matrix host's logical name (`inference-01`, `inference-02`) — same drift-resistant pattern as the VM `vm_name` approach. **Gotcha that bit us**: the original file was named `.alloy` and copied with `copy:`, shipping literal placeholders verbatim. See `homelab-bootstrap` skill's "copy: ships files verbatim" section.

To verify Alloy is shipping correctly-labeled from a given VM:

```bash
curl -s -u admin:changeme \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/loki/loki/api/v1/label/node/values" \
  | python -c "import sys,json; print(json.load(sys.stdin).get('data'))"
```

Expected: a list of distinct VM names matching what's provisioned. Red flags:
- **Random hex strings** = container IDs leaking through (template using `constants.hostname`)
- **Wrong VM name** = hostname drift (template using `ansible_hostname` while OS hostname is wrong)
- **A VM in the expected list is missing volume** = it might be shipping under one of the *other* labels you DO see; cross-check with `count_over_time({node="<suspect>"}[5m])` (see "Debugging missing-looking telemetry" below)

---

## DNS for `.lan` from inside VMs

Alloy on each VM needs to resolve `observability-vm.lan`. `base-vm.yml`
writes a systemd-resolved drop-in (`/etc/systemd/resolved.conf.d/technitium.conf`)
that routes only `.lan` queries to Technitium:

```ini
[Resolve]
DNS=192.168.0.250    # dns-vm; on dns-vm itself this is 127.0.0.1
Domains=~lan         # only .lan queries go here; everything else uses defaults
```

If a VM was provisioned before this dropin was added, re-provision to apply. The dropin is idempotent — Ansible's template task triggers a `restart systemd-resolved` + `restart alloy` handler only when content changes.

---

## Tempo config — known gotcha

The compose file's embedded Tempo config used to have a `compactor:` block. Current Tempo rejects it:

```
field compactor not found in type app.Config
```

Removed in `services/observability/docker-compose.yml`. Tempo uses defaults — fine for now. If you tune compaction later, the field probably moved under a different parent or got renamed; check Tempo upstream docs for current schema.

Same shape of error would apply to other LGTM components — when an upstream image rejects a config field, the fastest path is "remove the block, run with defaults, revisit when you actually need to tune it."

---

## Deploy workflow shape

`.github/workflows/deploy-observability.yml` does more than the obvious. Reads top-to-bottom:

1. **Checkout**
2. **Ensure runner can resolve .lan** — writes the same systemd-resolved dropin on terraform-runner itself; necessary because workflows ssh to `<vm>.lan` later
3. **Load Ansible secrets from Infisical**
4. **Get VM IP from Terraform state** — soon-to-be-legacy; future deploys should use `observability-vm.lan` directly
5. **Write SSH key**
6. **Create service + data dirs + chown** — runs BEFORE rsync (the rsync-before-mkdir bug from `homelab-bootstrap` skill)
7. **Sync service files via rsync**
8. **Pull and start stack** — `docker compose pull && docker compose up -d --force-recreate` (force-recreate ensures bind-mount edits take effect)
9. **Wait + report container state** — sleeps 15s, runs `ps -a`, tails logs from any non-running service. **Also probes `/api/v1/targets`** and prints `health` + `lastError` for auth'd scrape jobs (`pve`, `litellm`). Container-running ≠ scrape-working — a 403 on the upstream API leaves the container "healthy" but the metric pipeline broken. When the `pve` job is unhealthy, the step **also dumps `docker compose logs --tail 40 pve-exporter`** because the exporter's wrapped exception names the actual Proxmox 4xx + path (e.g. `Permission check failed (/, Sys.Audit)`)
10. **Refresh Alloy on known client VMs** — SSH to dns-vm.lan, dashboard-vm.lan, observability-vm.lan; restart alloy on each; tail post-restart logs
11. **Cleanup SSH key**

The "client VMs" list in step 10 is static — add new VMs there as they come online with Alloy.

---

## First-time admin password (not yet declarative)

Hardcoded as `changeme` in `docker-compose.yml`. Change via UI on first login.

**Phase 4 follow-up**: move to Infisical at `/grafana/` path, inject via env var. Deploy workflow loads it, sets `GF_SECURITY_ADMIN_PASSWORD` at container start. Then a tear-down + redeploy recreates with the correct password automatically.

---

## Quick verification commands

```bash
# Stack health
for svc in "grafana:3000/api/health" "prometheus:9090/-/ready" "loki:3100/ready" "tempo:3200/ready"; do
  name=$(echo "$svc" | cut -d: -f1)
  path=$(echo "$svc" | cut -d: -f2-)
  status=$(curl -s -o /dev/null -w "%{http_code}" -m 5 "http://observability-vm.lan:$path" 2>&1)
  echo "$name → $status"
done

# Scrape targets
curl -s -u admin:changeme \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/prometheus/api/v1/targets" \
  | python -c "import sys,json; [print(f'  {t[\"labels\"][\"job\"]:20} {t[\"scrapeUrl\"]:50} {t[\"health\"]}') for t in json.load(sys.stdin)['data']['activeTargets']]"

# Log volume per node, last 5min
curl -s -u admin:changeme --get \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/loki/loki/api/v1/query" \
  --data-urlencode 'query=sum by (node, job) (count_over_time({node=~".+"}[5m]))' \
  | python -c "import sys,json; [print(f'  node={x[\"metric\"][\"node\"]:20} job={x[\"metric\"][\"job\"]:10} count={x[\"value\"][1]}') for x in json.load(sys.stdin)['data']['result']]"
```

---

## Debugging missing-looking telemetry — check labels first

Before assuming a node isn't shipping, **list the actual label values**:

```bash
# Loki: what node names exist?
curl -s -u admin:changeme \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/loki/loki/api/v1/label/node/values" \
  | python -c "import sys,json; print(sorted(json.load(sys.stdin).get('data',[])))"

# Loki: log volume per node in the last 15 min (catches sparse but present nodes)
curl -s -u admin:changeme --get \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/loki/loki/api/v1/query" \
  --data-urlencode 'query=sum by (node) (count_over_time({node=~".+"}[15m]))' \
  | python -c "import sys,json; [print(f'  {x[\"metric\"][\"node\"]:22} {x[\"value\"][1]:>6}') for x in sorted(json.load(sys.stdin)['data']['result'], key=lambda r: r['metric']['node'])]"

# Prometheus: scrape targets + per-target health
curl -s -u admin:changeme \
  "http://observability-vm.lan:3000/api/datasources/proxy/uid/prometheus/api/v1/targets?state=any" \
  | python -c "import sys,json; [print(f'  {t[\"labels\"][\"job\"]:20} {t[\"health\"]}') for t in json.load(sys.stdin)['data']['activeTargets']]"
```

Common patterns when a VM "isn't shipping":

| What you see | What's actually happening |
|---|---|
| VM name absent, total volume normal | Logs landing under a different (wrong) label — usually a hostname-drift case. Look for unexpected names with significant volume. |
| VM name absent, hex-string labels with volume | Alloy template still uses `constants.hostname` (container ID) instead of `vm_name`. |
| VM name present but stale volume | Alloy reached Loki at least once but stopped — DNS resolution broke, network ACL, container crashed. Check `docker logs alloy` via the deploy workflow's refresh-alloy step (it tails logs per VM). |
| VM name absent, no candidate elsewhere | Alloy genuinely not running OR can't reach Loki. SSH in, `docker ps`, `docker logs alloy`, `docker exec alloy getent hosts observability-vm.lan`. |

See [[feedback-telemetry-label-check]] for the durable habit and rationale.

---

## Troubleshooting decision tree

| Symptom | Likely cause | Where to look |
|---|---|---|
| Grafana 302 → /login but no dashboards | Provisioning files not mounted | docker-compose volumes; container logs |
| Dashboard panels say "Data source not found" | UID mismatch | `datasources.yaml` UIDs vs dashboard JSON `datasource.uid` |
| "Failed to import dashboard" on Grafana start | `id` field set in JSON | python rewrite to `j['id'] = None` |
| Prometheus target `down` | Hostname doesn't resolve OR scrape URL wrong | `nslookup <target> 192.168.0.250`; container logs for Prometheus |
| Prometheus target `down` with `dial tcp: lookup <host>.lan on 127.0.0.11:53: no such host` | Host has a static OS-level IP, not a DHCP lease → router never auto-registers it in Technitium | Add explicit A record to `homelab-services/services/dns/terraform-config/variables.tf` `a_records` map (same as `pve.lan`, `infisical-vm.lan`). Push → `configure-dns` auto-applies. See `homelab-bootstrap` skill's "static-IP hosts don't get DHCP auto-DNS-registration" section. |
| Prometheus self-scrape `down` for `localhost:9100` | Container's localhost ≠ host | use `observability-vm.lan:9100` instead |
| Loki has no `node` labels OR labels are container IDs | Alloy template not re-rendered, or uses `constants.hostname` | re-provision affected VMs; verify template uses `vm_name` (the current pattern), not `ansible_hostname` (drift-prone) or `constants.hostname` (container ID) |
| A VM's logs are missing but another VM's `node` label has unexpectedly high volume | Hostname drift — VM's `/etc/hostname` mismatches its `vm_name`; old template using `ansible_hostname` baked the wrong name in. | Re-provision; the current `base-vm.yml` enforces hostname matches `vm_name` and the Alloy template uses `vm_name` directly. See `homelab-bootstrap` skill's "ansible_hostname can drift" section. |
| Loki has zero log streams | Alloy can't resolve `observability-vm.lan` OR Alloy not running | check systemd-resolved dropin on the VM; `docker ps` for alloy |
| Tempo CrashLooping after a Tempo image upgrade | Config schema field rejected | `docker logs tempo`; remove the rejected field, run with defaults |
| All containers Restarting after first deploy | Bind-mount UID ownership | chown to UIDs in table above |
| Containers stay Restarting after config fix | `up -d` didn't recreate | `docker compose up -d --force-recreate` |
| Tempo `/api/search` returns 0 traces but `tempo_receiver_accepted_spans` > 0 | Default Tempo search window is short (~1h); old traces fall off | Pass explicit `start`/`end` params (`?start=...&end=...`); dashboard panels honor the picker so they're fine |
| LiteLLM (or any new OTel-instrumented app) just deployed, traces not visible yet | OTel SDK batches before flushing (default ~5s / 512 spans) | Wait a beat, or trigger a real handler endpoint — /health usually isn't instrumented |
| LiteLLM Prom scrape `down` with `unable to read authorization credentials: ... permission denied` | Token file is correct, but secrets *directory* is too tight (e.g. 0700) for Prom UID 65534 to traverse | `chmod 0711` on the dir; file stays 0440 owned by 65534. See `homelab-bootstrap` "Bind-Mounted Secrets" section |
| `pve` scrape `down` with `HTTP status 500 INTERNAL SERVER ERROR` | pve-exporter is wrapping an upstream 4xx — usually a Proxmox 403 due to missing perm | Deploy workflow dumps exporter logs automatically; look for `proxmoxer.core.ResourceException: 403 ... (<path>, <perm>)`. If it's `(/, Sys.Audit)` → token privsep trap, see `proxmox-api` skill |
| Re-deploy fails `tee: <file>: Permission denied` writing a secret file owned by ubuntu mode 0400 | Plain `tee` (running as ubuntu) can't truncate a file with no owner-write bit; the previous deploy created it 0400 | Use `sudo tee ... && sudo chown ubuntu:ubuntu ... && sudo chmod 0400 ...`. See [[feedback-redeployable-secret-files]] |

---

## LiteLLM — full telemetry wiring

The LiteLLM proxy (on `litellm-gateway`) is wired into all three pillars.
Each is independent and answers different questions:

| Layer | Where it lives | Best for |
|---|---|---|
| **Container logs** (Loki) | `{node="litellm-gateway"}` | "What did LiteLLM say when this broke?" raw stderr/stdout |
| **OTLP traces** (Tempo) | `service.name="litellm"` | "Where did this one request spend its time?" per-request span tree |
| **App metrics** (Prometheus) | `job="litellm"`, `litellm_*` metric names | "How is the proxy *behaving over time* by model?" aggregates, percentiles, per-model breakdowns |

### How traces flow

LiteLLM pushes OTLP HTTP to Tempo via an OTel callback:

```yaml
# configs/litellm/settings.yaml (local-inference-infrastructure)
litellm_settings:
  callbacks: ["prometheus", "otel", "lmstudio_context_hook.proxy_handler_instance"]
```

```bash
# In the container env, written by provision-litellm-gateway → templates/litellm.env.j2
OTEL_EXPORTER_OTLP_ENDPOINT=http://observability-vm.lan:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
```

The env block in the template is conditionally rendered ONLY when
`observability_server_ip` is passed as an Ansible var. Without it, the
otel callback can still be in the callbacks list — but the OTel SDK
retries forever to reach an empty endpoint, **delaying LiteLLM startup
by 30+ seconds.** If you ever take observability-vm down without
removing `otel` from the callbacks list, expect slow LiteLLM restarts.

### How Prom scrape flows (auth dance)

LiteLLM's `/metrics` is gated behind the master key. To scrape:

1. Master key lives in Infisical at `/litellm/LITELLM_MASTER_KEY` in the
   shared `homelab` project (duplicated from the litellm-specific
   project — same value, easier than cross-project access).
2. `deploy-observability.yml` loads it as `LITELLM_MASTER_KEY` env var
   via the secrets-action.
3. A workflow step SSHes into `observability-vm`, writes the token to
   `~/services/observability/secrets/litellm-token` (mode `0440`, owned
   by 65534 = Prom). Secret piped via stdin → `sudo tee` so it never
   appears in argv or logs. **Parent dir must be `0711`** so Prom UID
   can traverse (see `homelab-bootstrap` skill).
4. `docker-compose.yml` bind-mounts `./secrets:/etc/prometheus/secrets:ro`.
5. `prometheus.yml` references via:
   ```yaml
   authorization:
     type: Bearer
     credentials_file: /etc/prometheus/secrets/litellm-token
   ```

### Useful `litellm_*` metric names

There are 54 series total. The most informative for "is the proxy
healthy" dashboards:

| Metric | What it measures |
|---|---|
| `litellm_in_flight_requests` | Current concurrent requests (gauge) |
| `litellm_deployment_success_responses_total` | Cumulative successful requests, labelled by `model`, `requested_model`, `api_provider` |
| `litellm_deployment_failure_responses_total` | Cumulative failures, same labels |
| `litellm_llm_api_latency_metric_bucket` | Full request latency histogram, labelled by `model`. Use `histogram_quantile(0.95, sum by (model, le) (rate(...[5m])))` for p95 |
| `litellm_llm_api_time_to_first_token_metric_bucket` | TTFT histogram (streaming UX signal) |
| `litellm_input_tokens_metric_total` / `..._output_tokens_metric_total` | Tokens in/out per request, labelled by `model`, `user`, `team` |
| `litellm_spend_metric_total` | Per-user/per-team cost (LiteLLM-tracked) |
| `litellm_deployment_state` | Per-backend health (use to spot a slow LM Studio host) |
| `litellm_cache_misses_metric_total` | Useful when caching is configured |

**Key labels to slice by:**
- `model` — the actually-routed model (e.g. `claude-sonnet-4-6`, `local-reasoning`)
- `requested_model` — what the client asked for (e.g. `anthropic_auto`)
- `api_provider` — `anthropic`, `openai`, etc.

---

## Proxmox host + per-VM metrics — `pve-exporter` sidecar

Proxmox doesn't expose Prometheus metrics natively. We run
[`prompve/prometheus-pve-exporter`](https://github.com/prometheus-pve/prometheus-pve-exporter)
as a compose sidecar on `observability-vm`. Prometheus scrapes the exporter,
the exporter queries the PVE API.

### Pieces

```yaml
# docker-compose.yml
pve-exporter:
  image: prompve/prometheus-pve-exporter:latest
  restart: unless-stopped
  ports:
    - "127.0.0.1:9221:9221"   # internal only — Prometheus on same host
  env_file:
    - ./secrets/pve.env
```

```yaml
# prometheus.yml — bridge scrape
- job_name: 'pve'
  metrics_path: /pve
  params:
    target: ['pve.lan']         # which PVE host to query
  static_configs:
    - targets: ['pve-exporter:9221']   # docker network alias
      labels:
        node: 'pve'
```

```ini
# secrets/pve.env  (mode 0400, owned by ubuntu; written by deploy workflow)
PVE_USER=terraform@pam
PVE_TOKEN_NAME=ci-token
PVE_TOKEN_VALUE=<UUID>
PVE_VERIFY_SSL=false
```

The deploy workflow loads `/proxmox/PROXMOX_VE_API_TOKEN` from Infisical
(format `user@realm!tokenid=uuid`), splits it into the three env fields, and
writes the env file via `sudo tee` (NOT plain `tee`) — see
[[feedback-redeployable-secret-files]].

### Useful exposed metrics

| Metric | Notes |
|---|---|
| `pve_up{id="..."}` | 1 / 0 per node / VM / storage |
| `pve_cpu_usage_ratio{id="..."}` | 0–1 |
| `pve_memory_usage_bytes{id="..."}` / `pve_memory_size_bytes{id="..."}` | divide for ratio |
| `pve_disk_usage_bytes{id="storage/..."}` / `pve_disk_size_bytes{id="storage/..."}` | per storage pool |
| `pve_network_receive_bytes{id="qemu/<vmid>"}` / `pve_network_transmit_bytes{id="qemu/<vmid>"}` | counters |
| `pve_guest_info{id="qemu/<vmid>", name="<vm-name>", node="<node>"}` | label-only series for joining |

To get per-VM CPU labeled by *name* (not vmid), join on `pve_guest_info`:

```promql
pve_cpu_usage_ratio{id=~"qemu/.+"} * on(id) group_left(name) pve_guest_info
```

### Permission setup on Proxmox

Tokens need `Sys.Audit` on `/` (PVEAuditor covers it). **Token privsep
trap** — see `proxmox-api` skill's "API token permissions" section. The
short version: if `privsep=0`, you must grant on the **user**, not the
token. The wrong-place grant produces an identical 403 forever.

### Adding a second PVE host

The exporter is multi-tenant. Add another scrape with a different
`target` param and a different `node` label — same sidecar serves both.
Set `PVE_USER` / `PVE_TOKEN_NAME` / `PVE_TOKEN_VALUE` for *each*
endpoint in `pve.env` if hosts have different tokens (see exporter
docs for the `<endpoint_name>_user=…` syntax).

---

## Technitium DNS metrics — `technitium-exporter` sidecar

Same compose-sidecar pattern as pve-exporter. Image:
`ghcr.io/guycalledseven/technitium-dns-prometheus-exporter:latest` —
combines window-based snapshots (top domains, top clients, blocked,
DHCP) with v15+ lifetime counters. Listens on `:9105/metrics`.

### Pieces

```yaml
# docker-compose.yml
technitium-exporter:
  image: ghcr.io/guycalledseven/technitium-dns-prometheus-exporter:latest
  restart: unless-stopped
  ports:
    - "127.0.0.1:9105:9105"
  env_file:
    - ./secrets/technitium.env
```

```yaml
# prometheus.yml
- job_name: 'technitium'
  static_configs:
    - targets: ['technitium-exporter:9105']
      labels:
        node: 'dns-vm'
```

```ini
# secrets/technitium.env  (sudo-tee'd, 0400 owned by ubuntu)
TECHNITIUM_BASE_URL=http://dns-vm.lan:5380
TECHNITIUM_TOKEN=<from Infisical /technitium/TECHNITIUM_TOKEN>
TECHNITIUM_VERIFY_SSL=false
TECHNITIUM_STATS_RANGE=LastHour
TECHNITIUM_TOP_LIMIT=25
SERVER_LABEL=dns-vm
```

The token at Infisical `/technitium/TECHNITIUM_TOKEN` is the same one the
`kenske/technitium` Terraform provider and the `technitium-api` skill use —
admin scope, more than enough for the exporter's read-only API calls.
Best practice would be a dedicated read-only token; not done here for
expedience and because the homelab's blast radius is tiny.

### Useful exposed metrics

| Metric | Notes |
|---|---|
| `technitium_up` | API reachability (use as the health stat) |
| `technitium_dns_queries_window{category=…}` | gauge over the configured window. Categories: `all`, `no_error`, `nxdomain`, `servfail`, `refused`, `authoritative`, `recursive`, `cached`, `blocked`, `dropped` |
| `technitium_dns_protocol_queries{protocol=…}` | per-protocol traffic (Udp, Tcp, Tls, Https, …) |
| `technitium_dns_top_blocked_domain_hits{domain=…}` | top blocked domains over the window — pair with `topk(N, …)` |
| `technitium_dns_top_client_hits{client_ip, client_name}` | top clients over the window |
| `technitium_dns_top_domain_hits{domain=…}` | top queried domains overall |
| `technitium_dns_zones`, `technitium_dns_cached_entries`, `technitium_dns_blocklist_zones`, `technitium_dns_allowed_zones` | inventory gauges |
| `technitium_dhcp_leases_total{scope, type}` | per-scope DHCP leases |
| `technitium_dns_realtime_queries_total{category}` | **counter** (v15+ only) — use `rate(...[5m])` for true rate-over-time |

Cache hit ratio:
```promql
sum(technitium_dns_queries_window{category="cached"})
/ sum(technitium_dns_queries_window{category="all"})
```
Block ratio: same shape, swap `cached` → `blocked`.

### Companion Grafana dashboard

Dashboard `id=24555` ("Technitium DNS Exporter") on grafana.com is the
companion to this exporter — 33 panels, broad coverage. Drop in via the
standard recipe from "Adding a Grafana dashboard (declarative)" above
(curl download → `${DS_PROMETHEUS}` rewrite → `id=null` → also drop the
`__inputs` / `__requires` blocks, which are UI-import scaffolding and
trip Grafana's file provisioner if left in).

Our custom companion is `dns-network-activity.json` — 19 panels, slim
"morning coffee" view (totals + cache/block ratios + protocol mix + top
blocked + top clients + DHCP). Built via the Python generator pattern.

### UFW reminder

If the deploy probe reports `technitium` `down` with `connection refused`
on first deploy, dns-vm's UFW is blocking observability-vm on :5380.
Open it with the standard pattern from `homelab-platform`'s
`base-vm.yml` / dns playbook — see [[feedback-prometheus-scrape-through-ufw]].
(On our current setup the port was already LAN-open because the user
hits the UI from their workstation, so no UFW change was needed.)

---

## Building dashboards — Python generator pattern

The Local Inference Overview dashboard has 27 panels across 6 rows.
Hand-writing that JSON is hostile (Grafana's schema has a lot of
defaults and per-panel quirks). The pattern that worked:

1. Write a Python script under `.dashboard-gen.py` (gitignored —
   it's a build tool, not source) that emits the final JSON.
2. Use helper functions per panel type (`stat()`, `timeseries()`,
   `logs()`, `trace_table()`) that take the minimum-bothering args
   and fill in sane defaults.
3. Pin data source UIDs (`{"type":"prometheus","uid":"prometheus"}`)
   so the JSON works against our provisioning.
4. Always set `j["id"] = None` — Grafana assigns one on load;
   reusing a stale id causes "Failed to import" on re-provisioning.
5. Layout: 24-col grid. Stat panels w=4-6, timeseries w=8, logs
   w=12, wide tables w=24. Each row's `y` = sum of previous heights.
6. Bump `version` in the dashboard meta when shipping changes (helps
   with Grafana's change-tracking).

To regenerate: `python .dashboard-gen.py` from the repo root, commit
the JSON output, delete the script. Reify the script next time you
need to extend the dashboard (it's a small file, easy to recreate).

The grafana-folder-provider is set to `updateIntervalSeconds: 30` and
`allowUiUpdates: true` — quick iteration loop: edit script → regenerate
→ push → 30s later it's live.

### TraceQL metrics in Grafana panels

Tempo data source query types in Grafana panels:
- `"queryType": "traceqlSearch"` with a `filters` array → table panel of trace search results
- `"queryType": "metrics"` with a TraceQL query string ending in `| rate()` or `| quantile_over_time(...)` → timeseries panel

TraceQL metrics use nanoseconds for duration. Use `"unit": "ns"` on
latency timeseries panels so Grafana auto-formats.

---

## Related

- [[project-homelab-observability]] — what's deployed
- [[project-homelab-dns]] — Technitium serves the `.lan` zone this stack uses
- [[feedback-dns-record-cname-pattern]] — observability.lan is a CNAME, not a pinned A
- `homelab-bootstrap` skill — broader gotchas (localhost-in-container, docker_container handler, systemd-resolved dropin, force-recreate, container UIDs, workflow_dispatch 422, heredoc-in-YAML)
- `homelab-dashboard` skill — dashboard tiles link to the LGTM endpoints
- Upstream docs: [grafana.com/oss](https://grafana.com/oss/) for LGTM components
