---
name: opencode-plugin-dev
description: >-
  OpenCode plugin system architecture reference. Use when building, debugging, or
  extending an OpenCode plugin — plugin hook types (auth, tool, config, provider),
  /connect provider registration, custom provider opencode.json format, no-key
  sentinel, auth loader pattern, graceful degradation, type: "api" prompt
  behavior, auth.json storage schema, plugin cache invalidation, or understanding
  why a provider does not appear in the /connect UI.
allowed-tools: Read, Grep, Glob, WebFetch
---
# OpenCode Plugin Development

## Plugin Type Signature

```typescript
import type { Plugin } from "@opencode-ai/plugin"

// Plugin is an async factory that receives a context and returns hooks.
const plugin: Plugin = async (ctx) => {
  return {
    auth: { ... },
    tool: { ... },
    config: async (config) => { ... },
  }
}
export default plugin
```

`Plugin = (ctx: PluginInput) => Promise<Hooks>`

Available hook keys: `auth`, `tool`, `config`, `provider`, `chat.params`, `event`

---

## `auth` Hook

Attaches an authentication method to a **named provider slot**. It does NOT
create a new provider — the named provider must already exist (either built-in
or via a static `opencode.json` entry).

```typescript
auth: {
  provider: "my-provider",   // must match an existing provider ID
  loader: async (auth) => {
    let stored: unknown = null
    try { stored = await auth() } catch {}  // throws when no credential stored
    return {
      apiKey: (stored as any)?.key ?? "",
      baseURL: "http://host:4000/v1",
    }
  },
  methods: [{
    type: "api" as const,
    label: "Connect to My Provider",
    // DO NOT add your own API-key prompt here — OpenCode adds a built-in one
    // for every type: "api" method. Only list EXTRA prompts (baseURL, org, etc).
    prompts: [{ type: "text" as const, key: "baseURL", message: "Base URL" }],
    authorize: async (inputs) => ({
      type: "success" as const,
      // NOTE: for type: "api", authorize is rarely invoked in practice —
      // OpenCode persists prompts natively (see "Auth store schema" below).
      key: inputs?.key || "no-key",
    }),
  }],
},
```

### `type: "api"` prompt behavior (empirically verified)

- OpenCode **always** renders a built-in API-key prompt for `type: "api"`
  methods. Its header is the method's `label` field.
- Your `prompts` array is **additive** — entries are shown *in addition to*
  the built-in key prompt. Adding a prompt with `key: "key"` or
  `key: "apiKey"` produces a duplicate API-key popup.
- Use `label` for hint text you'd otherwise put on a custom key prompt
  (e.g. `label: "MyProvider (leave API key blank if proxy has no auth)"`).

### Auth store schema — where prompt values actually live

After `/connect <provider>`, OpenCode writes to
`~/.local/share/opencode/auth.json`:

```json
{
  "my-provider": {
    "type": "api",
    "key": "<value from the built-in API-key prompt>",
    "metadata": {
      "baseURL": "<value from your baseURL prompt>",
      "...": "every other prompt's value, keyed by prompt.key"
    }
  }
}
```

- The built-in API-key prompt → `key` (top-level).
- Every entry in your `prompts` array → `metadata.<prompt.key>`.
- **`authorize()` is NOT invoked for `type: "api"` in the normal flow** —
  OpenCode handles persistence itself. Read prompt values back from
  `auth.json.metadata.*` on startup; do not rely on `authorize` writing
  `opencode.json`.

**`no-key` sentinel:** OpenCode cannot store an empty string as a credential.
If the user leaves the built-in prompt blank, it may be stored as `"no-key"`.
Filter that sentinel out in `loader` / anywhere else you read `auth.key`.

---

## `tool` Hook

Registers AI-callable tools available in the current session.

```typescript
import { tool } from "@opencode-ai/plugin"

tool: {
  my_tool: tool({
    description: "What this tool does",
    args: {
      my_arg: tool.schema.string().describe("Argument description"),
    },
    execute: async ({ my_arg }) => {
      // Return a string — shown to the model as the tool result
      return `Done: ${my_arg}`
    },
  }),
},
```

---

## `config` Hook

Runs at every OpenCode startup. Receives the mutable config object and returns
void. Anything you set on `config` is visible to the rest of OpenCode.

```typescript
config: async (config) => {
  // Inject a slash command
  config.command ??= {}
  config.command["my-command"] = {
    template: "AI prompt sent when user runs /my-command",
    description: "Shown in the command palette",
  }

  // Inject a provider dynamically
  config.provider ??= {}
  config.provider["my-provider"] = {
    npm: "@ai-sdk/openai-compatible",
    name: "Display Name",
    options: { baseURL: "http://host:4000/v1", apiKey: "..." },
    models: { "model-id": { id: "model-id", name: "model-id" } },
  }
}
```

**Graceful degradation:** any network call in `config` must be wrapped in
try/catch with an early `return`. A thrown error here crashes OpenCode startup.

---

## `/connect` UI — How Providers Appear

The `/connect` list is built from the merged config OpenCode sees at startup.
Two mechanisms put a provider there:

1. **Static entry in `~/.config/opencode/opencode.json`** — persists across
   restarts, visible even before any plugin loads.
2. **Dynamic injection via the `config` hook** — `config.provider["id"] = {...}`
   in the hook is visible to `/connect` on the *same* launch.

### First-launch rule

If your plugin is the only thing that knows about the provider, you must
inject it from the `config` hook on every launch. Otherwise the user has to
run the plugin once, restart, and *then* see the provider in `/connect`.

```typescript
config: async (config) => {
  config.provider ??= {}
  config.provider["my-provider"] = {
    npm: "@ai-sdk/openai-compatible",
    name: "Display Name",
    options: { baseURL: "http://host:4000/v1" },
    models: {
      setup: { id: "setup", name: "Run /connect my-provider to configure" },
    },
  }
  // Then attempt to upgrade `models` with a live fetch, wrapped in try/catch.
}
```

### Pattern: Write from a tool (persistent registration)
When you want the provider to persist in `opencode.json` (e.g. so other tools
or non-plugin callers see it), write it from a tool's `execute`:

```typescript
execute: async ({ base_url }) => {
  const configPath = join(homedir(), ".config", "opencode", "opencode.json")
  let existing: Record<string, unknown> = {}
  try { existing = JSON.parse(await readFile(configPath, "utf8")) } catch {}
  existing.provider = {
    ...(existing.provider as object ?? {}),
    "my-provider": {
      npm: "@ai-sdk/openai-compatible",
      name: "Display Name",
      options: { baseURL: `${base_url}/v1` },
      models: {},
    },
  }
  await mkdir(dirname(configPath), { recursive: true })
  await writeFile(configPath, JSON.stringify(existing, null, 2))
  return "Provider registered. Run /connect my-provider to add an API key."
}
```

After this write + OpenCode restart, the provider appears in `/connect`.

---

## Common Pitfalls

| Symptom | Cause | Fix |
|---|---|---|
| Provider missing from `/connect` list on first launch | `auth` hook alone, or `opencode.json` entry written by a tool but not yet seen by the running process | Inject `config.provider[id]` from the `config` hook on every launch |
| Two API-key popups during `/connect` | Custom prompt with `key: "key"` or `"apiKey"` duplicates OpenCode's built-in API-key prompt | Remove the custom key prompt; put hint text in the method's `label` |
| `authorize()` never fires for `type: "api"` | Expected — OpenCode persists prompts natively to `auth.json` | Read values from `auth.json.<provider>.metadata.*` instead |
| Local `src/index.ts` edits have no effect | Plugin is npm-sourced and OpenCode is running a cached copy | Wipe `~/.cache/opencode/packages/<name>@*/` before relaunching |
| `auth()` throws on first run | No credential stored yet | Wrap in try/catch with fallback |
| `config` hook crashes OpenCode | Unguarded `fetch` or file read throws | try/catch every I/O call, return early on failure |
| Model IDs show with wrong format | Adding a prefix to model IDs from `/v1/models` | Use IDs from `/v1/models` verbatim |
| Plugin config directory missing on CI | OpenCode never ran on the runner | `mkdir({ recursive: true })` before every `writeFile` |

---

## Plugin cache invalidation

OpenCode downloads npm-sourced plugins **once** to
`~/.cache/opencode/packages/<name>@<tag>/` and reuses the cached copy across
launches. Publishing a new npm tag or editing the local source has **no
effect** until that cache directory is removed — even `bun link`-style workflows
are shadowed if any `opencode.json` references the plugin by package name.

Reset tooling for a plugin should wipe:

1. `provider.<id>` from `~/.config/opencode/opencode.json` (surgical — leave
   other providers alone).
2. `<id>` from `~/.local/share/opencode/auth.json` (surgical).
3. Every `~/.cache/opencode/packages/<name>@*/` directory (full recursive
   delete).

---

## E2E Testing OpenCode Plugins

Testing with a real `opencode run` process requires a mock HTTP server:

```typescript
// Bun.serve({ port: 0 }) gets an OS-assigned port
// boundPort must be captured AFTER Bun.serve() returns
let boundPort = 0
const server = Bun.serve({ port: 0, fetch(req) { /* use boundPort here */ } })
boundPort = server.port
```

**Stdout caveat:** `opencode run` is a TUI. When stdout is piped
(`Bun.spawnSync` with `stdout: "pipe"`), the model response is NOT captured —
the TUI renders directly to the terminal device. Assert on the mock's request
log instead:

```typescript
const result = Bun.spawnSync(["opencode", "run", "--model", "..."], { stdout: "pipe" })
// result.stdout is empty — don't assert on it
// Instead assert that the mock received the expected request:
assert(mock.chatRequests.some(r => r.model === "test-model-chat"))
```

**Circular dependency fix:** If Flow A configures a provider that Flow B
depends on, pre-seed the config file before Flow A runs. Flow A then
"rewrites" it (to the same value), and both flows work without reordering.
