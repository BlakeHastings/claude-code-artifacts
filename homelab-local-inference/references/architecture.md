# Homelab Inference Architecture

Current state as of May 2026. The pre-May-2026 single-machine layout
(LiteLLM on the inference host, per-host GitHub runners, single LM
Studio engine) is gone — see SKILL.md "Decommissioning notes" if you
find stale references in old docs.

## Network topology

Three planes: a control-plane runner that drives Ansible, a gateway VM
that fronts everything as a single endpoint, and the inference workers
themselves. Clients only ever talk to the gateway.

```
┌───────────────────────────────────────────────────────────────────────┐
│ CONTROL PLANE                                                         │
│                                                                       │
│  ┌─────────────────────────────┐                                      │
│  │ terraform-runner            │  Proxmox VM · 192.168.1.20           │
│  │ GH Actions: self-hosted-    │  Drives ALL provisioning + deploys   │
│  │   infra (only)              │  via Ansible-over-SSH.               │
│  │ Deploy key: Infisical       │  No runners on any inference host    │
│  │   /ansible                  │  or gateway VM.                      │
│  └──────────────┬──────────────┘                                      │
│                 │                                                     │
│                 │  ssh -i $deploy_key (port 22)                       │
│                 ▼                                                     │
└───────────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────────┐
│ GATEWAY VM (litellm-gateway)                                          │
│ Proxmox VMID 300 · 192.168.0.5 · 2 CPU / 4 GB / 40 GB                 │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │ docker network: litellm-net                                     │  │
│  │  ┌─────────────────┐    ┌─────────────────────┐                 │  │
│  │  │ litellm (:4000) │◄──►│ litellm-postgres    │                 │  │
│  │  │ proxy +         │    │ (:5432, internal)   │                 │  │
│  │  │ /metrics        │    └─────────────────────┘                 │  │
│  │  └────────┬────────┘                                            │  │
│  │           │                                                     │  │
│  │  ┌────────▼─────────┐                                           │  │
│  │  │ Open WebUI :3000 │ (web UI; talks to litellm over net)       │  │
│  │  └──────────────────┘                                           │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│  Node Exporter :9100   Alloy (log shipper)                            │
└────────────┬───────────────────────────────┬──────────────────────────┘
             │ :1234 (LM Studio)             │ :8080 (llama-server)
             │ :8080 (llama-server)          │ :1234 (LM Studio)
             ▼                               ▼
┌──────────────────────────────┐ ┌──────────────────────────────┐
│ ubuntu-tower.lan             │ │ ubuntu-tower-02.lan          │ ← physical
│ (= inference-01)             │ │ (= inference-02)             │   boxes
│ i7-9700K · 8c/8t             │ │ i7-10700 · 8c/16t            │
│ 15 GiB RAM · RTX 3060 12 GB  │ │ 78 GiB RAM · RTX 2060 6 GB   │
│                              │ │                              │
│ ┌──────────────────────────┐ │ │ ┌──────────────────────────┐ │
│ │ LM Studio :1234          │ │ │ │ LM Studio :1234          │ │
│ │ (currently no model;     │ │ │ │ local-voice (Voxtral 3B) │ │
│ │  embedding-only)         │ │ │ │                          │ │
│ └──────────────────────────┘ │ │ └──────────────────────────┘ │
│ ┌──────────────────────────┐ │ │ ┌──────────────────────────┐ │
│ │ llama-server :8080       │ │ │ │ llama-server :8080       │ │
│ │ docker: llama-server-    │ │ │ │ docker: llama-server-    │ │
│ │ local-moe                │ │ │ │ local-reasoning          │ │
│ │ Qwen3.6-35B-A3B @ 150K   │ │ │ │ Qwen3.5-9B @ 170K        │ │
│ │ --metrics endpoint       │ │ │ │ --metrics endpoint       │ │
│ └──────────────────────────┘ │ │ └──────────────────────────┘ │
│ Node Exporter :9100          │ │ Node Exporter :9100          │
│ DCGM Exporter :9400          │ │ DCGM Exporter :9400          │
│ Alloy (log shipper)          │ │ Alloy (log shipper)          │
│ UFW: only 192.168.0.5        │ │ UFW: only 192.168.0.5        │
│      + 192.168.0.23 → :8080  │ │      + 192.168.0.23 → :8080  │
└──────────────────────────────┘ └──────────────────────────────┘
              ▲                                ▲
              │  Prometheus scrape (port 9100/9400/8080)
              │
┌─────────────┴─────────────────────────────────────────────────────────┐
│ observability-vm.lan · 192.168.0.23 · LGTM stack                      │
│ Loki (logs from Alloy) · Mimir/Prometheus (metrics) · Tempo (traces   │
│ from LiteLLM otel) · Grafana UI                                       │
└───────────────────────────────────────────────────────────────────────┘

                ▲ clients (LAN): Claude Code, Zoe, Open WebUI users,
                │ opencode-litellm, compute-01, dev machines, …
                │ → all hit https://litellm-gateway.lan:4000
```

`inference-01` / `inference-02` are workflow inputs and manifest keys;
they resolve to the `.lan` hostnames inside each workflow via
`dig @192.168.0.250` (see SKILL.md "DNS resolution at workflow time").

## Request flows

Three patterns. They differ in how auth gets to whatever ends up serving
the request.

### 1. Claude Code with Max subscription (forwarded OAuth)

```
Claude Code
  └─ ANTHROPIC_BASE_URL=http://litellm-gateway.lan:4000
     └─ x-litellm-api-key: <virtual-key>     ← gateway access
        Authorization: Bearer <OAuth token>  ← forwarded as-is
        └─ LiteLLM proxy
           ├─ matches model_name to a `forward_client_headers_to_llm_api: true`
           │  entry (e.g. claude-sonnet-4-6, claude-opus-4-7)
           └─ POST https://api.anthropic.com/v1/messages
              with the user's Authorization header intact
                 └─ Anthropic API
```

The virtual key authenticates the user *to LiteLLM* (rate limits,
spend tracking). The OAuth token authenticates the upstream call *to
Anthropic* as that user — so per-user Max-subscription quota applies,
not a shared API key.

### 2. App with API key (cloud relay)

```
App (Zoe, opencode-litellm, …)
  └─ POST http://litellm-gateway.lan:4000/v1/chat/completions
     with virtual key
        └─ LiteLLM proxy
           ├─ model_name → litellm_params.model resolution
           │  (e.g. "claude-haiku" → "anthropic/claude-haiku-4-5")
           ├─ api_key from os.environ/ANTHROPIC_API_KEY (loaded from
           │  /litellm in Infisical at gateway startup, baked into
           │  .env on the gateway VM)
           └─ upstream API call with the shared API key
                 └─ Anthropic / OpenAI / …
```

### 3. Local inference

```
App
  └─ POST http://litellm-gateway.lan:4000/v1/chat/completions
     {"model": "local-moe", …}
        └─ LiteLLM proxy
           ├─ model_name → openai/<model-id> + api_base
           │  e.g. local-moe → openai/Qwen3.6-35B-A3B-UD-IQ3_XXS.gguf
           │       api_base http://ubuntu-tower.lan:8080/v1
           ├─ api_key from the engine type (lmstudio / llama-server —
           │  llama-server accepts any non-empty string)
           └─ HTTP POST over the LAN to the chosen engine
                 └─ llama-server :8080  OR  LM Studio :1234
                    on inference-01 or inference-02
```

LiteLLM lives in a container but talks to the inference hosts over the
LAN using the real `.lan` hostnames in `api_base` — *not*
`host.docker.internal` (that pattern was retired when LiteLLM moved off
the inference machine onto its own gateway VM). The gateway VM's docker
daemon routes the container's outbound requests through the VM's
network stack, which resolves `.lan` via Technitium on dns-vm.

Mirrored hosts: when a model declares `hosts: [inference-01, inference-02]`,
the generator emits two `model_list` entries with identical `model_name`
and LiteLLM least-busy-routes between them. None of our current models
are mirrored (each is pinned to one host) but the path exists.

## Docker network (gateway VM)

`docker network ls` on litellm-gateway shows `litellm-net`:

- **litellm** (the proxy) — published `:4000` to the host
- **litellm-postgres** — no published ports; only reachable via the
  container hostname `litellm-postgres` from within the network. Holds
  virtual key registry + spend logs + UI state.
- **open-webui** — published `:3000` to the host; talks to litellm over
  the network using the internal hostname

The inference engines (LM Studio, llama-server) live on *physical*
machines, not on litellm-net. Reached via real .lan hostnames; UFW on
each inference host restricts inbound to 192.168.0.5 (gateway) +
192.168.0.23 (observability-vm, for Prometheus scrape).

## Secret + auth flow

Secrets are NOT in GitHub repo secrets (except `ANSIBLE_BECOME_PASSWORD`,
the sudo password — kept there because Infisical OIDC chicken-and-egg).
Everything else comes from self-hosted Infisical at 192.168.0.161:

```
┌──────────────────────────────────────────────────────────────┐
│ Infisical (homelab-platform-eu2-m project)                   │
│  /ansible/PRIVATE_KEY        ← SSH key for blake@ on all     │
│                                inference hosts + gateway VM  │
│  /terraform/PROXMOX_*        ← proxmox provider creds        │
│  /proxmox/*                  ← VM creation creds             │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│ Infisical (local-inference-infrastructure-pca-l project)     │
│  /litellm/LITELLM_MASTER_KEY                                 │
│  /litellm/LITELLM_POSTGRES_PASSWORD                          │
│  /litellm/ANTHROPIC_API_KEY  (optional, cloud relay)         │
│  /litellm/OPENAI_API_KEY     (optional, cloud relay)         │
└──────────────────────────────────────────────────────────────┘

Workflow run on terraform-runner:
  1. OIDC token issued by GitHub for this workflow run
  2. Infisical action exchanges OIDC token for short-lived access
  3. Secrets injected as $ANSIBLE_PRIVATE_KEY, $LITELLM_MASTER_KEY, …
  4. Ansible playbook templates these into target files on the gateway
     (e.g. .env file, /etc/prometheus/secrets/litellm-token)
  5. Containers restart, read their env, serve requests
```

The OIDC identity is trust-bound to `refs/heads/main` only — branch
dispatches return 403 from Infisical. See memory
`feedback_infisical_oidc_main_only`.

## Observability data flow

Two streams: logs and metrics, both ending at observability-vm.

**Logs** (push):
```
Container stdout / journald
  └─ Alloy on inference-01 / inference-02 / gateway / etc.
     ├─ loki.source.docker  (Docker container logs)
     └─ loki.source.journal (systemd journal)
        └─ loki.write → http://observability-vm.lan:3100/loki/api/v1/push
```

Alloy is on every VM. Labels include `node` (the workflow input name,
not the OS hostname — pinned this way to survive hostname drift; see
memory `feedback_telemetry_label_check`).

**Metrics** (pull): Prometheus on observability-vm scrapes:
- Every VM's `node_exporter :9100`
- Both inference hosts' `dcgm-exporter :9400` (GPU)
- Both inference hosts' `llama-server :8080/metrics` (when running) —
  needs the observability-vm UFW exception on each host
- `litellm-gateway :4000/metrics` with bearer-token auth (master key)

The scrape config lives in `homelab-services/services/observability/prometheus.yml`,
not this repo. Pushing changes there auto-triggers deploy-observability
which hot-reloads Prometheus.

**Traces**: LiteLLM's `otel` callback (configured in
`configs/litellm/settings.yaml`) sends OTLP HTTP traces to Tempo at
`observability-vm.lan:4318`. Per-request — model, latency, cache hit,
tokens — flow through.

## Versions & key tags

- **LiteLLM**: pin in `ansible/vars/litellm-gateway.yml` — read on each
  deploy from the docker image tag baked into the playbook
- **llama-server image**: `ghcr.io/ggml-org/llama.cpp:server-cuda` —
  unpinned tag (pulls latest on each provision). Pin to a specific
  `bXXXX` build via the image tag in `ansible/vars/main.yml` if
  reproducibility matters
- **NVIDIA driver on inference hosts**: 595.71.05 (verified 2026-05-25)

## See also

- `SKILL.md` — top-level reference + current model layout + workflow table
- `references/deployment.md` — Ansible playbook usage + CI workflows
- `references/claude-code-setup.md` — Claude Code env vars for the proxy
- `references/tuning.md` — engine_args sweep methodology
- `/homelab-bootstrap` — cross-repo Ansible/Infisical/Terraform gotchas
- `/homelab-observability` — LGTM stack ops + dashboards
- `/litellm` — general LiteLLM (not homelab-specific)
