namespace BrighterEventing.Messaging.Wire;

/// <summary>
/// Application-layer input matching the Azure LgsEvent envelope (Service Bus). User fills these; the library wraps with <see cref="Envelope.LgsPublishedMessage"/>.
/// </summary>
public class LgsEventWire
{
    public string SpecVersion { get; set; } = "1.0";

    public string Type { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public DateTime Time { get; set; }

    public string DataContentType { get; set; } = "application/json";

    public LgsEventDataWire Data { get; set; } = new();
}
