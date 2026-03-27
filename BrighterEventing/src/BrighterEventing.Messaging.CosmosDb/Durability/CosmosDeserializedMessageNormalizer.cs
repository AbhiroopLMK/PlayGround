using System.Text.Json;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.CosmosDb.Durability;

/// <summary>
/// STJ deserializes <c>Dictionary&lt;string, object&gt;</c> values as <see cref="JsonElement"/>.
/// Azure Service Bus cannot encode <see cref="JsonElement"/> as application properties (see
/// <c>AmqpAnnotatedMessageConverter.TryCreateAmqpPropertyValueFromNetProperty</c>).
/// </summary>
internal static class CosmosDeserializedMessageNormalizer
{
    public static void NormalizeAfterDeserialize(Message message)
    {
        var bag = message.Header.Bag;
        if (bag.Count == 0) return;

        foreach (var key in bag.Keys.ToList())
            bag[key] = NormalizeValue(bag[key])!;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null) return null;
        if (value is JsonElement je) return NormalizeJsonElement(je);
        if (value is Dictionary<string, object> d)
        {
            foreach (var k in d.Keys.ToList())
                d[k] = NormalizeValue(d[k])!;
            return d;
        }

        return value;
    }

    private static object? NormalizeJsonElement(JsonElement je)
    {
        switch (je.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                return je.GetString();
            case JsonValueKind.Number:
                if (je.TryGetInt64(out var l)) return l;
                if (je.TryGetDouble(out var d)) return d;
                return je.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object>();
                foreach (var p in je.EnumerateObject())
                    dict[p.Name] = NormalizeJsonElement(p.Value)!;
                return dict;
            case JsonValueKind.Array:
                return je.GetRawText();
            default:
                return je.GetRawText();
        }
    }
}
