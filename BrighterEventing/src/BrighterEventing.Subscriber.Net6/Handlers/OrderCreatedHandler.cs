using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Net6.Handlers;

public class OrderCreatedHandler : RequestHandlerAsync<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger)
    {
        _logger = logger;
    }

    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(OrderCreatedHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<OrderCreatedEvent> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "OrderCreated: OrderId={OrderId}, Amount={Amount}",
            @event.OrderId, @event.Amount);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
