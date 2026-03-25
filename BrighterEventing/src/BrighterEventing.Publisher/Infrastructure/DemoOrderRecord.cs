namespace BrighterEventing.Publisher.Infrastructure;

/// <summary>
/// Minimal domain row written in the same DB transaction as the outbox (HLD: outbox + domain same TX).
/// </summary>
public class DemoOrderRecord
{
    public Guid Id { get; set; }

    public string OrderKey { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
