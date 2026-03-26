using Paramore.Brighter;

namespace BrighterEventing.Publisher.Commands;

public enum DomainEventKind
{
    OrderCreated = 0,
    OrderUpdated = 1,
    OrderCancelled = 2
}

/// <summary>
/// Demo command: host selects which domain event to publish; handler maps to Pattern A events and uses the PostgreSQL outbox in one transaction.
/// </summary>
public sealed class PublishDomainEventCommand : Command
{
    public PublishDomainEventCommand()
        : base(Id.Random())
    {
    }

    public PublishDomainEventCommand(Id id)
        : base(id)
    {
    }

    public DomainEventKind Kind { get; set; }

    public string OrderId { get; set; } = "";

    public decimal Amount { get; set; }

    public string Status { get; set; } = "";

    public string Reason { get; set; } = "";

    public string? PublishRoutingKey { get; set; }
}
