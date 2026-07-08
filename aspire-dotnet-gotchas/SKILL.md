---
name: aspire-dotnet-gotchas
description: >-
  Hard-won gotchas building a .NET Aspire app with gRPC, a message bus, and an in-process agent, the kind
  that each cost a debugging session. Use when an Aspire service cannot reach another (service discovery,
  "No such host"), wiring gRPC between Aspire projects (HTTP/2, HTTPS, ALPN), adding RabbitMQ via Aspire
  (guest login fails, generated password, Not_Authorized), using MassTransit (v8 vs v9 licensing, IBus vs
  IPublishEndpoint, captive dependency), passing AppHost configuration or parameters (the `__` env-var
  separator, AddParameter from appsettings/user-secrets, where each setting should live, clone-and-run
  defaults, Worker SDK appsettings copy), building an agent with the Microsoft Agent Framework
  (Microsoft.Agents.AI, ChatClientAgent, function tools, an OpenAI-compatible/LiteLLM endpoint,
  CodeAct/Agent Harness providers, compacting a long-lived AgentSession), a Web SDK project hitting an
  ambiguous type like SessionOptions, the
  AppHost not seeing app types, exporting OpenTelemetry GenAI telemetry or fanning it through an
  OpenTelemetry Collector to the Aspire dashboard and a self-hosted Langfuse (the collector-to-dashboard
  host.docker.internal/TLS hop, UseOtlpExporter vs AddOtlpExporter, getting a non-.NET/polyglot container -- a
  Node/Python worker sidecar -- to export to the collector when WithAppForwarding skips AddDockerfile containers
  and the collector's OTLP receiver is HTTPS and rejects a plain-HTTP exporter, filtering the Langfuse leg by
  service.name, otelcol validate), grouping or pinning dashboard resources
  (WithParentRelationship, dashboard URL/port), or a build failing with file-locked DLLs (MSB3026/MSB3027) or
  a folder that will not rename. Personal companion to the vendored aspire* plugin skills (which should not be
  hand-edited).
allowed-tools: Read, Bash, Edit, Grep
---

# Aspire .NET app gotchas (config, gRPC, RabbitMQ, MassTransit, Agent Framework, build locks)

Non-obvious things hit while building a .NET 10 Aspire app (Aspire 13.4) with a gRPC link between
projects, a MassTransit/RabbitMQ event bus, and an in-process agent on the Microsoft Agent Framework. Scope
every version-specific claim to the versions named and verify before relying on it. Keep notes here, not in
the vendored `aspire*` plugin skills, those update and clobber edits (the same reason that skill set
refuses to edit `.aspire/modules/`).

## Service discovery needs a *declared* endpoint

`WithReference(target)` injects something resolvable only if `target` actually has endpoints. A project
with **no launch profile** (e.g. a Worker switched to `Microsoft.NET.Sdk.Web` without a
`Properties/launchSettings.json`) exposes none, so the consumer's `services__<name>__...` config is empty
and a client resolving `https://<name>` falls through to a **literal DNS lookup**:

    System.Net.Sockets.SocketException (11001): No such host is known. (<name>:443)

Recognize it by the `Microsoft.Extensions.ServiceDiscovery` handler being in the call stack yet the name
still being used as a hostname. Fix: give the target an `applicationUrl` (an https+http
`launchSettings.json` profile) or declare an endpoint in the AppHost.

## gRPC between Aspire projects: use HTTPS (HTTP/2 via ALPN)

gRPC needs HTTP/2. Expose the server over **HTTPS** so Kestrel's ALPN negotiates h2 while still serving
HTTP/1 (dashboard/health probes) on the same port. Client side: `AddGrpcClient<T>(o => o.Address =
new Uri("https://<resourcename>")).AddServiceDiscovery()`. Plaintext h2c needs an explicit Http2-only
endpoint and does not coexist with h1 on one port without ALPN, so prefer HTTPS. The ASP.NET dev cert
must be trusted (`dotnet dev-certs https --trust`) or the client fails the TLS handshake, a *different*
error than the DNS one above.

## RabbitMQ via Aspire: it is not guest/guest

`builder.AddRabbitMQ("messaging")` generates a **random password** (persisted in AppHost user secrets), so
the management UI rejects `guest`/`guest` with `Not_Authorized`. RabbitMQ also restricts the literal
`guest` user to localhost, so an app connecting from its own container cannot auth as guest regardless of
password. For predictable dev, set explicit **non-guest** credentials via parameters:

    var user = builder.AddParameter("rabbitmq-username", "zoe");
    var pass = builder.AddParameter("rabbitmq-password", "dev-password", secret: true);
    var mq   = builder.AddRabbitMQ("messaging", user, pass).WithManagementPlugin();

`WithManagementPlugin()` exposes the UI; its link is in the dashboard.

## MassTransit gotchas (v8 era)

- **v8 is OSS (Apache-2.0); v9+ moved to a commercial license.** `dotnet add package MassTransit.RabbitMQ`
  pulls the latest, which may be v9. Pin the v8 line (`Version="8.*"`) to stay OSS. Verify current
  licensing before assuming.
- **Publish via `IBus`, not `IPublishEndpoint`, outside a consumer.** `IPublishEndpoint` is registered
  **scoped**; injecting it into a singleton (a `BackgroundService` or singleton handler) is a
  captive-dependency error. `IBus` is the singleton publisher and is itself an `IPublishEndpoint`.
- **Routing is by message type**, so there are no exchange/routing-key strings in code (the exchange is
  named after the message type's full name). Define slim message contracts (records) in a shared assembly
  with **no MassTransit dependency**, so the published language stays transport-agnostic.
- With Aspire, feed the connection string to the transport inside `UsingRabbitMq`:
  `cfg.Host(new Uri(builder.Configuration.GetConnectionString("messaging")!))`.

## Web SDK implicit usings cause type ambiguity

Switching a project to `Microsoft.NET.Sdk.Web` adds `Microsoft.AspNetCore.Builder` to the implicit usings,
whose `SessionOptions` collides with other libraries' (e.g. `Microsoft.ML.OnnxRuntime.SessionOptions`):

    error CS0104: 'SessionOptions' is an ambiguous reference between
    'Microsoft.AspNetCore.Builder.SessionOptions' and 'Microsoft.ML.OnnxRuntime.SessionOptions'

Fix with a using alias in the affected files: `using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;`.
Same pattern for any type the ASP.NET implicit usings shadow.

## The AppHost cannot reflect over app types

Aspire project references (`AddProject<Projects.X>`) are emitted with `ReferenceOutputAssembly=false`: the
app DLLs are not copied into the AppHost output and its types are not loadable there (the generated
`Projects.X` class carries only a project path). So you cannot scan/inspect app assemblies from inside the
AppHost (e.g. for a code-derived diagram). Use a dedicated tool project with real `ProjectReference`s.

## Configuration: where values live, and the `__` question

The double underscore in `WithEnvironment("Listener__Vad__ModelPath", ...)` is **the .NET environment-variable
config separator, not an Aspire feature**: the env-var configuration provider maps `__` to the `:` hierarchy
separator because `:` is not a legal env-var name character on all platforms. So any hierarchical setting
delivered as an env var must be `Section__Key`, with or without Aspire. The only way the name gets written
for you is `WithReference`: a connection-string resource injects `ConnectionStrings__<name>`, a
project/endpoint reference injects `services__<name>__...`. There is no "bind a config section to a resource"
API; for an arbitrary setting you either hand-write the `__` name or do not push it from the AppHost at all.

**Where each value belongs (this removes most `WithEnvironment` calls):**

- A value with a sensible committable default → that **service's own `appsettings.json`**, read by its own
  Options binding. The AppHost never mentions it. This is just standard .NET configuration; the service
  reads it identically whether run standalone or under Aspire.
- A value with **no good committable default** (a secret, or an environment-specific endpoint) → an AppHost
  **parameter**. `builder.AddParameter("name")` resolves `Parameters:name` from the AppHost's own
  configuration; precedence is env var `Parameters__name` > config files (`appsettings.json`, user-secrets)
  > interactive prompt. Put non-secret defaults in the AppHost's `appsettings.json` under a `Parameters`
  section; put real secrets in user-secrets (`dotnet user-secrets set Parameters:name value`).
- Pushing a service's *static* config from the AppHost via `WithEnvironment` literals is a redundant
  override of what the service already reads from its own `appsettings.json` (two sources of truth). Delete
  it. Confirm the value is really there by checking the service's `appsettings.json` first.

**Clone-and-run.** For `aspire run` to work on a fresh clone, every parameter needs a resolvable value.
Commit non-secret defaults in the AppHost `appsettings.json` `Parameters` section; leave only true secrets to
user-secrets, which live in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>` and **survive a re-clone** on
the same machine (keyed by the csproj `UserSecretsId`, not stored in the repo).

**`aspire run` re-prompts for a secret parameter every run (user-secrets not loaded).** The host adds
user-secrets to configuration **only in the Development environment**. `aspire run` does not put the AppHost in
Development unless something sets it (there is often no AppHost `launchSettings.json` profile doing so), so a
secret-only parameter never resolves and the CLI prompts. (Confirmed: `aspire run --help` exposes **no
`--launch-profile`** option, so `aspire run` does not read `launchSettings.json` profiles the way `dotnet run`
does — configure the AppHost and dashboard via `appsettings.json`, which `aspire run` does honor.) The trap: the
CLI **saves** the entered value back to
user-secrets, yet the AppHost still cannot read it next run (still not Development), so it prompts again and
re-saves the same value, looping. Tell it apart from a wrong key: non-secret parameters in `appsettings.json`
resolve fine (those load in every environment), so only the secret one keeps prompting while its value sits
correctly under `Parameters:<name>` in `secrets.json`. Fix by loading the store unconditionally in the AppHost:

    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);

(right after `DistributedApplication.CreateBuilder(args)`; needs `using System.Reflection;` and
`using Microsoft.Extensions.Configuration;`). Now the saved secret resolves under both `aspire run` and
`dotnet run`. Forcing Development instead (a `launchSettings.json` profile or `DOTNET_ENVIRONMENT`) relies on
the launcher honoring it; the explicit `AddUserSecrets` is deterministic.

**Worker SDK copies `appsettings.json`.** `Microsoft.NET.Sdk.Worker` auto-copies `appsettings.json` to build
output just like the Web SDK (verified: a Worker project's file lands in `bin/.../net10.0/`), so a Worker
service reading config via `Host.CreateApplicationBuilder` works with no explicit `<Content>` include.
Confirm by listing the output dir if unsure. Corollary: do **not** add `<Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />` "to be safe" — the SDK already includes it, so the explicit item is a duplicate and the build fails with **`NETSDK1022: Duplicate 'Content' items were included`**. Only add an explicit item if you first set `EnableDefaultContentItems=false`.

## Microsoft Agent Framework (building an in-process agent)

Notes from wiring an agent with the Microsoft Agent Framework. Scope: `Microsoft.Agents.AI` 1.10.0 era; the
framework moves fast, verify versions and preview status before relying.

- **GA stack that works today:** `Microsoft.Agents.AI` (1.x, GA) + `Microsoft.Extensions.AI` (10.x, GA).
  Build a `ChatClientAgent` over an `IChatClient`; give it tools with `AIFunctionFactory.Create(method)`
  (`[Description]` on the method and each parameter drives the JSON schema). The composed
  `FunctionInvokingChatClient` runs the model -> tool -> model loop to completion automatically; bound it
  with `MaximumIterationsPerRequest` (default 40, lower it).
- **Point at an OpenAI-compatible endpoint** (LiteLLM, llama.cpp, Ollama, LM Studio) instead of real OpenAI:
  `new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(".../v1") })
  .GetChatClient(model).AsIChatClient()`, then `new ChatClientAgent(chat, options)`. Use `GetChatClient`
  (**Chat Completions**), not `GetResponseClient` (Responses API), because local gateways usually do not
  implement Responses. The key is required by the constructor but ignored by local servers; pass any
  non-empty placeholder.
- **API gotcha:** instructions live on `ChatOptions.Instructions`, **not** `ChatClientAgentOptions` (the
  latter is `error CS0117: 'ChatClientAgentOptions' does not contain a definition for 'Instructions'`). Use
  `new ChatClientAgentOptions { Name = "...", ChatOptions = new() { Instructions = "...", Tools = [...] } }`.
- **A custom `AIContextProvider` must override `ProvideAIContextAsync`, not `InvokingCoreAsync`.** This is the
  one that cost a long debugging session. On `Microsoft.Agents.AI` 1.10.0 the provider base has three layers:
  `InvokingAsync` (public, **non-virtual** entry), `InvokingCoreAsync` (`protected virtual`, returns the **full
  merged** `AIContext` for the invocation), and `ProvideAIContextAsync` (`protected virtual`, returns only the
  **additional** context to merge). The default `InvokingCoreAsync` filters the input, calls
  `ProvideAIContextAsync`, then **merges the result with the caller's input (concatenating instructions,
  messages, and tools)** so the user's turn is preserved. A provider that overrides `InvokingCoreAsync` and
  returns `new AIContext { Instructions = ... }` (no `Messages`) **replaces** the whole context and **drops the
  user turn**: the request reaches the gateway system-only and Anthropic 400s (`requires at least one
  non-system message ... Received Messages=[]`), surfacing as 0 streamed chunks then an immediate fault.
  Streaming tends to expose it first, but it is not streaming-specific. **Fix:** override `ProvideAIContextAsync`
  (same body, returning just the additional `Instructions`/`Messages`/`Tools`); the base merge keeps the turn.
  Confirm which methods are virtual with a quick reflection dump (`GetMethods(Public|NonPublic|Instance)` and
  read `IsVirtual`/`IsFamily`) before picking an override point.
- **Do not "fix" a system-only request with LiteLLM `modify_params: True`.** It injects a dummy
  `{"role":"user","content":"Please continue."}`, so the 400 disappears but the agent answers the dummy prompt
  and ignores the real user turn — it masks a dropped-turn bug. To prove where a turn is lost, wrap the gateway
  `IChatClient` in a `DelegatingChatClient` that logs message count/roles **inside** the agent (so it sees the
  post-function-invocation messages actually sent): a healthy turn logs `1 message(s) [user], instructions=N chars`.
- **Tool-calling reliability is the model's, not the framework's.** The framework only orchestrates the loop;
  whether valid `tool_calls` come back depends on the served model and the gateway forwarding the `tools`
  schema. Prove the harness with a known-good tool-calling model (e.g. Claude via the gateway) before
  blaming it, then swap to a local model.
- **Wrong model name** surfaces as a gateway `HTTP 400: Invalid model name passed in model=X. Call
  /v1/models to view available models`. `GET <gateway>/v1/models` with the key lists the exact aliases.
- **Agent Harness providers are `AIContextProvider` subclasses you drop into the same array.** Each
  (`AgentSkillsProvider`, `TodoProvider`, `FileMemoryProvider` [session-scoped], `FileAccessProvider`
  [cross-session folder], `BackgroundAgentsProvider` [sub-agents as tools], `AgentModeProvider` [plan/execute],
  `CompactionProvider`, plus `ChatHistoryMemoryProvider`/`TextSearchProvider`) overrides `ProvideAIContextAsync`
  and contributes instructions/tools, so you adopt one by adding it to `ChatClientAgentOptions.AIContextProviders`
  next to your instructions/skills providers. Observed in `Microsoft.Agents.AI` **1.10.0** (enumerate the real
  set with a reflection dump). **`ShellExecutor` (sandboxed shell) was NOT in 1.10.0** despite being announced
  for the Harness; keep a hand-rolled shell function-tool until it actually ships in your installed version.
  They share the `[Experimental]`/`MAAI001` gate. **CodeAct** (`Microsoft.Agents.AI.Hyperlight`) was Alpha and
  may not restore (its `Hyperlight.HyperlightSandbox.Api` dep was unpublished). The firmly-GA path is the DIY
  function-tool approach above.
- **Out-of-band, cancellable conversation compaction.** A long-lived `AgentSession` grows unbounded; you do not
  have to compact in-run (`CompactionProvider` as a registered provider rewrites the message view before *every*
  turn, in the request path). The static **`CompactionProvider.CompactAsync(strategy, IEnumerable<ChatMessage>,
  logger, ct)`** is documented as "ad-hoc compaction outside the provider pipeline" and takes a
  `CancellationToken`; **`AgentSessionExtensions.TryGetInMemoryChatHistory(out List<ChatMessage>)` /
  `SetInMemoryChatHistory(List<ChatMessage>)`** read and replace the session's stored history. So compact on your
  own trigger (e.g. end-of-conversation, off a bus event): read history, `await CompactAsync(...)`, and if the
  token was not cancelled, write the result back. Use **`SummarizationCompactionStrategy(chatClient,
  CompactionTriggers.TurnsExceed(n))`** — a turn/message-count trigger avoids dragging in a
  `Microsoft.ML.Tokenizers.Tokenizer` (a `TokensExceed` trigger needs one). Cancel the token when a new turn
  arrives, before taking your turn gate, so a resumed conversation aborts the summary fast and the history is
  left untouched, retried next time. (Observed on 1.10.0; verify the API surface against the installed assembly.)
- **Compaction summarizer chokes on tool history (Anthropic).** `SummarizationCompactionStrategy` replays the
  older turns — including `FunctionCallContent`/`FunctionResultContent` from the agent's tool calls — to the
  summarizer `IChatClient`. The OpenAI client serializes those as `tool_calls`/`role:tool` messages, and
  Anthropic (via LiteLLM) rejects any request carrying tool blocks without a `tools=` param:
  `litellm.UnsupportedParamsError: Anthropic doesn't support tool calling without 'tools=' param specified`
  (HTTP 400). Compaction then silently no-ops (the event shows `BeforeCount == AfterCount`). **Fix:** wrap only
  the summarizer in a `DelegatingChatClient` that flattens tool content to text for the summary request —
  `FunctionCallContent` -> `[called tool name(args)]`, `FunctionResultContent` -> a user-role `[tool result: ...]`
  line (demote `role:tool` to user so there is no orphan tool block). The live session history is untouched, and
  the older turns were being collapsed to prose anyway. (`modify_params: True` on LiteLLM also clears the 400 by
  injecting a dummy tool, but prefer the client-side flatten so the gateway stays untouched.)

## Observability: OpenTelemetry, collector fan-out, Langfuse, dashboard

Exporting GenAI telemetry and fanning it out: instrumenting the agent + chat client with `UseOpenTelemetry`, the
`UseOtlpExporter()`/`AddOtlpExporter()` `NotSupportedException`, an OpenTelemetry Collector
(`CommunityToolkit.Aspire.Hosting.OpenTelemetryCollector`) forwarding to the Aspire dashboard and a self-hosted
Langfuse, the collector-in-container -> dashboard `host.docker.internal`/TLS gauntlet (and the proper dev-cert
fix), self-hosting Langfuse v3 (6 containers + headless `LANGFUSE_INIT_*` keys), and dashboard ergonomics
(`WithParentRelationship` grouping, pinning the URL, dropping the login token). Also: connecting one
round-trip trace across the bus (the `"MassTransit"` source + keeping an Activity current at publish),
capturing gRPC-client hops via HttpClient instrumentation without pulling AspNetCore into a trimmed
ServiceDefaults, dropping the redundant GenAI HTTP span with `FilterHttpRequestMessage`, separating a
real-time play-out wait into its own span, RT-thread-safe custom metrics (Meter + ObservableGauge), and the
in-memory dashboard being gone once the run stops. See
[observability-otel-langfuse.md](observability-otel-langfuse.md).

## Windows build + rename locks (stop the app first)

A running .NET app holds its `bin` DLLs open. After editing that project's **source**, a build tries to
overwrite the locked DLL and fails:

    error MSB3027/MSB3026: Could not copy "...\X.dll" ... being used by another process.
    The file is locked by: "X (<pid>)"

Likewise `mv`/rename of a project directory fails with **Permission denied** when a process holds a handle
inside it (the running app, or MSBuild node-reuse / `VBCSCompiler` holding `obj` DLLs). Before
building/renaming:

1. Stop the running app (Aspire: stop `aspire run`, or stop the `aspire` / `<AppHost>` / child processes).
2. `dotnet build-server shutdown` (releases the persistent MSBuild nodes, `VBCSCompiler`, Razor server).

An editor's C# Dev Kit / Roslyn language server can also hold handles; closing the folder or stopping that
server may be needed if locks persist after the above.

**Verifying a compile without stopping the app.** Compilation runs *before* the failing DLL copy, so to
check whether your edits build while the stack is up, filter the real diagnostics out of the lock noise:

    dotnet build <proj> 2>&1 | grep -E "error CS|warning CS"

No `CS` lines means it compiled clean and only the `MSB3026/MSB3027` copy step failed on the lock.
(`TreatWarningsAsErrors` surfaces warnings as `warning CS` here too, so this catches those.)

**Do not reach for `-t:CoreCompile` to skip the copy** — run in isolation it skips `ResolveReferences` and
emits a wall of bogus `CS0518/CS0246 'System'/'System.String' could not be found`. Use the full `dotnet build`
(or `-t:Compile`) above and filter; the compile runs before the locked copy regardless.
