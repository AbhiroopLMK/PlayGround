# Brighter Eventing – Publisher & Subscriber

Sample publisher and subscriber applications demonstrating **Paramore.Brighter** event streaming with the drivers and requirements from the **SE-Brighter High Level Design** document. The solution uses **clean architecture** so you can switch between **Azure Service Bus** and **RabbitMQ** via configuration.

## HLD drivers and requirements covered

| HLD driver / requirement | Where it's shown |
|--------------------------|------------------|
| **1. Separation of concerns** | **`WireEnvelopeBuilder`** + domain events / legacy wire shapes (`LgsEventWire`, `IInternalEventPayload`) in **`BrighterEventing.Messaging`**; publisher handlers separate from transport. |
| **2. Encapsulation of cross-cutting concerns** | Handler pipeline (logging, retry) and optional inbox/outbox middleware. |
| **3. Reliability via Outbox and Inbox** | **Publisher**: Postgres outbox; **Subscriber**: Postgres inbox for de-duplication. |
| **4. Durable execution** | Outbox: messages persisted in Postgres before being sent. |
| **5. Transport flexibility** | Single `Transport` setting (`RabbitMQ` or `AzureServiceBus`). Swap broker via config. |

## Message flow

### BrighterEventing: end-to-end flows (registration → publish → map → subscribe)

The **primary** sample path uses **domain events** (`BrighterEventing.Sample.DomainEvents`: `OrderCreatedEvent`, `OrderUpdatedEvent`, `OrderCancelledEvent`), **`BrighterMessaging:Publisher` / `BrighterMessaging:Subscriber`** configuration, and **`WireEnvelopeBuilder`** for transport-specific JSON. The steps below match **`BrighterEventing.Publisher` / `BrighterEventing.Subscriber`** (and **`.Net6`** hosts).

#### 1. Registration (composition root)

| Step | Publisher | Subscriber |
|------|-------------|------------|
| Config | `BrighterMessaging:Publisher` — `Transport`, `Publications[]` (`EventType`, `RoutingKey`, optional `Topic`, optional `ServiceBusEventType`), ASB/Rabbit connection settings, optional Postgres/Cosmos outbox | `BrighterMessaging:Subscriber` — `Transport`, `Subscriptions[]` (`EventType`, `RoutingKey`, `Topic`, `SubscriptionName`, optional `AzureServiceBusFilterRules`), ASB/Rabbit settings, optional inbox |
| API | **`AddBrighterEventingPublisherMessaging`** (or Cosmos/PostgreSQL variants): binds options, registers **`IEventTypeRegistry`** from **`EventTypeCatalogBuilder`**, wires Brighter **`AddProducers`** (RMQ exchange or ASB **session-aware** producer registry) | **`AddBrighterEventingSubscriberMessaging`**: **`AddConsumers`** (RMQ or ASB channel factory), Polly retry pipelines per event type |
| Event catalog | **`catalog => catalog.AddSampleOrderEvents()`** maps logical names → CLR types + CloudEvents **`type`** | Same registry resolves handler **`Event`** types |

Azure Service Bus subscribers also register **`AzureServiceBusCorrelationRulesHostedService`** (see subscribe step).

#### 2. Map (domain event → Brighter `Message`)

- Handlers call **`CommandProcessor.PostAsync`** / **`DepositPostAsync`** with a domain **`Event`** (e.g. `OrderCreatedEvent`).
- **`IAmAMessageMapperAsync<TEvent>`** (e.g. `OrderCreatedEventMessageMapper`) calls **`WireEnvelopeBuilder.BuildForConfiguredTransport`**, passing Brighter’s **`Publication`** (topic + subject from registry) and **`IConfiguration`**.
- **RabbitMQ**: internal JSON envelope + exchange **routing key** from publication.
- **Azure Service Bus**: **LGS-shaped** JSON body; CloudEvents **subject** = publication subject / routing key.
- Optional **`ServiceBusEventType`** on the matching **`PublicationBinding`** row is resolved via **`BrighterPublisherPublicationMetadata.TryGetServiceBusEventType`** and copied to **`MessageHeader.Bag["serviceBusEventType"]`** so subscriptions can filter on that **custom property**.

#### 3. Publish (outbox → broker)

- With outbox: same transaction as business data → **`DepositPostAsync`** persists the **`Message`**; background **`ClearOutboxAsync`** dispatches to the broker.
- **Azure Service Bus**: producers are wrapped so **`BrighterEventingAzureServiceBusProducerSend`** sends with **`BrighterEventingServiceBusMessageConverter`**, which sets broker **`ServiceBusMessage.Subject`** from **`MessageHeader.Subject`** (Brighter’s default converter only sets **`cloudEvents:subject`** as an application property). This aligns with **correlation filters** on **Subject** and with **`BrokerSubject`** in config.

#### 4. Subscribe (consumer → handler)

- Brighter builds **topic + subscription entity** from **`Subscriptions`** (see **`BrighterMessagingBrokerRegistration`**). **`AzureServiceBusSubscriptionConfiguration.SqlFilter`** is left **empty** so Paramore does **not** create a **`sqlFilter`** SQL rule (its API only supports SQL filters).
- **`AzureServiceBusCorrelationRulesHostedService`** runs **after** host start (background work, **not** blocking **`StartAsync`**). It waits for the subscription to exist, then applies **Azure Service Bus** **`CorrelationRuleFilter`** rules from **`AzureServiceBusFilterRules`**: multiple rules are **OR**; conditions inside one rule are **AND**. It removes **`sqlFilter`** and **`$Default`** when replaced. If **`AzureServiceBusFilterRules`** is omitted, a **legacy** correlation rule matches the **`cloudEvents:subject`** application property to **`RoutingKey`**.
- **Service Activator** receives messages; optional **inbox** deduplicates; **event handlers** run.

```mermaid
flowchart LR
  subgraph reg [Registration]
    A[appsettings BrighterMessaging]
    B[IEventTypeRegistry + producers/consumers]
  end
  subgraph pub [Publish path]
    M[IAmAMessageMapperAsync]
    W[WireEnvelopeBuilder]
    O[Outbox optional]
    P[ASB / Rabbit producer]
  end
  subgraph sub [Subscribe path]
    C[Brighter ASB subscription SqlFilter empty]
    R[AzureServiceBusCorrelationRulesHostedService]
    H[Handlers]
  end
  A --> B
  M --> W --> O --> P
  B --> C --> R --> H
```

#### Legacy: wrapped envelope (Lgs / Rabbit internal)

An earlier sample used **`PublishWrappedEnvelopeCommand`**, **`LgsEnvelopeBrighterEvent`**, and dedicated handlers. The same **outbox → broker → inbox** idea applies: outbox rows are cleared when the publisher successfully sends; inbox rows appear after the subscriber processes a message.

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

- **BrighterEventing.Messaging** – NuGet-style library (`net6.0` + `net8.0`): **`BrighterMessagingBrokerRegistration`** (empty ASB **`SqlFilter`** on subscription create), **`WireEnvelopeBuilder`**, **`BrighterPublisherPublicationMetadata`**, **`BrighterEventingServiceBusMessageConverter`** + **`BrighterEventingAzureServiceBusProducerSend`** (broker **`Subject`** + custom properties), **`AzureServiceBusCorrelationRulesHostedService`** + **`AzureServiceBusCorrelationRuleFilterBuilder`**, optional Postgres/Cosmos outbox and inbox. **`DepositPostAsync`** uses the **async** mapper pipeline only — sync **`IAmAMessageMapper<T>`** is not invoked for outbox deposit.
- **BrighterEventing.Sample.DomainEvents** – Shared **`OrderCreatedEvent`** / **`OrderUpdatedEvent`** / **`OrderCancelledEvent`** and **`EventTypeCatalogBuilder` / `IEventTypeRegistry`** extensions (**`AddSampleOrderEvents`**).
- **BrighterEventing.Publisher** / **BrighterEventing.Subscriber** – Primary path: domain-event handlers + **`IAmAMessageMapperAsync`** per event (**`OrderCreatedEventMessageMapper`**, etc.), **`BrighterMessaging:*`** config. Legacy path: **`PublishWrappedEnvelopeCommand`**, **`LgsEnvelopeBrighterEvent`** / **`RabbitInternalEnvelopeBrighterEvent`**, **`LgsEnvelopeBrighterEventMessageMapper`** / **`RabbitInternalEnvelopeBrighterEventMessageMapper`** (wire **`LgsWire` / `RabbitWire`** → **`LgsPublishedMessage` / `RabbitPublishedMessage`** with **`common`** metadata).
- **BrighterEventing.Publisher.Net6** / **BrighterEventing.Subscriber.Net6** – Same patterns on **.NET 6** hosts (direct **`PostAsync`**; no durable outbox/inbox unless you extend them).
- **BrighterEventing.Publisher.Net6.Cosmos** / **BrighterEventing.Subscriber.Net6.Cosmos** – **.NET 6** with **Cosmos DB** outbox/inbox via **`BrighterEventing.Messaging.CosmosDb`**; **`DepositPostAsync`** + **`ClearOutboxAsync`** on the publisher. Set **`BrighterMessaging:*:CosmosDb:Endpoint`** and **`Key`** (and Service Bus / Rabbit settings) in **`secrets.json`**.

### Common metadata (`common` property) — legacy wrapped envelope

The Implementation Guide’s recommended minimum fields are populated **once**, on the nested **`common`** object (**`CommonEventMetadata`**), alongside the unchanged Lgs / Rabbit body fields at the root. The Brighter message mappers map from wire inputs without repeating the same values at two levels. Applications set **`LgsWire` / `RabbitWire`** on the event; optional **`EnvelopeMapOptions`** fills correlation and message id for Rabbit-style publishes.

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

- **Transport**: Under **`BrighterMessaging:Publisher`** / **`BrighterMessaging:Subscriber`**, set **`Transport`** to **`RabbitMQ`** or **`AzureServiceBus`**. For Azure Service Bus, add **`BrighterMessaging:Publisher:AzureServiceBus:ConnectionString`** (and **`BrighterMessaging:Subscriber:AzureServiceBus:ConnectionString`** where applicable) in **secrets.json**. Topic names come from each **`Publications[]` / `Subscriptions[]`** row (**`Topic`**) — e.g. **`orders-events`** in the domain-event sample, not fixed **`order.created`** / **`greeting.made`** unless your appsettings still use those.
- **BrighterMessaging (Publisher)**: **`Publications`** — **`EventType`**, **`RoutingKey`**, optional **`Topic`**, optional **`ServiceBusEventType`** (maps to **`MessageHeader.Bag["serviceBusEventType"]`** for correlation filters). **`ImplementOutbox`**, **`DatabaseType`**, outbox table name, etc.
- **BrighterMessaging (Subscriber)**: **`Subscriptions`** — same identifiers plus **`SubscriptionName`**, optional **`AzureServiceBusFilterRules`** ( **`CorrelationRuleFilter`** semantics: OR across rules, AND within a rule). Correlation rules are applied at runtime by **`AzureServiceBusCorrelationRulesHostedService`** after the subscription exists.
- **Publisher (legacy keys)**: `Publisher:SendIntervalSeconds` (default 5), order routing key overrides where still used.
- **RabbitMQ**: `BrighterMessaging:*:RabbitMQ:AmqpUri` (secrets), **`Exchange`**, **`SubscriptionName`** (Subscriber) when using RabbitMQ.
- **Connection strings**: `ConnectionStrings:BrighterOutbox` (Publisher), `ConnectionStrings:BrighterInbox` (Subscriber) — set via `secrets.json` or env; not in appsettings for committed files.

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

- **Subscriber**: Uses these when creating subscriptions (RMQ and ASB). Domain-event handlers (e.g. **`OrderCreatedHandler`**) and legacy **`LgsEnvelopeHandler` / `RabbitInternalEnvelopeHandler`** use a Polly retry pipeline (**`ConsumerRetryPipeline`**) with exponential backoff driven by **`Messaging:Consumer`** (in-handler retries).
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
- **Azure Service Bus**: Single-message send paths use **`BrighterEventingServiceBusMessageConverter`** so broker **`Subject`** matches **`MessageHeader.Subject`**. **Bulk / batch** sends (**`CreateBatchesAsync`** → **`SendAsync(IAmAMessageBatch)`**) still delegate to Brighter’s inner producer and may not set **`ServiceBusMessage.Subject`** the same way; prefer single sends for correlation-on-Subject scenarios until batching is aligned.
- Inbox retention: implement a job to clear old inbox rows if needed.

## References

- [Brighter docs](https://brightercommand.gitbook.io/paramore-brighter-documentation/)
- HLD: `SE-Brighter High Level Design-100326-053043.pdf`
