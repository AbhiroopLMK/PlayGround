using BrighterEventing.Messaging.Events;
using Paramore.Brighter;

namespace BrighterEventing.Sample.DomainEvents;

public sealed class OrderCreatedEvent : DomainBrighterEvent
{
    public OrderCreatedEvent()
    {
    }

    public OrderCreatedEvent(Id id)
        : base(id)
    {
    }

    public string OrderId { get; set; } = "";

    public decimal Amount { get; set; }
}
