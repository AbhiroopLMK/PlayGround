using BrighterEventing.Contracts.Events;
using BrighterEventing.Publisher.Commands;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Handlers;

/// <summary>
/// Handles PublishOrderCreatedCommand: writes event to outbox in same transaction (HLD: Reliability via Outbox).
/// Cross-cutting: RequestLogging, retry policy (Encapsulation of Cross-Cutting Concerns).
/// </summary>
public class PublishOrderCreatedHandler(
    IAmACommandProcessor commandProcessor,
    BrighterOutboxDbContext dbContext,
    ILogger<PublishOrderCreatedHandler> logger)
    : RequestHandlerAsync<PublishOrderCreatedCommand>
{
    public override async Task<PublishOrderCreatedCommand> HandleAsync(
        PublishOrderCreatedCommand command,
        CancellationToken cancellationToken = default)
    {
        var evt = new OrderCreatedEvent(command.Id)
        {
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            Amount = command.Amount,
            CreatedAtUtc = DateTime.UtcNow
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Deposit to outbox in same transaction (no other business data here; in real app you'd write to Orders table too)
            var messageId = await commandProcessor.DepositPostAsync(evt, cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // Clear outbox: send these messages to broker and set Outbox.Dispatched
            await commandProcessor.ClearOutboxAsync([messageId], null, null, false, cancellationToken);

            logger.LogInformation("Deposited and cleared OrderCreatedEvent {OrderId}, MessageId={MessageId}", command.OrderId, messageId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Failed to deposit OrderCreatedEvent {OrderId}", command.OrderId);
            throw;
        }

        return await base.HandleAsync(command, cancellationToken);
    }
}
