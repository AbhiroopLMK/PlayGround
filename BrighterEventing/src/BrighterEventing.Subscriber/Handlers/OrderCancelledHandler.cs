using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Handlers;

public class OrderCancelledHandler(ILogger<OrderCancelledHandler> logger) : RequestHandlerAsync<OrderCancelledEvent>
{
    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(OrderCancelledHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<OrderCancelledEvent> HandleAsync(
        OrderCancelledEvent @event,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "OrderCancelled: OrderId={OrderId}, Reason={Reason}",
            @event.OrderId, @event.Reason);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
