using BrighterEventing.Publisher.Net6.Commands;
using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Net6.Handlers;

/// <summary>
/// Publishes Pattern A domain events via Brighter producers (in-memory outbox when no durable outbox is registered).
/// </summary>
public sealed class PublishDomainEventHandler : RequestHandlerAsync<PublishDomainEventCommand>
{
    private readonly IAmACommandProcessor _commandProcessor;
    private readonly ILogger<PublishDomainEventHandler> _logger;

    public PublishDomainEventHandler(
        IAmACommandProcessor commandProcessor,
        ILogger<PublishDomainEventHandler> logger)
    {
        _commandProcessor = commandProcessor;
        _logger = logger;
    }

    public override async Task<PublishDomainEventCommand> HandleAsync(
        PublishDomainEventCommand command,
        CancellationToken cancellationToken = default)
    {
        switch (command.Kind)
        {
            case DomainEventKind.OrderCreated:
                await _commandProcessor.PostAsync(
                    new OrderCreatedEvent
                    {
                        OrderId = command.OrderId,
                        Amount = command.Amount,
                        PublishRoutingKey = command.PublishRoutingKey
                    },
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Published OrderCreated OrderId={OrderId} Amount={Amount}", command.OrderId, command.Amount);
                break;
            case DomainEventKind.OrderUpdated:
                await _commandProcessor.PostAsync(
                    new OrderUpdatedEvent
                    {
                        OrderId = command.OrderId,
                        Status = command.Status,
                        PublishRoutingKey = command.PublishRoutingKey
                    },
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Published OrderUpdated OrderId={OrderId} Status={Status}", command.OrderId, command.Status);
                break;
            case DomainEventKind.OrderCancelled:
                await _commandProcessor.PostAsync(
                    new OrderCancelledEvent
                    {
                        OrderId = command.OrderId,
                        Reason = command.Reason,
                        PublishRoutingKey = command.PublishRoutingKey
                    },
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Published OrderCancelled OrderId={OrderId} Reason={Reason}", command.OrderId, command.Reason);
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(DomainEventKind)} {command.Kind}.");
        }

        return await base.HandleAsync(command, cancellationToken);
    }
}
