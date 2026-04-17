# Homelab Inference Architecture

## Network Topology

```
┌──────────────────────────────────────────────────┐
│  Inference Machine (ubuntu-tower / 192.168.0.52) │
│                                                  │
│  ┌──────────────┐    ┌────────────────────────┐  │
│  │  LM Studio   │◄───│  LiteLLM Proxy (:4000) │  │
│  │  (:1234)      │    │  ┌──────────────────┐  │  │
│  │  local only   │    │  │ litellm-net      │  │  │
│  └──────────────┘    │  │  ┌────────────┐  │  │  │
│                      │  │  │ Postgres   │  │  │  │
│                      │  │  │ (:5432)    │  │  │  │
│                      │  │  └────────────┘  │  │  │
│                      │  └──────────────────┘  │  │
│                      └────────────────────────┘  │
│                                                  │
│  ┌──────────────┐    ┌────────────────────────┐  │
│  │ Node Exporter│    │ DCGM Exporter (:9400)  │  │
│  │ (:9100)      │    │ GPU metrics            │  │
│  └──────────────┘    └────────────────────────┘  │
│                                                  │
│  ┌──────────────┐    ┌────────────────────────┐  │
│  │ Alloy        │    │ GitHub Actions Runner  │  │
│  │ (log shipper)│    │ [self-hosted,inference] │  │
│  └──────────────┘    └────────────────────────┘  │
└──────────────────────────────────────────────────┘
         │
         │ :4000 (LiteLLM API)
         ▼
┌──────────────────┐
│  Clients         │
│  - Claude Code   │  ──► LiteLLM ──► Anthropic (OAuth passthrough)
│  - Zoe           │  ──► LiteLLM ──► LM Studio / Anthropic / OpenAI
│  - Other apps    │
└──────────────────┘
```

## Request Flow

### Claude Code (Max subscription)
```
Claude Code
  → ANTHROPIC_BASE_URL → LiteLLM :4000
  → x-litellm-api-key header validates gateway access
  → Authorization header (OAuth token) forwarded to Anthropic
  → Response returned
```

### App using API key (e.g., Zoe)
```
App
  → LiteLLM :4000 with virtual key
  → Model alias resolved (e.g., claude-sonnet → anthropic/claude-sonnet-4-6)
  → ANTHROPIC_API_KEY from env used for auth
  → Response returned
```

### Local inference request
```
App
  → LiteLLM :4000 with virtual key
  → Model alias resolved (e.g., local-coder → openai/deepseek-coder-v2-lite-instruct)
  → Routed to LM Studio at host.docker.internal:1234
  → LM Studio serves from GPU (no auth needed)
  → Response returned
```

## Docker Network

LiteLLM and Postgres share an internal docker network (`litellm-net`). Postgres has no published ports — only reachable from LiteLLM via the container hostname `litellm-postgres`.

LiteLLM uses `host.docker.internal` (mapped to `host-gateway`) to reach LM Studio on the host's port 1234.

## GitHub Secrets

| Secret | Used By |
|--------|---------|
| LITELLM_MASTER_KEY | LiteLLM gateway auth |
| ANTHROPIC_API_KEY | Cloud relay to Anthropic |
| OPENAI_API_KEY | Cloud relay to OpenAI |
| LITELLM_POSTGRES_PASSWORD | Postgres container |
| ANSIBLE_BECOME_PASSWORD | sudo for Ansible |
