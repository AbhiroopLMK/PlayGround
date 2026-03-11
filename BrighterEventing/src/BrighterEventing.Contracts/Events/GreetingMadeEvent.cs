using Paramore.Brighter;

namespace BrighterEventing.Contracts.Events;

/// <summary>
/// Simple event for greeting flow. Used to demonstrate outbox/inbox and transport flexibility.
/// </summary>
public class GreetingMadeEvent : Event
{
    public GreetingMadeEvent() : base(Id.Random())
    {
    }

    public GreetingMadeEvent(Id id) : base(id)
    {
    }

    public string Greeting { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
