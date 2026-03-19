using BrighterEventing.Contracts.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Handlers;

/// <summary>
/// Handles OrderCreatedEvent from the bus. Inbox de-duplication is configured at consumer level (HLD: Reliability via Inbox).
/// Duplicates are rejected (onceOnly: true); handler is skipped and message is acked. Uses ConsumerRetryPipeline for in-handler retries.
/// </summary>
public class OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) : RequestHandlerAsync<OrderCreatedEvent>
{
    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [Paramore.Brighter.Inbox.Attributes.UseInboxAsync(step: 0, contextKey: typeof(OrderCreatedHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
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
