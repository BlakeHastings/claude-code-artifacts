---
name: claude-agent-sdk-worker
description: >-
  Building a headless or containerized worker that drives the Claude Agent SDK (@anthropic-ai/claude-agent-sdk,
  the TypeScript/npm SDK that wraps the Claude Code CLI, not the `claude` CLI directly). Use when a query()
  session hangs and never finishes, when running Claude Code unattended with no human to answer a tool-permission
  prompt, when streaming input into a live session for mid-run steering, when the SDK's systemPrompt /
  SDKUserMessage / hook shapes do not match what you wrote, or when building a one-shot vs long-lived agent
  worker. Covers the streaming-input session that never ends (break on the result message), permissionMode
  bypassPermissions, the preset-append systemPrompt, SDKUserMessage.parent_tool_use_id, query()/interrupt()
  teardown, PreToolUse/PostToolUse hook fields, subscription-token billing, and version drift.
allowed-tools: Read, Bash, Edit, Grep
---

# Claude Agent SDK: headless worker gotchas

Building an autonomous worker around `@anthropic-ai/claude-agent-sdk` (the npm SDK; it spawns and speaks
stream-json to the bundled `claude` CLI, e.g. `node_modules/@anthropic-ai/claude-agent-sdk-*/claude ...`). These
each cost a debugging session.

**Version drift is real; read the installed `.d.ts`.** The API observed here is 0.3.x (0.3.200). A stale pin
(`^0.1.0`) installs a genuinely different API and its types won't match — `appendSystemPrompt` existed in the
mental model but not in 0.3.x, for instance. Before writing against it, read `node_modules/@anthropic-ai/
claude-agent-sdk/sdk.d.ts` (or `unpkg.com/@anthropic-ai/claude-agent-sdk@<ver>/sdk.d.ts`) for the real
`Options`, `SDKMessage`, and hook types rather than guessing.

## The streaming-input session never ends on its own (the hang)

If you pass an **async generator** as `prompt` (streaming input, so you can inject more user messages mid-run),
the session stays alive *waiting for more input* after each turn. The SDK emits an `SDKResultMessage`
(`type: "result"`) at the end of **each turn**. So this loop hangs forever — it gets the result but then blocks
on the next iteration that never comes:

    for await (const message of query({ prompt: gen, options })) {
      if (message.type === "result") result = message.result;   // WRONG: keeps looping
    }

Break on the first `result` — the turn's result *is* the job's result — then tear the session down:

    for await (const message of session) {
      if (message.type === "result") { result = message.result ?? result; break; }
      else if (message.type === "assistant") { /* fallback text */ }
    }
    // teardown: close the input generator AND interrupt, or Claude Code lingers
    inputQueue.end();
    await session.interrupt?.().catch(() => {});

`session` (the `Query` returned by `query()`) extends `AsyncGenerator<SDKMessage>` and has `interrupt(): Promise
<void>`. **`interrupt()` alone does not end the session** while the input is still open — it stops the current
turn; you must also close the input generator so the CLI subprocess exits. A job that "never completes" plus
leftover `claude` processes is this bug: because the loop never breaks, your cleanup `finally` never runs, so the
input is never closed.

If you only need one turn and no mid-run steering, the simplest fix is to *not* keep the input open — yield the
task and let the generator return, so the session ends and the `for await` completes naturally.

## permissionMode: run unattended

Set `permissionMode: "bypassPermissions"` in `options` for an autonomous run. In the default mode Claude Code
pauses for a tool-use permission prompt that nothing answers, and the job hangs with no error. `allowedTools`
lists which tools are permitted but is not a substitute — actions outside it (or certain operations) still
prompt. `bypassPermissions` runs all tools without checks, which is appropriate when the container is the
sandbox. (Refuses to enable in some setups, e.g. running as root — run the worker as a non-root user.)

## systemPrompt: append to the preset, don't replace it

To keep Claude Code's own coding system prompt *and* add your framing, use the preset-append object; a bare
string **replaces** the preset (losing the coding brain):

    systemPrompt: { type: "preset", preset: "claude_code", append: instructions }

There is no top-level `appendSystemPrompt` option in 0.3.x.

## SDKUserMessage needs `parent_tool_use_id`

Messages yielded into a streaming-input generator are `SDKUserMessage` and require `parent_tool_use_id` (null for
a normal user turn) — omitting it is a type error:

    { type: "user", message: { role: "user", content }, parent_tool_use_id: null }

## Hooks for per-tool telemetry

`options.hooks.PreToolUse` / `PostToolUse` fire in-process. The hook input carries `tool_name`, `tool_input`,
and `tool_use_id` (PostToolUse also `tool_response`). Open a span on `PreToolUse`, end it on `PostToolUse` keyed
by `tool_use_id`. Claude Code has **no native OpenTelemetry**, so this hook path is how you emit per-tool spans.

## Billing and the CLI binary

The SDK drives the bundled `claude` CLI, so the CLI's auth applies: `CLAUDE_CODE_OAUTH_TOKEN` (from `claude
setup-token`, a subscription token) bills the subscription; a present `ANTHROPIC_API_KEY` takes precedence and
bills API credits instead. For subscription billing, keep the API key out of the process env entirely (in a
container image, never bake it in).

## Debugging a hung session

Log every message type as it arrives — it makes the failure mode obvious:

    console.info(`session message: ${message.type}${message.subtype ? ` (${message.subtype})` : ""}`);
    // system (init) -> assistant -> user (tool result) -> assistant -> result (success)

- You reach `result (success)` but the job never finishes -> the break/teardown bug above.
- You never reach `result` (stuck after `assistant`/`system`) -> a permission stall (set `bypassPermissions`) or
  a tool waiting on something.
