using BrighterEventing.Publisher.Commands;
using BrighterEventing.Sample.DomainEvents;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Handlers;

/// <summary>
/// Publishes Pattern A domain events via Brighter producers; writes a domain row + outbox in one transaction.
/// </summary>
public class PublishDomainEventHandler(
    IAmACommandProcessor commandProcessor,
    BrighterOutboxDbContext dbContext,
    ILogger<PublishDomainEventHandler> logger)
    : RequestHandlerAsync<PublishDomainEventCommand>
{
    public override async Task<PublishDomainEventCommand> HandleAsync(
        PublishDomainEventCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var transactionCommitted = false;
        try
        {
            var domainId = Guid.NewGuid();
            dbContext.DemoOrders.Add(new DemoOrderRecord
            {
                Id = domainId,
                OrderKey = command.OrderId,
                CreatedUtc = DateTime.UtcNow
            });

            Id messageId;
            switch (command.Kind)
            {
                case DomainEventKind.OrderCreated:
                    messageId = await commandProcessor.DepositPostAsync(
                        new OrderCreatedEvent
                        {
                            OrderId = command.OrderId,
                            Amount = command.Amount,
                            PublishRoutingKey = command.PublishRoutingKey
                        },
                        cancellationToken: cancellationToken);
                    break;
                case DomainEventKind.OrderUpdated:
                    messageId = await commandProcessor.DepositPostAsync(
                        new OrderUpdatedEvent
                        {
                            OrderId = command.OrderId,
                            Status = command.Status,
                            PublishRoutingKey = command.PublishRoutingKey
                        },
                        cancellationToken: cancellationToken);
                    break;
                case DomainEventKind.OrderCancelled:
                    messageId = await commandProcessor.DepositPostAsync(
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

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            transactionCommitted = true;

            await commandProcessor.ClearOutboxAsync([messageId], null, null, false, cancellationToken);

            logger.LogInformation(
                "Domain event published. Kind={Kind}, DomainId={DomainId}, OrderId={OrderId}, MessageId={MessageId}",
                command.Kind, domainId, command.OrderId, messageId);
        }
        catch (Exception ex)
        {
            if (!transactionCommitted)
            {
                await tx.RollbackAsync(cancellationToken);
                logger.LogError(ex, "Failed to publish domain event (transaction rolled back).");
            }
            else
            {
                logger.LogError(ex,
                    "Failed after DB commit (e.g. ClearOutboxAsync). Domain + outbox rows are committed; message may retry on next sweep.");
            }

            throw;
        }

        return await base.HandleAsync(command, cancellationToken);
    }
}
