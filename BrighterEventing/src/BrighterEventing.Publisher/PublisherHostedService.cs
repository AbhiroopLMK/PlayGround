using BrighterEventing.Publisher.Commands;
using BrighterEventing.Publisher.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher;

/// <summary>
/// Sends <see cref="PublishDomainEventCommand"/> on an interval, rotating OrderCreated / OrderUpdated / OrderCancelled.
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

        _logger.LogInformation("Publisher started. Transport={Transport}, Interval={Interval}s", transport, intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _sequence++;
                var id = $"ORD-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{_sequence}";
                var kind = (DomainEventKind)((_sequence - 1) % 3);

                var cmd = new PublishDomainEventCommand
                {
                    Kind = kind,
                    OrderId = id,
                    Amount = 100.5m + _sequence,
                    Status = "Shipped",
                    Reason = "Customer request",
                    PublishRoutingKey = kind switch
                    {
                        DomainEventKind.OrderCreated => _configuration["Publisher:OrderCreatedPublishRoutingKey"],
                        DomainEventKind.OrderUpdated => _configuration["Publisher:OrderUpdatedPublishRoutingKey"],
                        DomainEventKind.OrderCancelled => _configuration["Publisher:OrderCancelledPublishRoutingKey"],
                        _ => null
                    }
                };

                await _commandProcessor.SendAsync(cmd, cancellationToken: stoppingToken);

                _logger.LogInformation("Sent PublishDomainEventCommand Kind={Kind}", kind);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending publish domain event command");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
