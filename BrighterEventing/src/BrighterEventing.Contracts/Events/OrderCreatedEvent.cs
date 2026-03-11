using Paramore.Brighter;

namespace BrighterEventing.Contracts.Events;

/// <summary>
/// Event raised when an order is created. Demonstrates Brighter Event (broadcast) and
/// HLD driver: Separation of Concerns — events as notifications.
/// </summary>
public class OrderCreatedEvent : Event
{
    public OrderCreatedEvent() : base(Id.Random())
    {
    }

    public OrderCreatedEvent(Id id) : base(id)
    {
    }

    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
