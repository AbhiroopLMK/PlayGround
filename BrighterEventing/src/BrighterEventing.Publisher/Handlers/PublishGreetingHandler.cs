using BrighterEventing.Contracts.Events;
using BrighterEventing.Publisher.Commands;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Handlers;

/// <summary>
/// Handles PublishGreetingCommand: deposits GreetingMadeEvent to outbox (HLD: Outbox pattern).
/// </summary>
public class PublishGreetingHandler(
    IAmACommandProcessor commandProcessor,
    BrighterOutboxDbContext dbContext,
    ILogger<PublishGreetingHandler> logger)
    : RequestHandlerAsync<PublishGreetingCommand>
{
    public override async Task<PublishGreetingCommand> HandleAsync(
        PublishGreetingCommand command,
        CancellationToken cancellationToken = default)
    {
        var evt = new GreetingMadeEvent(command.Id)
        {
            Greeting = command.Greeting,
            PersonName = command.PersonName,
            OccurredAtUtc = DateTime.UtcNow
        };

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var messageId = await commandProcessor.DepositPostAsync(evt, cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            await commandProcessor.ClearOutboxAsync([messageId], null, null, false, cancellationToken);

            logger.LogInformation("Deposited and cleared GreetingMadeEvent for {PersonName}, MessageId={MessageId}", command.PersonName, messageId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Failed to deposit GreetingMadeEvent");
            throw;
        }

        return await base.HandleAsync(command, cancellationToken);
    }
}
