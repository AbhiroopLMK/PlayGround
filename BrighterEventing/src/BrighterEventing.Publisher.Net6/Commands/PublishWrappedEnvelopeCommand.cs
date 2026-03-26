using BrighterEventing.Messaging.Wire;
using Paramore.Brighter;

namespace BrighterEventing.Publisher.Net6.Commands;

/// <summary>
/// Same command shape as net8 publisher; handler uses Cosmos outbox (no EF transaction).
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

    public bool UseAzureLgsShape { get; set; }

    public LgsEventWire? LgsInput { get; set; }

    public RabbitInternalEventWire? RabbitInput { get; set; }
}
