using BrighterEventing.Messaging.Envelope;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Mappers;

/// <summary>Parses Lgs- or Rabbit-shaped bodies into a JSON payload object for mapping into app events.</summary>
public static class WireEnvelopeParser
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static JObject ParsePayloadData(Message message)
    {
        var body = message.Body.Value ?? "{}";
        if (body.Contains("\"SpecVersion\"", StringComparison.Ordinal))
        {
            var lgs = JsonConvert.DeserializeObject<LgsPublishedMessage>(body, JsonSettings)
                ?? throw new InvalidOperationException("Lgs-shaped body could not be deserialized.");
            return lgs.EventData ?? new JObject();
        }

        var rabbit = JsonConvert.DeserializeObject<RabbitPublishedMessage>(body, JsonSettings)
            ?? throw new InvalidOperationException("Rabbit internal envelope body could not be deserialized.");
        return rabbit.Payload is JObject jo ? jo : JObject.FromObject(rabbit.Payload ?? new { });
    }

    /// <summary>Lgs correlation id fallback when mapping session/business id from the envelope.</summary>
    public static string? TryGetLgsEnvelopeId(Message message)
    {
        var body = message.Body.Value ?? "{}";
        if (!body.Contains("\"SpecVersion\"", StringComparison.Ordinal))
            return null;
        var lgs = JsonConvert.DeserializeObject<LgsPublishedMessage>(body, JsonSettings);
        return lgs?.Id;
    }
}
