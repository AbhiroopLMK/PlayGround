using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Wire;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Events;

/// <summary>
/// Brighter event for Lgs-shaped wrapped payloads. When publishing, set <see cref="LgsWire"/>; the
/// <see cref="BrighterEventing.Messaging.Mappers.LgsEnvelopeBrighterEventMessageMapper"/> builds the transport body. After consumption, <see cref="Envelope"/> is populated.
/// </summary>
public class LgsEnvelopeBrighterEvent : Event
{
    public LgsEnvelopeBrighterEvent() : base(Id.Random())
    {
    }

    public LgsEnvelopeBrighterEvent(Id id) : base(id)
    {
    }

    /// <summary>Wire input for publish; leave null when consuming (mapper fills <see cref="Envelope"/>).</summary>
    public LgsEventWire? LgsWire { get; set; }

    /// <summary>Optional correlation/causation overrides when mapping <see cref="LgsWire"/>.</summary>
    public EnvelopeMapOptions? EnvelopeOptions { get; set; }

    /// <summary>Resolved publish shape; set by <see cref="BrighterEventing.Messaging.Mappers.LgsEnvelopeBrighterEventMessageMapper"/> on consume.</summary>
    public LgsPublishedMessage? Envelope { get; set; }
}
