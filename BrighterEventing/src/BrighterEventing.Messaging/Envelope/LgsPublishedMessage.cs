using Newtonsoft.Json.Linq;

namespace BrighterEventing.Messaging.Envelope;

/// <summary>
/// Lgs-style body (unchanged wire shape) plus a single <see cref="Common"/> block for Implementation Guide metadata — no duplicate keys at root.
/// </summary>
public class LgsPublishedMessage
{
    public string SpecVersion { get; set; } = "1.0";

    public string Type { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public DateTime Time { get; set; }

    public string DataContentType { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public JObject? EventData { get; set; }

    /// <summary>Recommended minimum metadata (messageId, correlationId, …) — populated by application mappers when publishing Lgs-shaped bodies (see <see cref="Mappers.WireEnvelopeBuilder"/>).</summary>
    public EventMetadata? Common { get; set; }
}
