namespace BrighterEventing.Messaging.Envelope;

/// <summary>Optional overrides when mapping wire payloads to published envelopes (e.g. correlation from IOperationContext).</summary>
public class EnvelopeMapOptions
{
    public string? MessageId { get; set; }

    public string? CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public DateTime? OccurredAtUtc { get; set; }

    public string? ContentType { get; set; }
}
