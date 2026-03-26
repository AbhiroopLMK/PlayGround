namespace BrighterEventing.Messaging.Envelope;

/// <summary>
/// Recommended minimum metadata (Implementation Guide) — duplicated from transport-specific fields for forward compatibility.
/// </summary>
public class EventMetadata
{
    public string? MessageId { get; set; }

    public string? CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public string? MessageType { get; set; }

    public string? SchemaVersion { get; set; }

    public DateTime? OccurredAtUtc { get; set; }

    /// <summary>Topic (ASB) or routing key segment (Rabbit).</summary>
    public string? TopicOrRoutingKey { get; set; }

    public string? ContentType { get; set; }
}
