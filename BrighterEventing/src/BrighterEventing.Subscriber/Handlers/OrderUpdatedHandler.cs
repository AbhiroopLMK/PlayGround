using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Handlers;

public class OrderUpdatedHandler(ILogger<OrderUpdatedHandler> logger) : RequestHandlerAsync<OrderUpdatedEvent>
{
    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(OrderUpdatedHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<OrderUpdatedEvent> HandleAsync(
        OrderUpdatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "OrderUpdated: OrderId={OrderId}, Status={Status}",
            @event.OrderId, @event.Status);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
