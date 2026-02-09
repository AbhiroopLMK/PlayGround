# Service Bus Geo-Replication POC

A simple console POC to test **Azure Service Bus Geo-Replication** using **topic and subscription** (publish-subscribe): a sender publishes to a topic and a receiver consumes from a subscription, with logging that shows which region/endpoint is in use, promotion (failover) effects, and any messages that are retried or dropped.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Azure Service Bus **Premium** namespace with **Geo-Replication** configured (primary + secondary pairing)
- Connection string to your geo-replicated namespace (the hostname points to the current primary; after promotion it points to the new primary)

## Configuration

1. **Connection string, topic, and subscription**

   In both projects, set your Service Bus connection string, topic name, and (for the receiver) subscription name:

   - **Sender:** `GeoReplication.Sender/appsettings.json`
   - **Receiver:** `GeoReplication.Receiver/appsettings.json`

   ```json
   {
     "ServiceBus": {
       "ConnectionString": "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=...",
       "TopicName": "geo-poc-topic",
       "SubscriptionName": "geo-poc-subscription",
       "RegionLabel": "Primary"
     }
   }
   ```
   The sender uses only `TopicName`; the receiver uses both `TopicName` and `SubscriptionName`.

2. **Region label (optional)**

   Use `RegionLabel` to tag logs with the role you’re running (e.g. `"Primary"` or `"Secondary"`). After you **promote** the replicated region, you can change this (e.g. to `"Secondary"` or the new primary’s name) so logs clearly show which config/region you’re on.

3. **Sender interval (optional)**

   In the Sender’s `appsettings.json` you can set:

   - `Sender:SendIntervalSeconds` (default: 5) – delay between sends.

## What the logs show

- **Region**  
  Every log line includes `[Region=...]` from your `RegionLabel` so you can see which config/region the app is using.

- **Endpoint (FullyQualifiedNamespace)**  
  At startup and when the app detects a change, it logs the Service Bus `FullyQualifiedNamespace` you’re connected to. With geo-replication, the **same namespace hostname** is used; after promotion it resolves to the new primary. Reconnections and retries (see below) indicate failover activity.

- **Promotion / failover**  
  - When the replicated region is promoted, the namespace hostname starts pointing to the new primary.  
  - The SDK may **reconnect** or **retry** during the switch. Those events are logged by the **Azure SDK diagnostic listener** (see below).  
  - If you run the app with a **different connection string or config** after promotion (e.g. pointing to the new primary or updating `RegionLabel`), the logs will show the updated region/endpoint.

- **Retries**  
  - **Sender:** Transient send failures (e.g. `ServiceBusy`, transient errors) are caught and logged with `[RETRY]`; the SDK may retry automatically.  
  - **Receiver:** Transient receive/processing errors are reported in the processor’s error handler.  
  - **SDK diagnostics:** With `EnableAzureSdkDiagnostics: true` (default), the Azure SDK’s own retries and connection events are written to the console (verbose), so you can see when the client retries or reconnects (e.g. during promotion).

- **Dropped / failed messages**  
  - **Sender:** Non-transient send failures are logged with `[ERROR]` and counted as send failures (message not sent or dropped).  
  - **Receiver:** Processing errors (e.g. abandon, dead-letter) are logged in `ProcessErrorAsync`; the app logs and counts abandoned and dead-letter events so you can see if messages were dropped or moved to the dead-letter queue.

## How to run

1. **Create the topic and subscription**  
   In your geo-replicated namespace, create a topic (e.g. `geo-poc-topic`) and at least one subscription (e.g. `geo-poc-subscription`) under that topic. You can create them in the Azure portal under **Entities > Topics** (then open the topic and add a subscription under **Subscriptions**).

2. **Run the receiver** (in one terminal):

   ```bash
   dotnet run --project GeoReplication.Receiver
   ```

3. **Run the sender** (in another terminal):

   ```bash
   dotnet run --project GeoReplication.Sender
   ```

4. **Observe logs**  
   - Sender: `[SEND]` for each message, `[RETRY]` on transient errors, `[ERROR]` on send failures.  
   - Receiver: `[RECV]` for each message, and error handler logs for abandoned/dead-letter/connection issues.  
   - With SDK diagnostics on, you’ll also see Azure SDK connection/retry events.

5. **Test promotion**  
   - In Azure, promote the replicated region (make the secondary the new primary).  
   - Watch for reconnection/retry activity in the SDK diagnostic output and for any `[RETRY]` or `[ERROR]` lines.  
   - Optionally update `RegionLabel` (or your connection/config) and restart to make it explicit in logs that you’re now on the promoted region.

## Solution layout

```
GeoReplication/
├── GeoReplication.sln
├── nuget.config              # Uses only nuget.org for restore
├── README.md
├── GeoReplication.Sender/    # Publishes messages to topic every N seconds
│   ├── Program.cs
│   └── appsettings.json
└── GeoReplication.Receiver/  # Receives from topic subscription; logs receives and errors
    ├── Program.cs
    └── appsettings.json
```

## Disabling Azure SDK diagnostic logging

In `appsettings.json` set:

- Sender: `"Sender": { "EnableAzureSdkDiagnostics": false }`
- Receiver: `"Receiver": { "EnableAzureSdkDiagnostics": false }`

This turns off the verbose SDK trace output; your app’s own `[RETRY]`, `[ERROR]`, and `[RECV]`/`[SEND]` logs are unchanged.
