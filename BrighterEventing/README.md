# Brighter Eventing – Publisher & Subscriber

Sample publisher and subscriber applications demonstrating **Paramore.Brighter** event streaming with the drivers and requirements from the **SE-Brighter High Level Design** document. The solution uses **clean architecture** so you can switch between **Azure Service Bus** and **RabbitMQ** via configuration.

## HLD drivers and requirements covered

| HLD driver / requirement | Where it's shown |
|--------------------------|------------------|
| **1. Separation of concerns** | Wire shapes (`LgsEventWire`, `IInternalEventPayload`) and Brighter events in `BrighterEventing.Messaging`; publisher handlers separate from transport. |
| **2. Encapsulation of cross-cutting concerns** | Handler pipeline (logging, retry) and optional inbox/outbox middleware. |
| **3. Reliability via Outbox and Inbox** | **Publisher**: Postgres outbox; **Subscriber**: Postgres inbox for de-duplication. |
| **4. Durable execution** | Outbox: messages persisted in Postgres before being sent. |
| **5. Transport flexibility** | Single `Transport` setting (`RabbitMQ` or `AzureServiceBus`). Swap broker via config. |

## Message flow

### End-to-end

```
[Publisher]                          [RabbitMQ]                    [Subscriber]
     |                                     |                              |
     | 1. Timer: SendAsync(PublishWrappedEnvelopeCommand)                 |
     | 2. Handler: BeginTransaction (+ optional DemoOrders row)           |
     | 3. DepositPostAsync(LgsEnvelopeBrighterEvent or RabbitInternal…) → Outbox |
     | 4. Commit transaction                                               |
     | 5. ClearOutboxAsync(ids) → read Outbox, publish to broker -------->|  (exchange/queues)
     |    then set Outbox.Dispatched = now                                 |
     |                                     | 6. Message on queue            |
     |                                     |------------------------------>| 7. ServiceActivator receives
     |                                     |                              | 8. Inbox middleware: check duplicate
     |                                     |                              | 9. Handler runs (LgsEnvelopeHandler / RabbitInternalEnvelopeHandler)
     |                                     |                              | 10. Inbox middleware: INSERT into Inbox
```

- **Outbox (Publisher)**: Rows are added in step 3. They should be cleared in step 5 when `ClearOutboxAsync` sends the message to RabbitMQ and sets the `Dispatched` column. If rows keep stacking with `Dispatched` NULL, step 5 is likely failing (e.g. broker error or wrong API usage).
- **Inbox (Subscriber)**: Rows are added only in step 10, after the Subscriber has received a message from RabbitMQ and the handler has run. So you only see inbox entries when the Subscriber is running and actually consuming messages.

### Why outbox stacks and inbox is empty

1. **Subscriber not running**  
   If only the Publisher is running, messages may be published to RabbitMQ (if `ClearOutboxAsync` succeeds), but nobody consumes them. Inbox is written when the **Subscriber** processes a message, so with no Subscriber you get no inbox rows.

2. **ClearOutboxAsync not completing successfully**  
   If `ClearOutboxAsync` throws or doesn’t update the outbox (e.g. wrong parameter type in Brighter 10.0.1), outbox rows are never marked as dispatched and keep accumulating. This sample uses the correct Brighter 10.0.1 API (ClearOutboxAsync with the ids from DepositPostAsync). Check the Publisher logs for exceptions right after “Decoupled invocation of message” and ensure outbox rows get a non-NULL `Dispatched` after a run.

3. **Order of operations**  
   Start the **Subscriber first**, then the Publisher. That way queues and bindings exist on the broker before the Publisher starts clearing the outbox and publishing.

### Quick checks

- **Outbox**: In Postgres, `SELECT "MessageId", "Topic", "Dispatched" FROM "Outbox" LIMIT 5;`  
  If `Dispatched` is always NULL, clearing is not succeeding.
- **RabbitMQ**: Confirm queues `order.lgs.wrapped.queue` and `rabbit.internal.wrapped.queue` (and bindings for routing keys `order.lgs.wrapped` / `rabbit.internal.wrapped`).
- **Inbox**: Run the Subscriber; after it consumes messages you should see rows in `"Inbox"`. The Inbox table is created at startup using Brighter's `PostgreSqlInboxBuilder.GetDDL` in a single command; if you created the table manually with a different schema, drop it and restart the Subscriber so Brighter can recreate it.

## Solution layout

- **BrighterEventing.Messaging** – NuGet-style library (`net6.0` + `net8.0`): custom **`IAmAMessageMapperAsync`** implementations (`LgsEnvelopeBrighterEventMessageMapper`, `RabbitInternalEnvelopeBrighterEventMessageMapper`) turn wire (`LgsWire` / `RabbitWire`) into the published JSON body (`LgsPublishedMessage` / `RabbitPublishedMessage` with `common` metadata). `DepositPostAsync` uses the **async** mapper pipeline only — sync `IAmAMessageMapper<T>` is not invoked for outbox deposit. Brighter events `LgsEnvelopeBrighterEvent` and `RabbitInternalEnvelopeBrighterEvent`, routing key constants. No HTTP Event Service — payloads are built in the host and passed through Brighter to the broker.
- **BrighterEventing.Publisher** – `PublishWrappedEnvelopeCommand` + handler: same DB transaction writes **`DemoOrders`** + outbox deposit; transport from config selects **Azure Lgs-shaped** vs **Rabbit internal-shaped** input.
- **BrighterEventing.Subscriber** – Consumes wrapped events; Postgres inbox; retry + inbox idempotency on handlers.

### Common metadata (`common` property)

The Implementation Guide’s recommended minimum fields are populated **once**, on the nested `common` object (`CommonEventMetadata`), alongside the unchanged Lgs / Rabbit body fields at the root. The Brighter message mappers map from wire inputs without repeating the same values at two levels. Applications set `LgsWire` / `RabbitWire` on the event; optional `EnvelopeMapOptions` fills correlation and message id for Rabbit-style publishes.

## Prerequisites

- .NET 8 SDK
- PostgreSQL (for outbox and inbox)
- RabbitMQ (default) or Azure Service Bus

## Quick start (RabbitMQ + Postgres)

1. Create database and run `scripts/postgres-outbox.sql` and `scripts/postgres-inbox.sql` (or let the apps create tables on first run).
2. **Set connection strings and URIs** in **secrets.json** (see [Secrets](#secrets-local-development) below) so they are not checked in.
3. Run RabbitMQ (e.g. `docker run -d -p 5672:5672 rabbitmq:3-management`).
4. Start Subscriber: `cd src/BrighterEventing.Subscriber && dotnet run`
5. Start Publisher: `cd src/BrighterEventing.Publisher && dotnet run`

## Secrets (local development)

Connection strings and broker URIs are **not** in `appsettings.json` so they are not committed. Add a **`secrets.json`** file in each project folder (next to `appsettings.json`). It is **gitignored** and overwrites appsettings when present.

**Publisher** – create `src/BrighterEventing.Publisher/secrets.json`:

```json
{
  "ConnectionStrings": {
    "BrighterOutbox": "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true"
  },
  "RabbitMQ": {
    "AmqpUri": "amqps://user:password@host:5671/vhost"
  }
}
```

**Subscriber** – create `src/BrighterEventing.Subscriber/secrets.json`:

```json
{
  "ConnectionStrings": {
    "BrighterInbox": "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true"
  },
  "RabbitMQ": {
    "AmqpUri": "amqps://user:password@host:5671/vhost"
  }
}
```

Copy from `secrets.json.template` in each project and fill in your values. For production, use environment variables, Azure Key Vault, or your host’s secret store.

## Configuration

- **Transport**: `"Transport": "RabbitMQ"` (default) or `"AzureServiceBus"`. For Azure Service Bus, set `Transport` to `AzureServiceBus` and add `AzureServiceBus:ConnectionString` in secrets.json (and optionally `AzureServiceBus:TopicName`; the Publisher uses topics `order.created` and `greeting.made`).
- **Publisher**: `Publisher:SendIntervalSeconds` (default 5).
- **RabbitMQ**: `RabbitMQ:AmqpUri` (set via secrets.json or env), `RabbitMQ:Exchange`, `RabbitMQ:SubscriptionName` (Subscriber).
- **Connection strings**: `ConnectionStrings:BrighterOutbox` (Publisher), `ConnectionStrings:BrighterInbox` (Subscriber) — set via `secrets.json` or env; not in appsettings.

### Messaging (retry, dead-letter, backoff)

Shared settings for both RabbitMQ and Azure Service Bus live under **`Messaging`** in appsettings (Subscriber).

| Key | Description | Default |
|-----|-------------|---------|
| `Messaging:Consumer:MaxRetryCount` | Max requeues before dead-letter | 3 |
| `Messaging:Consumer:RequeueDelayMs` | Delay before requeue (backoff) | 5000 |
| `Messaging:Consumer:ReceiveTimeoutMs` | Message receive/processing timeout (ms) | 400 |
| `Messaging:AzureServiceBus:MaxDeliveryCount` | ASB max deliveries before DLQ | 5 |
| `Messaging:AzureServiceBus:LockDurationSeconds` | ASB lock duration (seconds) | 60 |
| `Messaging:AzureServiceBus:DeadLetteringOnMessageExpiration` | Dead-letter on TTL expiry | true |
| `Messaging:AzureServiceBus:DefaultMessageTimeToLiveDays` | Default message TTL (days) | 3 |

- **Subscriber**: Uses these when creating subscriptions (RMQ and ASB). `LgsEnvelopeHandler` / `RabbitInternalEnvelopeHandler` use a Polly retry pipeline (`ConsumerRetryPipeline`) with exponential backoff driven by `Messaging:Consumer` (in-handler retries).
- **Publisher**: `Messaging:DeferredDelaySeconds` is reserved for future deferred/scheduled send.

### Testing retry and dead-letter

- **Simulated failures**: `Testing:SimulateFailureCount` is reserved; wire a handler to throw on first N invocations if you want to reproduce DLQ behaviour (previously demonstrated with `GreetingMadeHandler`).
- **How to test**: Run Subscriber with `Testing:SimulateFailureCount: 2` and `Messaging:Consumer:MaxRetryCount: 3`, start the Publisher, and watch logs: you should see "Simulated failure 1/2", then retries, then "Simulated failure 2/2", then "Greeting received" on the next delivery. With enough failures (e.g. set `SimulateFailureCount` higher than delivery count), messages will move to the broker’s dead-letter queue (RabbitMQ or ASB DLQ).
- **Inbox/outbox**: Inbox and outbox behaviour is unchanged; simulated failures exercise the retry/DLQ path before a message is completed and recorded in the inbox.

### Logging and shutdown

- **Pipeline logs**: Brighter logs "Building send async pipeline" and "Found X async pipelines" at Info for each message. The Subscriber sets `Logging:LogLevel:Paramore.Brighter.CommandProcessor` to **Warning** to avoid this per-message noise. The Polly resilience pipeline is cached by the registry; Brighter’s handler resolution is per dispatch by design.
- **Azure Service Bus**: "No Cloud Events data schema/subject/trace…" warnings are suppressed by setting `Paramore.Brighter.MessagingGateway.AzureServiceBus` to **Error** (so only errors are logged).
- **Graceful shutdown**: `HostOptions:ShutdownTimeout` is set to 30 seconds so the Service Activator can stop its message pumps before the host disposes the synchronization context, reducing `ObjectDisposedException` on Ctrl+C.

## Swapping transport

Change only the `Transport` key and the corresponding broker settings in config. Handlers and contracts stay unchanged.

## Limitations

- Outbox sweeper not wired in this sample (Brighter 10.0.x); enable where your version supports it.
- Azure Service Bus is supported: set `Transport` to `AzureServiceBus` and provide `AzureServiceBus:ConnectionString` in secrets.json. The Publisher creates topics `order.created` and `greeting.made`; the Subscriber creates subscriptions under those topics when using `MakeChannels: Create`.
- Inbox retention: implement a job to clear old inbox rows if needed.

## References

- [Brighter docs](https://brightercommand.gitbook.io/paramore-brighter-documentation/)
- HLD: `SE-Brighter High Level Design-100326-053043.pdf`
