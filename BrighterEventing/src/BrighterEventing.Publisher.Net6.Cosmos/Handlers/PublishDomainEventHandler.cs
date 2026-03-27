using BrighterEventing.Publisher.Net6.Cosmos.Commands;
using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Net6.Cosmos.Handlers;

/// <summary>
/// Deposits domain events into the Cosmos outbox, then dispatches via <see cref="IAmACommandProcessor.ClearOutboxAsync"/>.
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
        Id messageId;
        switch (command.Kind)
        {
            case DomainEventKind.OrderCreated:
                messageId = await _commandProcessor.DepositPostAsync(
                    new OrderCreatedEvent
                    {
                        OrderId = command.OrderId,
                        Amount = command.Amount,
                        PublishRoutingKey = command.PublishRoutingKey
                    },
                    cancellationToken: cancellationToken);
                break;
            case DomainEventKind.OrderUpdated:
                messageId = await _commandProcessor.DepositPostAsync(
                    new OrderUpdatedEvent
                    {
                        OrderId = command.OrderId,
                        Status = command.Status,
                        PublishRoutingKey = command.PublishRoutingKey
                    },
                    cancellationToken: cancellationToken);
                break;
            case DomainEventKind.OrderCancelled:
                messageId = await _commandProcessor.DepositPostAsync(
                    new OrderCancelledEvent
                    {
                        OrderId = command.OrderId,
                        Reason = command.Reason,
                        PublishRoutingKey = command.PublishRoutingKey
                    },
                    cancellationToken: cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(DomainEventKind)} {command.Kind}.");
        }

        await _commandProcessor.ClearOutboxAsync(new[] { messageId }, null, null, false, cancellationToken);

        _logger.LogInformation(
            "Cosmos outbox cleared. Kind={Kind}, OrderId={OrderId}, MessageId={MessageId}",
            command.Kind,
            command.OrderId,
            messageId);

        return await base.HandleAsync(command, cancellationToken);
    }
}
