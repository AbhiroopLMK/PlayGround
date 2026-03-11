using BrighterEventing.Publisher.Commands;
using BrighterEventing.Publisher.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher;

/// <summary>
/// Periodically sends commands to trigger outbox publishing. Demonstrates Durable Execution (sweeper clears outbox).
/// </summary>
public class PublisherHostedService : BackgroundService
{
    private readonly IAmACommandProcessor _commandProcessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublisherHostedService> _logger;
    private int _orderSequence;
    private int _greetingSequence;

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
        var transport = _configuration["Transport"] ?? TransportType.RabbitMQ;
        _logger.LogInformation("Publisher started. Transport={Transport}, Interval={Interval}s", transport, intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _orderSequence++;
                _greetingSequence++;

                var orderCmd = new PublishOrderCreatedCommand
                {
                    OrderId = $"ORD-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{_orderSequence}",
                    CustomerId = $"CUST-{_orderSequence % 10}",
                    Amount = 100.50m + _orderSequence
                };
                await _commandProcessor.SendAsync(orderCmd, cancellationToken: stoppingToken);

                var greetingCmd = new PublishGreetingCommand
                {
                    PersonName = $"User-{_greetingSequence}",
                    Greeting = $"Hello from BrighterEventing at {DateTime.UtcNow:O}"
                };
                await _commandProcessor.SendAsync(greetingCmd, cancellationToken: stoppingToken);

                _logger.LogInformation("Sent PublishOrderCreated and PublishGreeting commands");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending commands");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
