# Claude Code → LiteLLM Proxy Setup

## How It Works

Claude Code authenticates to Anthropic via OAuth (Max subscription). The LiteLLM proxy sits in between, forwarding the OAuth token while tracking usage through a virtual key.

```
Claude Code ──► LiteLLM (192.168.0.52:4000) ──► Anthropic API
                │                                    ▲
                │ x-litellm-api-key validates        │ Authorization header
                │ gateway access                     │ (OAuth token) forwarded
                └────────────────────────────────────┘
```

## Claude Code Settings (~/.claude/settings.json)

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "http://192.168.0.52:4000",
    "ANTHROPIC_CUSTOM_HEADERS": "x-litellm-api-key: Bearer sk-<your-virtual-key>"
  }
}
```

- `ANTHROPIC_BASE_URL` — redirects all Claude Code API calls through LiteLLM
- `ANTHROPIC_CUSTOM_HEADERS` — passes the LiteLLM virtual key as a custom header
- Do NOT set `ANTHROPIC_API_KEY` — auth comes from the OAuth token (Max subscription)

## LiteLLM Config Requirements

Two things must be set in `litellm-config.base.yaml`:

1. **`forward_client_headers_to_llm_api: true`** in `general_settings` — forwards the OAuth Authorization header to Anthropic

2. **Model entries matching Claude Code's model IDs** — Claude Code sends model names like `claude-sonnet-4-6`, `claude-opus-4-6`, etc. LiteLLM needs `model_name` entries matching these exact IDs, without `api_key` (since auth comes from the forwarded header).

## Using Local Models from Claude Code

Claude Code's `/model` command accepts any model name. Type a local alias (e.g., `local-coder`) and it routes through LiteLLM to LM Studio.

Caveats:
- The desired model must be loaded in LM Studio first (`lms load <model>`)
- Only one local model can be loaded at a time on the 3060
- Claude Code's Agent tool `model` parameter only accepts `sonnet`, `opus`, `haiku` — not custom model names
- For skills needing local models, use direct HTTP calls to LiteLLM via curl instead of the Agent tool

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| "Invalid model name" | LiteLLM config missing the model alias; or container needs restart |
| Connection refused to :4000 | LiteLLM container not running; check `docker ps` |
| Connection refused to :1234 | LM Studio server not started or model not loaded |
| Auth error from Anthropic | Missing `forward_client_headers_to_llm_api: true` in config |
| Settings not taking effect | Restart Claude Code session (env vars read at startup) |
