# Observability: OpenTelemetry, the collector fan-out, and self-hosted Langfuse

Wiring GenAI telemetry out of a .NET Aspire app: instrumenting a Microsoft Agent Framework agent, fanning
spans out through an OpenTelemetry Collector to both the Aspire dashboard and a self-hosted Langfuse, and the
container-to-host TLS dance that costs an evening. Version-anchored to: Aspire 13.4,
`CommunityToolkit.Aspire.Hosting.OpenTelemetryCollector` 13.4.0, `Microsoft.Agents.AI` 1.10.0,
`Microsoft.Extensions.AI` 10.7.0, `OpenTelemetry.*` 1.15.x, collector image
`otel/.../opentelemetry-collector-contrib`, Langfuse **v3**. Verify against installed versions before relying.

## Instrument the agent and the chat client (two layers, one source)

The Agent Framework and `Microsoft.Extensions.AI` both ship GenAI OpenTelemetry instrumentation following the
GenAI semantic conventions. Wire both so a turn reads `invoke_agent -> chat -> execute_tool` in the trace:

- **Agent layer:** `agent.AsBuilder().UseOpenTelemetry(sourceName, configure: o => o.EnableSensitiveData = b).Build()`
  (`Microsoft.Agents.AI`, `OpenTelemetryAgent`). One `invoke_agent` span per turn, tool calls nested.
- **Chat layer:** `chatClient.AsBuilder().UseOpenTelemetry(loggerFactory, sourceName, c => c.EnableSensitiveData = b).Build()`
  (`Microsoft.Extensions.AI`, `OpenTelemetryChatClient`). One `chat {model}` span per model round-trip. Put it
  *inside* the agent's function-invocation layer (i.e. wrap the raw client you hand to `ChatClientAgent`), so each
  span is one real call on the wire. Chain other delegating clients with `.Use(inner => new MyClient(inner))`.
- **`EnableSensitiveData`** controls whether prompts/completions/tool args land on spans. Off by default. It is an
  app-level choice (what goes on the span); it applies to every exporter, so it is not Langfuse-specific.
- **Register the source** on the tracer provider or nothing exports it: `AddOpenTelemetry().WithTracing(t =>
  t.AddSource(sourceName))`. Pass the same explicit `sourceName` to both `UseOpenTelemetry` calls so one
  `AddSource` covers the whole agent. (`ServiceDefaults` already adds `Environment.ApplicationName` as a source,
  but relying on that coincidence is fragile.)

## Connect the whole round trip and shape the spans

The GenAI spans above only cover the agent. To get one connected trace across an event-driven system (e.g. `capture -> transcribe -> relevance -> agent -> synthesize`):

- **Bus hops:** MassTransit (v8) emits Activities on source `"MassTransit"`; `AddSource("MassTransit")` in *every* service stitches its publish/consume spans into one trace. Propagation is automatic **only if an Activity is current when you publish** — MassTransit injects `traceparent` into the message headers from `Activity.Current`. So keep your stage span current across `IBus.Publish` / `context.Publish`, or the consumer starts a fresh root and the trace fragments.
- **gRPC/HTTP client hops without AspNetCore:** `AddHttpClientInstrumentation()` captures outbound gRPC-client calls as spans (gRPC rides HTTP/2), so you do **not** need `AddGrpcClientInstrumentation` (still beta) or `AddAspNetCoreInstrumentation`. That matters because a trimmed `ServiceDefaults` referenced by **Worker** projects must stay AspNetCore-free — put HttpClient instrumentation there; add AspNetCore *server* instrumentation only inside the web projects.
- **Stage spans at the seams auto-instrumentation can't see:** one shared `ActivitySource` (e.g. `"Zoe.Pipeline"`) for the work between hops. A small `using` scope that opens the span and records a duration `Histogram` gives you the trace bar and the metric in one call. Register the source with `AddSource`.
- **Drop the redundant GenAI HTTP span:** the agent's `chat` span already describes the model call; the raw `HttpClient` `POST /chat/completions` underneath doubles the LLM span count (and clutters Langfuse). Suppress just that one, provider-agnostically, by path:
  `http.FilterHttpRequestMessage = req => req.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) != true;` — leaves gRPC and bus HTTP intact.
- **Separate a real-time *wait* from compute:** if a method blocks on backpressure (streaming audio out at playback speed through a bounded channel), wrap that wait in its own span (e.g. `playback.render`) so the enclosing bus-consume span isn't misread as a stall — a 25 s consume over a 2 s synthesize is just the play-out wait, not synthesis cost.
- **Custom metrics on a real-time thread:** prefer a `Meter` (Counters + ObservableGauge) over a hand-rolled periodic logger/file. `Counter<T>.Add(n)` with **no tags** is allocation-free and lock-free — safe from a PortAudio audio callback. For "current depth" gauges, store the latest value in an `Interlocked` field and read it from an `ObservableGauge` callback (sampled at export), so the hot path is one interlocked store. Register with `AddMeter(name)`; it rides the same OTLP path. The default metric export interval is ~60 s — set `OTEL_METRIC_EXPORT_INTERVAL` (ms) lower to watch fast-moving gauges live.
- **Dashboard telemetry is in-memory and ephemeral:** it vanishes when the AppHost stops, and `aspire logs` / `aspire otel ...` return *"No running AppHost found"* once it is down. For evidence that outlives a run, read it live while the run is up, have the service also write its own file, or fan the collector to a persistent backend (Tempo/Loki).

## `UseOtlpExporter()` and `AddOtlpExporter()` cannot be combined

The cross-cutting `UseOtlpExporter()` (what the default `ServiceDefaults` calls, exports all signals to
`OTEL_EXPORTER_OTLP_ENDPOINT`) **throws `NotSupportedException`** if any signal-specific `AddOtlpExporter()` is
also registered on the same `IServiceCollection`:

    Signal-specific AddOtlpExporter methods and the cross-cutting UseOtlpExporter method
    being invoked on the same IServiceCollection is not supported.

So you cannot add a *second* OTLP destination via `AddOtlpExporter` next to `ServiceDefaults`. To add one without
touching `ServiceDefaults`, construct the exporter and attach it as a processor instead (this bypasses the
registration that conflicts):

    var exporter = new OtlpTraceExporter(new OtlpExporterOptions {
        Endpoint = new Uri(url), Protocol = OtlpExportProtocol.HttpProtobuf, Headers = $"Authorization=Basic {b64}",
    });
    tracing.AddProcessor(new BatchActivityExportProcessor(exporter));

`OtlpTraceExporter`/`OtlpExporterOptions` are public in `OpenTelemetry.Exporter.OpenTelemetryProtocol`. (Better
still: don't fan out from the app at all — send once to a collector and fan out there, see below.)

## Collector fan-out (CommunityToolkit hosting integration)

`CommunityToolkit.Aspire.Hosting.OpenTelemetryCollector` (versioned in lockstep with Aspire; 13.4.0 needs
Aspire >= 13.4.0) runs the collector as a container and forwards every app's OTLP through it:

    builder.AddOpenTelemetryCollector("otel-collector", s => s.EnableGrpcEndpoint = true)
        .WithConfig("collector-config.yaml")   // bind-mounts the file (relative to AppHost dir) at /config/<name>
        .WithAppForwarding();                   // points every OtlpExporter resource's OTEL endpoint at the collector

- It injects `ASPIRE_ENDPOINT` (the dashboard OTLP url, read from `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` ->
  `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL` -> default `http://localhost:18889`) and `ASPIRE_API_KEY`
  (`AppHost:OtlpApiKey`) into the collector container. Your config references them as `${env:ASPIRE_ENDPOINT}` /
  `${env:ASPIRE_API_KEY}`. The default the toolkit falls back to is `localhost:18889`, which under `aspire run`
  may not be where the dashboard actually listens — pin it (see dashboard section) or override `ASPIRE_ENDPOINT`.
- `WithConfig` **replaces** the default config; your YAML must be complete (receivers, exporters, service
  pipelines). Default image is `opentelemetry-collector-**contrib**` (has `filter`, `transform`, `otlphttp`).
- `WithAppForwarding` is app-wide: it routes *every* service (via `OtlpExporterAnnotation`) through the collector,
  not just one. Filter per-destination in the collector config (e.g. a `filter/...` processor that drops spans
  whose `service.name != "agent"` on the Langfuse pipeline) if a sink should see only some services.
- Minimal config: an `otlp/aspire` exporter (dashboard, all signals) plus an `otlphttp/langfuse` exporter on a
  traces pipeline. The `otlphttp` exporter appends `/v1/traces` to its `endpoint`, so point it at Langfuse's
  base `http://langfuse-web:3000/api/public/otel`.

## The collector-in-container -> dashboard hop (a TLS/networking gauntlet)

The collector runs in a container; the Aspire dashboard's OTLP receiver runs on the host. Three failures in
sequence, each unmasking the next (all observed live):

1. **`connection refused` on `host.docker.internal` (192.168.65.254:18889).** The dashboard OTLP endpoint binds
   `localhost` only, unreachable from a container. Fix: bind all interfaces —
   `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL=http://0.0.0.0:18889` in the AppHost `appsettings.json`. Binding is
   independent of TLS.
2. **`OptionsValidationException: ... must be an https address unless ASPIRE_ALLOW_UNSECURED_TRANSPORT ...`** —
   the dashboard refuses a plain-`http` OTLP endpoint. Either set `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`
   (plaintext, dev-only) **or** keep `https://` (preferred, see below).
3. **`tls: first record does not look like a TLS handshake`** — with a plaintext (`http`) endpoint, the
   collector's `otlp` exporter still attempts TLS (this collector version does not infer insecure from the
   `http://` scheme). For a plaintext endpoint add `tls: { insecure: true }` to the exporter.

**Do it properly with the dev cert (no insecure flags).** Keep the dashboard on HTTPS (it serves the ASP.NET dev
cert). A container has no access to the host cert store, so export the dev cert to a PEM and mount it as the
exporter's CA. Because the container dials `host.docker.internal` but the dev cert is issued for `localhost`,
override the verified name:

    # AppHost: export once, then mount into the collector
    dotnet dev-certs https --export-path certs/aspire-dev-cert.pem --format Pem --no-password
    # .WithBindMount(certPath, "/etc/otelcol/aspire-dev-cert.pem", isReadOnly: true)
    # .WithEnvironment("ASPIRE_ENDPOINT", "https://host.docker.internal:18889")

    # collector-config.yaml, otlp/aspire exporter:
    tls:
      ca_file: /etc/otelcol/aspire-dev-cert.pem
      server_name_override: localhost

This keeps `app -> collector` on HTTPS too (the toolkit makes the collector's receivers HTTPS when the dashboard
URL is `https`, via its `WithHttpsCertificateConfiguration`). The dev-cert path is the *correct* end state but was
not yet runtime-confirmed in the session it was written — verify the `app -> collector` receiver-trust hop on a
real run (watch app logs for TLS export errors). The plaintext path in step 3 *was* confirmed reachable. The
collector's OTLP receiver being HTTPS is now confirmed (next section).

## A non-.NET container exporting to the collector (the `WithAppForwarding` gap + the HTTPS receiver)

`WithAppForwarding()` injects `OTEL_EXPORTER_OTLP_ENDPOINT` into **project** resources only, not into a container
added with `AddDockerfile`/`AddContainer`. A polyglot worker (a Node/Python sidecar) therefore self-emits nothing —
its exporter has no endpoint and silently no-ops. Set it explicitly, pointing at the collector by resource name
(which resolves on the Aspire network: `getent hosts otel-collector` -> the container IP):

    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "https://otel-collector:4318")

**It must be `https`.** The toolkit puts the whole OTLP mesh on TLS with the dev cert, so the collector's OTLP
receiver is HTTPS. A plain-`http` exporter is rejected and the spans never land; the tell is in the *collector*
log, not the client (which just sees a dropped connection):

    error http: TLS handshake error ... client sent an HTTP request to an HTTPS server  {component: otlp receiver}

The container has no access to the dev cert and the cert's name won't match `otel-collector`, so a non-.NET
exporter must export over HTTPS and **skip verification** (dev-only; this affects only the OTLP leg). For the
OpenTelemetry Node exporter (`@opentelemetry/exporter-trace-otlp-proto`), the URL still comes from the env:

    new OTLPTraceExporter({ httpAgentOptions: { rejectUnauthorized: false } })

Verified live: a span from the worker image is accepted (`status 200`, no TLS error). Confirm reachability with
node (slim images lack `curl`): `net.connect(4318, "otel-collector")`.

**The Langfuse leg is filtered by `service.name`.** The collector's Langfuse pipeline usually keeps only the agent
(a `filter` processor, OTTL: drop when the condition is true). To send a second service there too, broaden the
drop condition rather than remove it:

    - 'resource.attributes["service.name"] != "agent" and resource.attributes["service.name"] != "zoe-deep-harness"'

**Validate a config change before restarting.** `otelcol validate --config <file>` (exit 0 = valid), run in the
same collector image the AppHost uses. On Git Bash prefix `MSYS_NO_PATHCONV=1`, or the `-v host:/container`
mount's *container* path is mangled to `C:/Program Files/Git/...`.

**Do not hand-attach a test worker to the Aspire session network.** A container you `docker run --network
aspire-session-network-...` onto can resolve resource names but often hits `ENETUNREACH`/`EAI_AGAIN` reaching the
Aspire-managed broker/collector (the proxy DNS and routing are Aspire's). For an isolated round-trip test, stand
up your own `docker network` plus a standalone `rabbitmq:4` and run the worker there.

## Self-hosted Langfuse is a 6-container stack (v3)

Langfuse **v3** self-host is not one image. Replicate its `docker-compose.yml`: `postgres`, `clickhouse`
(traces/observations store, **not** swappable — Langfuse talks to it directly), `redis` (ingestion queue),
`minio` (S3 blobs; pre-create the bucket via `mkdir -p /data/<bucket>`), `langfuse/langfuse:3` (web/API),
`langfuse/langfuse-worker:3` (queue drainer). Web+worker share the DB/store/queue/S3 env; only web takes
`NEXTAUTH_*`. Required secrets: `SALT`, `ENCRYPTION_KEY` (64 hex chars), `NEXTAUTH_SECRET`. v2 was a single
container + Postgres only — far lighter but deprecated; choose deliberately.

**Headless API keys.** `LANGFUSE_INIT_*` env on the web container seeds an org/project/user and the project
keys (`LANGFUSE_INIT_PROJECT_PUBLIC_KEY` / `_SECRET_KEY`) on first boot, idempotent on later boots. This lets the
AppHost own deterministic keys and inject the same ones into the collector's exporter
(`Authorization=Basic base64(pk:sk)`) with no manual UI step. Langfuse ingests OTLP **traces only**, over
**HTTP/protobuf** (no gRPC), at `/api/public/otel/v1/traces`.

Langfuse's storage engines are not pluggable, so you cannot "replace ClickHouse with Tempo" *inside* Langfuse.
But because the collector fan-out is provider-agnostic, swapping Langfuse for the Grafana stack (Tempo) is a
one-line exporter change in the collector config with zero new containers — the real choice is product (Langfuse's
LLM-native cost/eval/session views) vs. generic trace viewing (Tempo).

## Dashboard ergonomics: group resources, pin the URL, drop the login token

- **Group resources** in the dashboard with `child.WithParentRelationship(parentResource)` (on the child, pass the
  parent `IResource`). Pure display nesting (collapsible group); does not affect startup/networking (that is
  `WaitFor`/`WithReference`). Good for collapsing a multi-container stack (e.g. the Langfuse services under
  `langfuse-web`) into one row.
- **AppHost `appsettings.json` is the launcher-independent lever for dashboard config.** Confirmed: setting
  `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL` there changed the dashboard's OTLP bind under `aspire run`. `aspire run`
  has **no `--launch-profile` option** (`aspire run --help`), so it does not honor `launchSettings.json` profiles
  the way `dotnet run` does — configure the dashboard via `appsettings.json`, not a launch profile.
- **Pin the dashboard URL so you can bookmark it.** `ASPNETCORE_URLS=https://localhost:18888` pins the frontend
  port. But the churn that makes you re-copy the URL is usually the rotating **login token** (`?t=...`), not the
  port (non-isolated `aspire run` already uses deterministic ports; `--isolated` is the one that randomizes). To
  make a bookmark "just refresh" across restarts, also drop dev auth:
  `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` (dev-only; also relaxes OTLP auth, harmless — the collector's
  API key is then ignored and the HTTPS/cert trust is independent). The `ASPNETCORE_URLS` frontend pin follows the
  same proven `appsettings.json` pattern but was not separately runtime-confirmed — check the landed port.
