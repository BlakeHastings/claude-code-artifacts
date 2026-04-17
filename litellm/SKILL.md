---
name: litellm
description: >-
  LiteLLM proxy configuration, model routing, virtual keys, and admin UI.
  Use when working with LiteLLM config files, adding or removing models,
  troubleshooting routing errors, managing virtual keys, configuring
  callbacks, or diagnosing proxy issues. Covers both general LiteLLM
  knowledge and admin operations.
allowed-tools: Read, Bash, Edit, Grep, Glob
---

# LiteLLM

## Quick Reference

Read `references/config-guide.md` for config file format, model routing, and provider setup.
Read `references/admin-operations.md` for admin UI, virtual keys, and operational tasks.

## When Invoked

If `$ARGUMENTS` mentions a specific topic, read the relevant reference file and answer directly.
If no arguments, summarize available operations and ask what the user needs help with.

## Key Concepts

- **Model list**: Maps friendly `model_name` aliases to provider-specific `litellm_params.model` IDs
- **Virtual keys**: Scoped API keys created in the admin UI for per-app access control and spend tracking
- **Callbacks**: Plugins for logging, metrics, and tracing (prometheus, otel, etc.)
- **`forward_client_headers_to_llm_api`**: Forwards client auth headers to the upstream provider — required for Claude Max subscription OAuth passthrough
- **`STORE_MODEL_IN_DB`**: Persists runtime model changes to Postgres so they survive restarts

## Common Troubleshooting

| Symptom | Likely Cause |
|---------|-------------|
| "Not connected to DB!" on login | No `DATABASE_URL` configured; LiteLLM needs Postgres for the admin UI |
| "Invalid model name" from Anthropic | Model name in config doesn't match what the client sends; check `model_name` vs actual API model ID |
| Config changes not taking effect | Container wasn't restarted after config file update on disk |
| Auth error with Max subscription | Missing `forward_client_headers_to_llm_api: true` in `general_settings` |
| OTEL export failures in logs | `OTEL_EXPORTER_OTLP_ENDPOINT` has a placeholder IP instead of a real address |
