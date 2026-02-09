using Azure;
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
var regionLabel = config["ServiceBus:RegionLabel"] ?? "Unknown";
var sendIntervalSeconds = config.GetValue("Sender:SendIntervalSeconds", 5);
var enableDiagnostics = config.GetValue("Sender:EnableAzureSdkDiagnostics", true);

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

Log("INFO", "Sender starting.", extra: $"Topic={topicName}, SendInterval={sendIntervalSeconds}s");
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
ServiceBusSender? sender = null;
string? lastLoggedFqdn = null;
var messageCount = 0;
var sendFailures = 0;
var retryCount = 0;

try
{
    client = new ServiceBusClient(connectionString, clientOptions);
    sender = client.CreateSender(topicName);

    var fqdn = client.FullyQualifiedNamespace;
    lastLoggedFqdn = fqdn;
    Log("INFO", "Connected to Service Bus.", extra: $"FullyQualifiedNamespace={fqdn}");
    Log("INFO", "After geo-replication promotion, reconnect or restart this app; logs will show the new endpoint when connection is re-established.");
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            messageCount++;
            var body = Encoding.UTF8.GetBytes($"POC Message #{messageCount} at {DateTime.UtcNow:O} from {regionLabel}");
            var msg = new ServiceBusMessage(body)
            {
                MessageId = Guid.NewGuid().ToString(),
                ApplicationProperties = { ["RegionLabel"] = regionLabel, ["Sequence"] = messageCount }
            };

            await sender.SendMessageAsync(msg, cts.Token);

            // Log endpoint periodically so promotion is visible (FQDN may resolve to new primary after promotion)
            if (client.FullyQualifiedNamespace != lastLoggedFqdn)
            {
                lastLoggedFqdn = client.FullyQualifiedNamespace;
                Log("WARN", "Endpoint changed (possible geo-replication promotion).", extra: $"FullyQualifiedNamespace={lastLoggedFqdn}");
            }

            Log("SEND", $"Message #{messageCount} sent.", extra: $"MessageId={msg.MessageId}");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceBusy || ex.IsTransient)
        {
            retryCount++;
            Log("RETRY", $"Transient error; SDK or application may retry. Reason={ex.Reason}, Message={ex.Message}");
            // Don't increment sendFailures; let SDK retry
        }
        catch (ServiceBusException ex)
        {
            sendFailures++;
            Log("ERROR", $"Send failed (non-transient). Message dropped or not sent.", extra: $"Reason={ex.Reason}, Message={ex.Message}");
        }
        catch (RequestFailedException ex)
        {
            sendFailures++;
            Log("ERROR", $"Request failed. Message dropped or not sent.", extra: $"Status={ex.Status}, Message={ex.Message}");
        }
        catch (Exception ex)
        {
            sendFailures++;
            Log("ERROR", $"Unexpected error. Message dropped or not sent.", extra: ex.Message);
        }

        await Task.Delay(TimeSpan.FromSeconds(sendIntervalSeconds), cts.Token);
    }
}
catch (Exception ex)
{
    Log("FATAL", "Sender failed.", extra: ex.ToString());
    return 1;
}
finally
{
    await (sender?.DisposeAsync() ?? ValueTask.CompletedTask);
    await (client?.DisposeAsync() ?? ValueTask.CompletedTask);
    Log("INFO", "Sender stopped.", extra: $"TotalSent={messageCount}, SendFailures={sendFailures}, RetriesLogged={retryCount}");
}

return 0;
