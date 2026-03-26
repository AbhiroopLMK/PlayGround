using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Events;
using BrighterEventing.Messaging.Wire;
using BrighterEventing.Publisher.Net6.Commands;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Net6.Handlers;

/// <summary>
/// Publishes via Brighter producers. Without a configured durable outbox (PostgreSQL/Cosmos),
/// Brighter still uses an <see cref="Paramore.Brighter.InMemoryOutbox"/> — <c>DepositPostAsync</c> + <c>ClearOutboxAsync</c>
/// (or a single <c>PostAsync</c>, which performs the same steps) is the correct publish path to the broker.
/// </summary>
public class PublishWrappedEnvelopeHandler : RequestHandlerAsync<PublishWrappedEnvelopeCommand>
{
    private readonly IAmACommandProcessor _commandProcessor;
    private readonly ILogger<PublishWrappedEnvelopeHandler> _logger;

    public PublishWrappedEnvelopeHandler(
        IAmACommandProcessor commandProcessor,
        ILogger<PublishWrappedEnvelopeHandler> logger)
    {
        _commandProcessor = commandProcessor;
        _logger = logger;
    }

    public override async Task<PublishWrappedEnvelopeCommand> HandleAsync(
        PublishWrappedEnvelopeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.UseAzureLgsShape && command.LgsInput != null)
        {
            var brighterEvent = new LgsEnvelopeBrighterEvent { LgsWire = command.LgsInput };
            // PostAsync == DepositPostAsync + ClearOutboxAsync (Brighter uses InMemoryOutbox when no durable outbox is registered).
            await _commandProcessor.PostAsync(brighterEvent, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Lgs wrapped envelope published. OrderKey={OrderKey}",
                command.LgsInput.Id);
        }
        else if (!command.UseAzureLgsShape && command.RabbitInput != null)
        {
            var brighterEvent = new RabbitInternalEnvelopeBrighterEvent
            {
                RabbitWire = command.RabbitInput,
                EnvelopeOptions = new EnvelopeMapOptions
                {
                    MessageId = command.RabbitInput.MessageId,
                    CorrelationId = command.RabbitInput.CorrelationId,
                    OccurredAtUtc = DateTime.UtcNow,
                    ContentType = command.RabbitInput.ContentType
                }
            };
            await _commandProcessor.PostAsync(brighterEvent, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Rabbit internal wrapped envelope published. MessageName={MessageName}",
                command.RabbitInput.MessageName);
        }
        else
        {
            throw new InvalidOperationException("PublishWrappedEnvelopeCommand requires LgsInput or RabbitInput matching UseAzureLgsShape.");
        }

        return await base.HandleAsync(command, cancellationToken);
    }
}
