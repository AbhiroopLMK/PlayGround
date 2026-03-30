using BrighterEventing.Messaging.AzureServiceBus;
using BrighterEventing.Messaging.Mappers;
using Microsoft.Extensions.Configuration;
using Paramore.Brighter;

namespace BrighterEventing.Sample.DomainEvents.Mappers;

public sealed class OrderUpdatedEventMessageMapper : IAmAMessageMapperAsync<OrderUpdatedEvent>
{
    private readonly IConfiguration _configuration;

    public OrderUpdatedEventMessageMapper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IRequestContext? Context { get; set; }

    public Task<Message> MapToMessageAsync(
        OrderUpdatedEvent request,
        Publication publication,
        CancellationToken cancellationToken = default)
    {
        var message = WireEnvelopeBuilder.BuildForConfiguredTransport(
            request.Id,
            request.PublishRoutingKey,
            publication,
            _configuration,
            SampleOrderEventNames.OrderUpdated,
            "/orders/order.updated",
            request.OrderId,
            SampleOrderEventNames.OrderUpdated,
            "1.0",
            new { orderId = request.OrderId, status = request.Status });

        // Publisher decides which custom ASB application properties to stamp on this event.
        message.Header.Bag[ServiceBusApplicationPropertyKeys.ServiceBusEventType] = "order.updated.v1";

        return Task.FromResult(message);
    }

    public Task<OrderUpdatedEvent> MapToRequestAsync(Message message, CancellationToken cancellationToken = default)
    {
        var data = WireEnvelopeParser.ParsePayloadData(message);
        var lgsId = WireEnvelopeParser.TryGetLgsEnvelopeId(message);
        return Task.FromResult(new OrderUpdatedEvent(message.Header.MessageId)
        {
            OrderId = data["orderId"]?.ToString() ?? lgsId ?? "",
            Status = data["status"]?.ToString() ?? ""
        });
    }
}
