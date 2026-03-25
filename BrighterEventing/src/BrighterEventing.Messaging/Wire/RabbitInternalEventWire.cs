namespace BrighterEventing.Messaging.Wire;

/// <summary>
/// Serializable Rabbit internal event shape for publishing (mirrors application command payload).
/// </summary>
public class RabbitInternalEventWire : IInternalEventPayload<object>
{
    public required string MessageName { get; init; }

    public required string Version { get; init; }

    public required object Payload { get; init; }

    public string? MessageId { get; init; }

    public string? CorrelationId { get; init; }

    public string? ContentType { get; init; }
}
