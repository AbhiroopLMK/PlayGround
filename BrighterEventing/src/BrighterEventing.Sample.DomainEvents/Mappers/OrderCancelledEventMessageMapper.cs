using BrighterEventing.Messaging.AzureServiceBus;
using BrighterEventing.Messaging.Mappers;
using Microsoft.Extensions.Configuration;
using Paramore.Brighter;

namespace BrighterEventing.Sample.DomainEvents.Mappers;

public sealed class OrderCancelledEventMessageMapper : IAmAMessageMapperAsync<OrderCancelledEvent>
{
    private readonly IConfiguration _configuration;

    public OrderCancelledEventMessageMapper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IRequestContext? Context { get; set; }

    public Task<Message> MapToMessageAsync(
        OrderCancelledEvent request,
        Publication publication,
        CancellationToken cancellationToken = default)
    {
        var message = WireEnvelopeBuilder.BuildForConfiguredTransport(
            request.Id,
            request.PublishRoutingKey,
            publication,
            _configuration,
            SampleOrderEventNames.OrderCancelled,
            "/orders/order.cancelled",
            request.OrderId,
            SampleOrderEventNames.OrderCancelled,
            "1.0",
            new { orderId = request.OrderId, reason = request.Reason });

        // Publisher decides which custom ASB application properties to stamp on this event.
        message.Header.Bag[ServiceBusApplicationPropertyKeys.ServiceBusEventType] = "order.cancelled";

        return Task.FromResult(message);
    }

    public Task<OrderCancelledEvent> MapToRequestAsync(Message message, CancellationToken cancellationToken = default)
    {
        var data = WireEnvelopeParser.ParsePayloadData(message);
        var lgsId = WireEnvelopeParser.TryGetLgsEnvelopeId(message);
        return Task.FromResult(new OrderCancelledEvent(message.Header.MessageId)
        {
            OrderId = data["orderId"]?.ToString() ?? lgsId ?? "",
            Reason = data["reason"]?.ToString() ?? ""
        });
    }
}
