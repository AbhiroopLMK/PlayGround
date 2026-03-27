using System.Text.Json;
using System.Text.Json.Serialization;
using Paramore.Brighter.Observability;

namespace BrighterEventing.Messaging.CosmosDb.Durability;

/// <summary>
/// System.Text.Json cannot deserialize <see cref="Baggage"/> by default (it is not a simple POCO collection).
/// Brighter uses W3C baggage strings and <see cref="Baggage.LoadBaggage"/> / <see cref="Baggage.ToString"/>.
/// </summary>
internal sealed class BrighterBaggageJsonConverter : JsonConverter<Baggage>
{
    public override Baggage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var baggage = new Baggage();
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return baggage;
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                if (!string.IsNullOrEmpty(s))
                    baggage.LoadBaggage(s);
                return baggage;
            }
            default:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var el = doc.RootElement;
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s))
                        baggage.LoadBaggage(s);
                }

                return baggage;
            }
        }
    }

    public override void Write(Utf8JsonWriter writer, Baggage value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
