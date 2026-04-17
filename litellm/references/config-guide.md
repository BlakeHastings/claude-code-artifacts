# LiteLLM Config Guide

## Config File Structure

```yaml
model_list:
  - model_name: <alias-clients-use>
    litellm_params:
      model: <provider>/<model-id>     # e.g. anthropic/claude-sonnet-4-6, openai/gpt-4o
      api_base: <url>                   # optional: override provider endpoint
      api_key: <key-or-env-ref>         # e.g. os.environ/ANTHROPIC_API_KEY

litellm_settings:
  callbacks: ["prometheus", "otel"]
  drop_params: true                     # silently drop unsupported params instead of erroring
  request_timeout: 120

general_settings:
  master_key: os.environ/LITELLM_MASTER_KEY
  forward_client_headers_to_llm_api: true   # forwards OAuth/auth headers to upstream
```

## Model Routing

LiteLLM matches requests by `model_name`. The client sends `model: "claude-sonnet-4-6"` and LiteLLM looks for a `model_name: claude-sonnet-4-6` entry, then routes to whatever `litellm_params.model` specifies.

Multiple entries with the same `model_name` enable load balancing across backends.

### Provider Prefixes

| Prefix | Provider | Example |
|--------|----------|---------|
| `anthropic/` | Anthropic API | `anthropic/claude-sonnet-4-6` |
| `openai/` | OpenAI or OpenAI-compatible | `openai/gpt-4o` |
| `bedrock/` | AWS Bedrock | `bedrock/anthropic.claude-v2` |
| `vertex_ai/` | Google Vertex | `vertex_ai/gemini-pro` |

For OpenAI-compatible local servers (LM Studio, vLLM, ollama), use `openai/` prefix with a custom `api_base`.

### Environment Variable References

Use `os.environ/VAR_NAME` in config to reference environment variables. LiteLLM resolves these at startup.

### Model Entries Without api_key

When `forward_client_headers_to_llm_api: true` is set, model entries can omit `api_key`. The client's Authorization header is forwarded directly to the provider. This is how Claude Code's Max subscription OAuth token passes through.

## Claude Code Integration

Claude Code sends model IDs like `claude-sonnet-4-6`, `claude-opus-4-6`, `claude-haiku-4-5-20251001`. LiteLLM needs `model_name` entries matching these exact IDs.

### Required Settings for Claude Max Subscription

1. `forward_client_headers_to_llm_api: true` in `general_settings`
2. Model entries matching Claude Code's model IDs (no `api_key` needed)
3. Client-side env vars in Claude Code settings:
   - `ANTHROPIC_BASE_URL`: LiteLLM proxy URL
   - `ANTHROPIC_CUSTOM_HEADERS`: `x-litellm-api-key: Bearer <virtual-key>`

The virtual key authenticates to the LiteLLM gateway; the OAuth token authenticates to Anthropic.

## Callbacks

```yaml
litellm_settings:
  callbacks: ["prometheus", "otel"]
```

- `prometheus`: Exposes `/metrics` endpoint for Prometheus scraping
- `otel`: Sends traces via OpenTelemetry (configure `OTEL_EXPORTER_OTLP_ENDPOINT` in env)

## Database (Postgres)

Required for: admin UI login, virtual keys, spend tracking, runtime model persistence.

Set via environment variable:
```
DATABASE_URL=postgresql://litellm:<password>@litellm-postgres:5432/litellm
STORE_MODEL_IN_DB=True
```

Without `DATABASE_URL`, the proxy API still works (master key auth) but the admin UI shows "Not connected to DB!".
