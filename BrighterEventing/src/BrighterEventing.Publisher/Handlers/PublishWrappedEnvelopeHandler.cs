using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Events;
using BrighterEventing.Messaging.Wire;
using BrighterEventing.Publisher.Commands;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Handlers;

/// <summary>
/// Publishes Lgs or Rabbit wire via <see cref="LgsEnvelopeBrighterEvent"/> / <see cref="RabbitInternalEnvelopeBrighterEvent"/>; Brighter message mappers build the transport body from wire. Writes a domain row + outbox in one transaction.
/// </summary>
public class PublishWrappedEnvelopeHandler(
    IAmACommandProcessor commandProcessor,
    BrighterOutboxDbContext dbContext,
    ILogger<PublishWrappedEnvelopeHandler> logger)
    : RequestHandlerAsync<PublishWrappedEnvelopeCommand>
{
    public override async Task<PublishWrappedEnvelopeCommand> HandleAsync(
        PublishWrappedEnvelopeCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var transactionCommitted = false;
        try
        {
            var domainId = Guid.NewGuid();
            if (command.UseAzureLgsShape && command.LgsInput != null)
            {
                var brighterEvent = new LgsEnvelopeBrighterEvent
                {
                    LgsWire = command.LgsInput
                };

                dbContext.DemoOrders.Add(new DemoOrderRecord
                {
                    Id = domainId,
                    OrderKey = command.LgsInput.Id,
                    CreatedUtc = DateTime.UtcNow
                });

                var messageId = await commandProcessor.DepositPostAsync(brighterEvent, cancellationToken: cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                transactionCommitted = true;

                await commandProcessor.ClearOutboxAsync([messageId], null, null, false, cancellationToken);

                logger.LogInformation(
                    "Lgs wrapped envelope published. DomainId={DomainId}, OrderKey={OrderKey}, MessageId={MessageId}",
                    domainId, command.LgsInput.Id, messageId);
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

                dbContext.DemoOrders.Add(new DemoOrderRecord
                {
                    Id = domainId,
                    OrderKey = command.RabbitInput.MessageName,
                    CreatedUtc = DateTime.UtcNow
                });

                var messageId = await commandProcessor.DepositPostAsync(brighterEvent, cancellationToken: cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                transactionCommitted = true;

                await commandProcessor.ClearOutboxAsync([messageId], null, null, false, cancellationToken);

                logger.LogInformation(
                    "Rabbit internal wrapped envelope published. DomainId={DomainId}, MessageName={MessageName}, MessageId={MessageId}",
                    domainId, command.RabbitInput.MessageName, messageId);
            }
            else
            {
                throw new InvalidOperationException("PublishWrappedEnvelopeCommand requires LgsInput or RabbitInput matching UseAzureLgsShape.");
            }
        }
        catch (Exception ex)
        {
            if (!transactionCommitted)
            {
                await tx.RollbackAsync(cancellationToken);
                logger.LogError(ex, "Failed to publish wrapped envelope (transaction rolled back).");
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
