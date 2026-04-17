---
name: homelab-local-inference
description: >-
  Homelab local inference infrastructure reference. Use when working with
  the local-inference-infrastructure repo, LM Studio on the inference machine,
  LiteLLM proxy routing, GPU node configuration, model selection for RTX 3060,
  or Claude Code proxy setup through the homelab. Covers the specific homelab
  deployment, not general LiteLLM or LM Studio topics.
allowed-tools: Read, Bash, Grep, Glob
---

# Homelab Local Inference Infrastructure

Reference for the homelab's local inference setup. For general LiteLLM
knowledge, use `/litellm` instead.

Read `references/architecture.md` for network topology and component layout.
Read `references/deployment.md` for Ansible playbook usage and CI workflows.
Read `references/claude-code-setup.md` for how Claude Code routes through the proxy.

## Key Facts

| Component | Host | Port | Notes |
|-----------|------|------|-------|
| LM Studio | inference machine (192.168.0.52) | 1234 | Local only, no auth |
| LiteLLM proxy | inference machine (192.168.0.52) | 4000 | Gateway, requires virtual key or master key |
| Postgres (LiteLLM DB) | inference machine (docker: litellm-postgres) | 5432 | Internal docker network only |
| GitHub Actions runner | inference machine | — | Labels: `self-hosted, inference, gpu` |

## Repo Layout

```
local-inference-infrastructure/
├── ansible/
│   ├── inference-setup.yml          # Main playbook (phases 1-6)
│   ├── templates/litellm.env.j2     # Secrets template
│   └── vars/main.yml                # Non-secret config vars
├── configs/
│   └── litellm/
│       ├── litellm-config.base.yaml     # Single-node config (active)
│       └── litellm-config.cluster.yaml  # Multi-node config (future)
└── .github/workflows/
    └── update-services.yml          # Auto-deploys on config/ansible changes + weekly schedule
```

## Current GPU Hardware

- **RTX 3060 12GB** — single inference node
- Only one model loaded at a time in LM Studio
- Models selected by llmfit v0.8.4 for this VRAM budget

## Configured Local Models

| Alias | Model | Quant | VRAM | Use Case |
|-------|-------|-------|------|----------|
| local-general | DeepSeek-V2-Lite-Chat | Q4_K_M | 8.0 GB | General chat |
| local-coder | DeepSeek-Coder-V2-Lite-Instruct | Q4_K_M | 8.0 GB | Code generation |
| local-reasoning | DeepSeek-R1-0528-Qwen3-8B | Q6_K | 11.35 GB | Chain-of-thought (tight fit) |
| local-fast | Phi-4-mini-reasoning | Q8_0 | 4.99 GB | Light tasks |
| local-vision | gemma-3n-E2B-it | Q8_0 | 8.89 GB | Multimodal/vision |
| local-embeddings | nomic-embed-text-v1.5 | Q8_0 | 0.65 GB | Embeddings |

## Cloud Relay Models

| Alias | Routes To | Auth |
|-------|-----------|------|
| claude-sonnet | anthropic/claude-sonnet-4-6 | ANTHROPIC_API_KEY env |
| claude-haiku | anthropic/claude-haiku-4-5 | ANTHROPIC_API_KEY env |
| claude-opus | anthropic/claude-opus-4-6 | ANTHROPIC_API_KEY env |
| gpt-4o | openai/gpt-4o | OPENAI_API_KEY env |
| claude-sonnet-4-6 | anthropic/claude-sonnet-4-6 | Forwarded OAuth (Max sub) |
| claude-opus-4-6 | anthropic/claude-opus-4-6 | Forwarded OAuth (Max sub) |
| claude-haiku-4-5-20251001 | anthropic/claude-haiku-4-5-20251001 | Forwarded OAuth (Max sub) |
| claude-haiku-4-5 | anthropic/claude-haiku-4-5 | Forwarded OAuth (Max sub) |

## Known Issues

- LM Studio can only serve one model at a time on the 3060 — model switching requires `lms load <model>`
- LiteLLM reads config at startup only; bind-mount changes require `docker restart litellm`
- The Ansible playbook now registers config/env changes and restarts litellm automatically
- `OTEL_EXPORTER_OTLP_ENDPOINT` in litellm.env.j2 has a placeholder IP (`OBSERVABILITY_SERVER_IP`) that needs replacing when the observability stack is set up
