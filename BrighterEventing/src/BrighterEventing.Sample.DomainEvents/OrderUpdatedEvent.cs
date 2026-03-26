using BrighterEventing.Messaging.Events;
using Paramore.Brighter;

namespace BrighterEventing.Sample.DomainEvents;

public sealed class OrderUpdatedEvent : DomainBrighterEvent
{
    public OrderUpdatedEvent()
    {
    }

    public OrderUpdatedEvent(Id id)
        : base(id)
    {
    }

    public string OrderId { get; set; } = "";

    public string Status { get; set; } = "";
}
