using BrighterEventing.Publisher.Net6.Commands;
using BrighterEventing.Publisher.Net6.Configuration;
using BrighterEventing.Messaging.Wire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Net6;

/// <summary>
/// Sends <see cref="PublishWrappedEnvelopeCommand"/> on an interval (same behavior as net8 sample host).
/// </summary>
public class PublisherHostedService : BackgroundService
{
    private readonly IAmACommandProcessor _commandProcessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublisherHostedService> _logger;
    private int _sequence;

    public PublisherHostedService(
        IAmACommandProcessor commandProcessor,
        IConfiguration configuration,
        ILogger<PublisherHostedService> logger)
    {
        _commandProcessor = commandProcessor;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _configuration.GetValue("Publisher:SendIntervalSeconds", 5);
        var transport = _configuration["BrighterMessaging:Publisher:Transport"]
            ?? _configuration["Transport"]
            ?? TransportType.RabbitMQ;
        var useAzure = transport == TransportType.AzureServiceBus;

        _logger.LogInformation("Net6 publisher started. Transport={Transport}, Interval={Interval}s", transport, intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _sequence++;
                var id = $"ORD-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{_sequence}";

                var cmd = new PublishWrappedEnvelopeCommand { UseAzureLgsShape = useAzure };

                if (useAzure)
                {
                    cmd.LgsInput = new LgsEventWire
                    {
                        SpecVersion = "1.0",
                        Type = "order.accepted",
                        Source = "/orders/order.accepted",
                        Id = Guid.NewGuid().ToString("N"),
                        SessionId = id,
                        Time = DateTime.UtcNow,
                        DataContentType = "application/json",
                        Data = new LgsEventDataWire
                        {
                            DataType = "application/json",
                            EventData = JObject.FromObject(new { orderId = id, amount = 100.5m + _sequence })
                        }
                    };
                }
                else
                {
                    cmd.RabbitInput = new RabbitInternalEventWire
                    {
                        MessageName = "transaction.request.updated",
                        Version = "1.0",
                        Payload = new { transactionId = id, status = "Submitted" },
                        MessageId = Guid.NewGuid().ToString("N"),
                        CorrelationId = Guid.NewGuid().ToString("N"),
                        ContentType = "application/json"
                    };
                }

                await _commandProcessor.SendAsync(cmd, cancellationToken: stoppingToken);
                _logger.LogInformation("Sent PublishWrappedEnvelopeCommand (AzureShape={Azure})", useAzure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending wrapped envelope command");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
