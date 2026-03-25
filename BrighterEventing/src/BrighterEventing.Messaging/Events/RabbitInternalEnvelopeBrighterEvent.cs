using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Wire;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Events;

/// <summary>
/// Brighter event for Rabbit internal-style wrapped payloads. When publishing, set <see cref="RabbitWire"/>; the
/// <see cref="BrighterEventing.Messaging.Mappers.RabbitInternalEnvelopeBrighterEventMessageMapper"/> builds the transport body. After consumption, <see cref="Envelope"/> is populated.
/// </summary>
public class RabbitInternalEnvelopeBrighterEvent : Event
{
    public RabbitInternalEnvelopeBrighterEvent() : base(Id.Random())
    {
    }

    public RabbitInternalEnvelopeBrighterEvent(Id id) : base(id)
    {
    }

    /// <summary>Wire input for publish; leave null when consuming.</summary>
    public RabbitInternalEventWire? RabbitWire { get; set; }

    /// <summary>Optional extras when mapping (e.g. <see cref="EnvelopeMapOptions.OccurredAtUtc"/>); message/correlation often come from <see cref="RabbitWire"/>.</summary>
    public EnvelopeMapOptions? EnvelopeOptions { get; set; }

    /// <summary>Resolved publish shape; set by <see cref="BrighterEventing.Messaging.Mappers.RabbitInternalEnvelopeBrighterEventMessageMapper"/> on consume.</summary>
    public RabbitPublishedMessage? Envelope { get; set; }
}
