---
name: masstransit-nodejs-interop
description: >-
  Interoperating between a Node/TypeScript service and a .NET MassTransit app over RabbitMQ, with no .NET runtime
  on the Node side. Use when a non-.NET worker must consume or publish MassTransit messages, when weighing or
  rejecting the masstransit-rabbitmq npm library, when RabbitMQ 4.x rejects a client (403 as guest, a frame_max
  negotiation reset / ECONNRESET, 541 INTERNAL_ERROR from transient_nonexcl_queues or global_qos being
  deprecated), or when a message published to a message-type exchange never reaches a MassTransit consumer.
  Covers the MassTransit JSON envelope + exchange topology to hand-roll with raw amqplib, why masstransit-rabbitmq
  0.1.x is unfit for RabbitMQ 4.x, the RabbitMQ 4.x incompatibilities and their fixes, broker-log diagnosis, and
  the CJS-double-wrap ESM import trap.
allowed-tools: Read, Bash, Edit, Grep
---

# Speaking MassTransit from Node over RabbitMQ

Getting a Node/TS service to interoperate with a .NET app that uses MassTransit, keeping the .NET side an
ordinary typed `Publish`/`IConsumer<T>` (no polyglot special-casing).

## Do not use `masstransit-rabbitmq` (0.1.x); hand-roll with raw `amqplib`

The `masstransit-rabbitmq` npm package (0.1.x observed) is unfit for RabbitMQ 4.x and for real interop, for four
independent reasons found live:

1. **No credentials.** Its `HostSettings` has only `host`/`virtualHost`/`port`/`ssl`/`heartbeat` — no
   username/password — so it dials as `guest` and RabbitMQ refuses (`403 ACCESS_REFUSED`, PLAIN).
2. **frame_max too low** (see below) — the connection is reset even with valid creds.
3. **Deprecated features** — it uses `globalPrefetch: true` (global QoS) and declares transient non-exclusive
   queues, both denied by RabbitMQ 4.x (see below).
4. **It never binds the message-type exchanges.** Its receive endpoint only creates `fanout exchange = queueName`
   + `queue = queueName` and binds those; a message .NET publishes to `Namespace:TypeName` never routes to it.

Raw `amqplib` emitting/parsing the envelope yourself is simpler and correct. Put it behind a small interface so
the transport is a swappable provider (the shape MassTransit itself gives .NET).

## The MassTransit wire contract (RabbitMQ)

- **Exchange per message type:** `"<Namespace>:<TypeName>"`, e.g. `MyApp.Contracts:OrderSubmitted`. Declare it
  **fanout, durable**. MassTransit's consumer receive endpoints bind these exchanges to their queues — you must
  replicate that binding.
- **Envelope** (JSON body): the essentials are `messageId`, `messageType` (a URN array), `message` (the payload),
  `sentTime`. Content type `application/vnd.masstransit+json`. `sourceAddress`/`destinationAddress`/`host` are
  optional for consume.

      { "messageId": "<guid>", "messageType": ["urn:message:MyApp.Contracts:OrderSubmitted"],
        "message": { ...payload... }, "sentTime": "2026-...Z" }

- **Consume what .NET published:** assert a durable queue, assert the message-type exchange (fanout durable), bind
  queue -> exchange, consume, parse `envelope.message`.
- **Publish to a .NET `IConsumer<T>`:** publish the envelope to that message-type exchange; .NET's receive-endpoint
  queue is already bound to it.
- **Casing:** MassTransit deserializes the payload case-insensitively, so publishing PascalCase (matching the .NET
  record) is safe; when *consuming*, read fields case-insensitively (accept `Id` or `id`) to be robust. The
  message-type URN in the envelope, not the payload, is how you tell which type it is.

## RabbitMQ 4.x incompatibilities (and the fixes)

RabbitMQ 4.x tightened defaults; a 3.x-era client trips several. All confirmed via the broker log.

- **frame_max floor 8192.** amqplib defaults `frameMax` to 4096, below the 4.x minimum, so the broker resets the
  connection after auth: broker log `failed to negotiate connection parameters: negotiated frame_max = 4096 is
  lower than the minimum allowed value (8192)`; the client just sees `ECONNRESET`. Set it: `amqp.connect({ ...,
  frameMax: 131072 })` or `...?frameMax=131072` in the URL.
- **Deprecated features denied by default**, each killing the connection with `541 INTERNAL_ERROR` on the
  offending op:
  - `global_qos` — from `channel.prefetch(count, /*global*/ true)`. Use per-consumer QoS: `channel.prefetch(1)`
    (global defaults false).
  - `transient_nonexcl_queues` — a non-durable, non-exclusive queue. Make queues **durable**, or make a
    per-instance queue **exclusive** (transient *exclusive* is permitted). Both dodge it.
  - Broker-side alternative (if you cannot change the client): `deprecated_features.permit.<name> = true` in a
    `rabbitmq.conf` under `/etc/rabbitmq/conf.d/`, then restart the broker. Note: a runtime `rabbitmqctl eval
    'application:set_env(rabbit, permit_deprecated_features, ...)'` did **not** take effect for the queue.declare
    in testing — it is effectively config/boot-time.
- **guest is localhost-only** (as in 3.x) — pass real credentials for any non-localhost connection.

## Diagnosis: read the RabbitMQ broker log, not just the client error

The client sees vague errors (`ECONNRESET`, `541`); the broker log names the exact cause. `docker logs <rabbit>`
shows lines like `PLAIN login refused: user 'guest'`, `failed to negotiate ... frame_max`, and `operation
queue.declare caused a connection exception internal_error: Feature transient_nonexcl_queues is deprecated`.
Useful `rabbitmqctl`: `authenticate_user <u> <p>` (is the password right?), `list_connections user peer_host
state`, `list_queues name messages consumers` (is anyone consuming?).

**Validate the round trip** against a standalone `rabbitmq:4-management` container (own docker network): publish a
MassTransit envelope, confirm your worker consumes and decodes it, publish a report envelope, and read it back —
before wiring into the real app.

## CJS/ESM import trap (masstransit-rabbitmq and similar CJS libs)

If you do use a CJS package from an ESM (`"type": "module"`) project under Node16 resolution, watch for
double-wrapping: a CJS module with `exports.default = fn` and `__esModule` imported into ESM gives the *module
object* as the default, with the real callable one level in at `.default`. Probe the runtime shape rather than
trust the `.d.ts`:

    node --input-type=module -e "import * as ns from 'pkg'; console.log(typeof ns.default, typeof ns.default?.default)"

Named exports may live only on submodules (`pkg/dist/foo.js`), not the package root. And if you must monkey-patch
a transitive dep (e.g. patch `amqplib.connect`) so the library uses your patched copy, add an npm `overrides` for
that dep to force a single deduped instance — otherwise the library nests its own copy and your patch misses it.
