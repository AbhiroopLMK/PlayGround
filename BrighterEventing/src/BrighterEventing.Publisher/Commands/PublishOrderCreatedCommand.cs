using Paramore.Brighter;

namespace BrighterEventing.Publisher.Commands;

/// <summary>
/// Internal command to trigger publishing an OrderCreatedEvent via the outbox.
/// Demonstrates separation: sender (timer) does not know about transport or outbox.
/// </summary>
public class PublishOrderCreatedCommand : Command
{
    public PublishOrderCreatedCommand() : base(Id.Random())
    {
    }

    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
