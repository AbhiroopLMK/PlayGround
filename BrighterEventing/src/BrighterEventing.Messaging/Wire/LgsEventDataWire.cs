using Newtonsoft.Json.Linq;

namespace BrighterEventing.Messaging.Wire;

/// <summary>
/// Aligns with LgsEventData / PublishEventRequestDto (Azure Event Service) — application supplies values.
/// </summary>
public class LgsEventDataWire
{
    public string DataType { get; set; } = string.Empty;

    public JObject? EventData { get; set; }
}
