using BrighterEventing.Messaging.Events;
using Paramore.Brighter;

namespace BrighterEventing.Sample.DomainEvents;

public sealed class OrderCancelledEvent : DomainBrighterEvent
{
    public OrderCancelledEvent()
    {
    }

    public OrderCancelledEvent(Id id)
        : base(id)
    {
    }

    public string OrderId { get; set; } = "";

    public string Reason { get; set; } = "";
}
