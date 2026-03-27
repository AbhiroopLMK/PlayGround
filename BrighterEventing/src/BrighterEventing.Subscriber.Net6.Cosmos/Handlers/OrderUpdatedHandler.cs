using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Net6.Cosmos.Handlers;

public class OrderUpdatedHandler : RequestHandlerAsync<OrderUpdatedEvent>
{
    private readonly ILogger<OrderUpdatedHandler> _logger;

    public OrderUpdatedHandler(ILogger<OrderUpdatedHandler> logger)
    {
        _logger = logger;
    }

    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(OrderUpdatedHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<OrderUpdatedEvent> HandleAsync(
        OrderUpdatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "OrderUpdated: OrderId={OrderId}, Status={Status}",
            @event.OrderId, @event.Status);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
