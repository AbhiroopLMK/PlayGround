# Brighter Eventing – Publisher & Subscriber

Sample publisher and subscriber applications demonstrating **Paramore.Brighter** event streaming with the drivers and requirements from the **SE-Brighter High Level Design** document. The solution uses **clean architecture** so you can switch between **Azure Service Bus** and **RabbitMQ** via configuration.

## HLD drivers and requirements covered

| HLD driver / requirement | Where it's shown |
|--------------------------|------------------|
| **1. Separation of concerns** | Commands and events in `BrighterEventing.Contracts`; handlers separate from transport. |
| **2. Encapsulation of cross-cutting concerns** | Handler pipeline (logging, retry) and optional inbox/outbox middleware. |
| **3. Reliability via Outbox and Inbox** | **Publisher**: Postgres outbox; **Subscriber**: Postgres inbox for de-duplication. |
| **4. Durable execution** | Outbox: messages persisted in Postgres before being sent. |
| **5. Transport flexibility** | Single `Transport` setting (`RabbitMQ` or `AzureServiceBus`). Swap broker via config. |

## Message flow

### End-to-end

```
[Publisher]                          [RabbitMQ]                    [Subscriber]
     |                                     |                              |
     | 1. Timer: SendAsync(PublishOrderCreatedCommand)                   |
     | 2. Handler: BeginTransaction                                       |
     | 3. DepositPostAsync(OrderCreatedEvent) → INSERT into Outbox         |
     | 4. Commit transaction                                               |
     | 5. ClearOutboxAsync(ids) → read Outbox, publish to broker -------->|  (exchange/queues)
     |    then set Outbox.Dispatched = now                                 |
     |                                     | 6. Message on queue            |
     |                                     |------------------------------>| 7. ServiceActivator receives
     |                                     |                              | 8. Inbox middleware: check duplicate
     |                                     |                              | 9. Handler runs (OrderCreatedHandler)
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
- **RabbitMQ**: In the CloudAMQP (or management) UI, confirm queues `order.created.queue` and `greeting.made.queue` exist and that message count increases when the Publisher runs.
- **Inbox**: Run the Subscriber; after it consumes messages you should see rows in `"Inbox"`. The Inbox table is created at startup using Brighter's `PostgreSqlInboxBuilder.GetDDL` in a single command; if you created the table manually with a different schema, drop it and restart the Subscriber so Brighter can recreate it.

## Solution layout

- **BrighterEventing.Contracts** – Shared events and commands (no transport dependency).
- **BrighterEventing.Publisher** – Sends commands; handlers deposit events into Postgres outbox and clear to RabbitMQ (or Azure when configured).
- **BrighterEventing.Subscriber** – Subscribes to the bus, uses Postgres inbox, handles `OrderCreatedEvent` and `GreetingMadeEvent`.

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

## Swapping transport

Change only the `Transport` key and the corresponding broker settings in config. Handlers and contracts stay unchanged.

## Limitations

- Outbox sweeper not wired in this sample (Brighter 10.0.x); enable where your version supports it.
- Azure Service Bus is supported: set `Transport` to `AzureServiceBus` and provide `AzureServiceBus:ConnectionString` in secrets.json. The Publisher creates topics `order.created` and `greeting.made`; the Subscriber creates subscriptions under those topics when using `MakeChannels: Create`.
- Inbox retention: implement a job to clear old inbox rows if needed.

## References

- [Brighter docs](https://brightercommand.gitbook.io/paramore-brighter-documentation/)
- HLD: `SE-Brighter High Level Design-100326-053043.pdf`
