using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Handlers;

public class OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) : RequestHandlerAsync<OrderCreatedEvent>
{
    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(OrderCreatedHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<OrderCreatedEvent> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "OrderCreated: OrderId={OrderId}, Amount={Amount}",
            @event.OrderId, @event.Amount);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
