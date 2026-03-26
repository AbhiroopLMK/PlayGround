using BrighterEventing.Messaging.Mappers;
using Microsoft.Extensions.Configuration;
using Paramore.Brighter;

namespace BrighterEventing.Sample.DomainEvents.Mappers;

public sealed class OrderCreatedEventMessageMapper : IAmAMessageMapperAsync<OrderCreatedEvent>
{
    private readonly IConfiguration _configuration;

    public OrderCreatedEventMessageMapper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IRequestContext? Context { get; set; }

    public Task<Message> MapToMessageAsync(
        OrderCreatedEvent request,
        Publication publication,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WireEnvelopeBuilder.BuildForConfiguredTransport(
            request.Id,
            request.PublishRoutingKey,
            publication,
            _configuration,
            SampleOrderEventNames.OrderCreated,
            "/orders/order.created",
            request.OrderId,
            SampleOrderEventNames.OrderCreated,
            "1.0",
            new { orderId = request.OrderId, amount = request.Amount }));

    public Task<OrderCreatedEvent> MapToRequestAsync(Message message, CancellationToken cancellationToken = default)
    {
        var data = WireEnvelopeParser.ParsePayloadData(message);
        var lgsId = WireEnvelopeParser.TryGetLgsEnvelopeId(message);
        return Task.FromResult(new OrderCreatedEvent(message.Header.MessageId)
        {
            OrderId = data["orderId"]?.ToString() ?? lgsId ?? "",
            Amount = data["amount"]?.ToObject<decimal>() ?? 0m
        });
    }
}
