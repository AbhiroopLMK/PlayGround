using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrighterEventing.Messaging.CosmosDb.Durability;

/// <summary>
/// System.Text.Json options for persisting <see cref="Paramore.Brighter.Message"/> in Cosmos.
/// Brighter v10 uses STJ converters on <see cref="Paramore.Brighter.MessageHeader"/> (e.g. <c>RoutingKeyConvertor</c>);
/// Newtonsoft.Json cannot deserialize those attributes and throws <see cref="InvalidCastException"/>.
/// </summary>
internal static class BrighterMessageCosmosJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new BrighterBaggageJsonConverter() },
    };
}
