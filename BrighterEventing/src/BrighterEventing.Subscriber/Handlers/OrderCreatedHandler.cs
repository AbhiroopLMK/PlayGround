using BrighterEventing.Contracts.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Subscriber.Handlers;

/// <summary>
/// Handles OrderCreatedEvent from the bus. Inbox de-duplication is configured at consumer level (HLD: Reliability via Inbox).
/// </summary>
public class OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) : RequestHandlerAsync<OrderCreatedEvent>
{
    [Paramore.Brighter.Inbox.Attributes.UseInboxAsync(step: 0, contextKey: typeof(OrderCreatedHandler), onceOnly: false)]
    public override async Task<OrderCreatedEvent> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Received OrderCreated: OrderId={OrderId}, CustomerId={CustomerId}, Amount={Amount}",
            @event.OrderId, @event.CustomerId, @event.Amount);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
