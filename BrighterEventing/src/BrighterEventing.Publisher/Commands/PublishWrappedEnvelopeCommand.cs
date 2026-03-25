using BrighterEventing.Messaging.Wire;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Commands;

/// <summary>
/// Single command: host fills either Lgs (Azure) or internal Rabbit payload; handler builds wrapped envelope and outbox.
/// </summary>
public class PublishWrappedEnvelopeCommand : Command
{
    public PublishWrappedEnvelopeCommand()
        : base(Id.Random())
    {
    }

    public PublishWrappedEnvelopeCommand(Id id)
        : base(id)
    {
    }

    /// <summary>When true, build <see cref="LgsInput"/>; when false, <see cref="RabbitInput"/>.</summary>
    public bool UseAzureLgsShape { get; set; }

    public LgsEventWire? LgsInput { get; set; }

    public RabbitInternalEventWire? RabbitInput { get; set; }
}
