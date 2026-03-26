using Paramore.Brighter;

namespace BrighterEventing.Messaging.Events;

/// <summary>Base for Pattern A domain events (one Brighter <see cref="Event"/> type per business event).</summary>
public abstract class DomainBrighterEvent : Event
{
    protected DomainBrighterEvent()
        : base(Id.Random())
    {
    }

    protected DomainBrighterEvent(Id id)
        : base(id)
    {
    }

    /// <summary>When set, overrides the default publication routing key for this send.</summary>
    public string? PublishRoutingKey { get; set; }
}
