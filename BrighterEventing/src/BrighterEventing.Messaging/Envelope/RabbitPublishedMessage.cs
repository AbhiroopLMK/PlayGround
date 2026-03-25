namespace BrighterEventing.Messaging.Envelope;

/// <summary>
/// Rabbit internal envelope (MessageName, Version, Payload) plus <see cref="Common"/> for guide metadata only — no duplicate properties.
/// </summary>
public class RabbitPublishedMessage
{
    public string MessageName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public object? Payload { get; set; }

    /// <summary>Recommended minimum metadata — populated by <see cref="BrighterEventing.Messaging.Mappers.RabbitInternalEnvelopeBrighterEventMessageMapper"/> when publishing.</summary>
    public CommonEventMetadata? Common { get; set; }
}
