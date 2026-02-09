using Azure.Core.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;

// Load configuration
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

var connectionString = config["ServiceBus:ConnectionString"] ?? "";
var topicName = config["ServiceBus:TopicName"] ?? "geo-poc-topic";
var subscriptionName = config["ServiceBus:SubscriptionName"] ?? "geo-poc-subscription";
var regionLabel = config["ServiceBus:RegionLabel"] ?? "Unknown";
var enableDiagnostics = config.GetValue("Receiver:EnableAzureSdkDiagnostics", true);
var maxConcurrentCalls = config.GetValue("Receiver:MaxConcurrentCalls", 5);

if (string.IsNullOrWhiteSpace(connectionString) || connectionString.StartsWith("<"))
{
    Console.WriteLine("[ERROR] Configure ServiceBus:ConnectionString in appsettings.json");
    return 1;
}

void Log(string level, string message, string? region = null, string? extra = null)
{
    var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
    var r = region ?? regionLabel;
    var line = $"[{ts}] [{level}] [Region={r}] {message}";
    if (!string.IsNullOrEmpty(extra)) line += " | " + extra;
    Console.WriteLine(line);
}

// Enable Azure SDK diagnostic events (retries, connection, errors)
if (enableDiagnostics)
{
    using var listener = AzureEventSourceListener.CreateTraceLogger(EventLevel.Verbose);
    Log("INFO", "Azure SDK diagnostic logging enabled (Verbose). Retries and connection events will appear here.");
}

Log("INFO", "Receiver starting.", extra: $"Topic={topicName}, Subscription={subscriptionName}, MaxConcurrentCalls={maxConcurrentCalls}");
Log("INFO", "Connecting to Service Bus...");

var clientOptions = new ServiceBusClientOptions
{
    RetryOptions = new ServiceBusRetryOptions
    {
        Mode = ServiceBusRetryMode.Exponential,
        MaxRetries = 5,
        Delay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        TryTimeout = TimeSpan.FromSeconds(60)
    }
};

ServiceBusClient? client = null;
ServiceBusProcessor? processor = null;
string? lastLoggedFqdn = null;
var receivedCount = 0;
var abandonedCount = 0;
var deadLetterCount = 0;
var errorCount = 0;

try
{
    client = new ServiceBusClient(connectionString, clientOptions);
    processor = client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions
    {
        MaxConcurrentCalls = maxConcurrentCalls,
        AutoCompleteMessages = false,
        MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
    });

    var fqdn = client.FullyQualifiedNamespace;
    lastLoggedFqdn = fqdn;
    Log("INFO", "Connected to Service Bus.", extra: $"FullyQualifiedNamespace={fqdn}");
    Log("INFO", "After geo-replication promotion, connection may reconnect; logs will show new endpoint and any retries.");
    Console.WriteLine();

    processor.ProcessMessageAsync += async args =>
    {
        var msg = args.Message;
        try
        {
            Interlocked.Increment(ref receivedCount);
            var body = msg.Body?.ToString() ?? Encoding.UTF8.GetString(msg.Body?.ToArray() ?? []);
            var seq = msg.ApplicationProperties.TryGetValue("Sequence", out var s) ? s?.ToString() : "-";
            var senderRegion = msg.ApplicationProperties.TryGetValue("RegionLabel", out var r) ? r?.ToString() : "-";

            if (client!.FullyQualifiedNamespace != lastLoggedFqdn)
            {
                lastLoggedFqdn = client.FullyQualifiedNamespace;
                Log("WARN", "Endpoint changed (possible geo-replication promotion).", extra: $"FullyQualifiedNamespace={lastLoggedFqdn}");
            }

            Log("RECV", $"Message received.", extra: $"MessageId={msg.MessageId}, Sequence={seq}, SenderRegion={senderRegion}, BodyLength={body.Length}");

            await args.CompleteMessageAsync(msg);
        }
        catch (Exception ex)
        {
            Log("ERROR", "Processing failed; message will be abandoned or dead-lettered after max delivery.", extra: $"MessageId={msg.MessageId}, Error={ex.Message}");
            await args.AbandonMessageAsync(msg);
            Interlocked.Increment(ref abandonedCount);
        }
    };

    processor.ProcessErrorAsync += async args =>
    {
        Interlocked.Increment(ref errorCount);
        var ex = args.Exception;
        var source = args.ErrorSource.ToString();

        if (source.Contains("Abandon") || ex.Message.Contains("abandoned"))
            Interlocked.Increment(ref abandonedCount);
        if (source.Contains("DeadLetter") || ex.Message.Contains("dead"))
            Interlocked.Increment(ref deadLetterCount);

        Log("ERROR", "Processor error (dropped/retry/connection).", extra: $"ErrorSource={source}, Message={ex.Message}");
        if (ex.InnerException != null)
            Log("ERROR", "Inner exception.", extra: ex.InnerException.Message);

        await Task.CompletedTask;
    };

    await processor.StartProcessingAsync();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    try
    {
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    catch (OperationCanceledException) { }
}
catch (Exception ex)
{
    Log("FATAL", "Receiver failed.", extra: ex.ToString());
    return 1;
}
finally
{
    if (processor != null)
    {
        await processor.StopProcessingAsync();
        await processor.DisposeAsync();
    }
    await (client?.DisposeAsync() ?? ValueTask.CompletedTask);
    Log("INFO", "Receiver stopped.", extra: $"Received={receivedCount}, Abandoned={abandonedCount}, DeadLetter={deadLetterCount}, ErrorEvents={errorCount}");
}

return 0;
