using Paramore.Brighter;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>Fluent builder for <see cref="IEventTypeRegistry"/>.</summary>
public sealed class EventTypeCatalogBuilder
{
    private readonly Dictionary<string, Type> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, string> _cloudEventsTypeOverrides = new();

    /// <summary>
    /// Maps one or more config keys to <typeparamref name="TEvent"/> (e.g. <c>Map&lt;OrderCreatedEvent&gt;("OrderCreatedEvent", "OrderCreated")</c>).
    /// </summary>
    public EventTypeCatalogBuilder Map<TEvent>(params string[] configKeys) where TEvent : Event
    {
        var t = typeof(TEvent);
        foreach (var key in configKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Config key cannot be null or whitespace.", nameof(configKeys));
            var trimmed = key.Trim();
            _map[trimmed] = t;
        }

        return this;
    }

    /// <summary>
    /// Sets the CloudEvents <c>type</c> string for <typeparamref name="TEvent"/> (Brighter producer lookup and
    /// <see cref="MessageHeader.Type"/>). Use the same value as in your message mapper / <c>WireEnvelopeBuilder</c> LGS type.
    /// When omitted, the CLR type <see cref="Type.FullName"/> is used.
    /// </summary>
    public EventTypeCatalogBuilder WithCloudEventsType<TEvent>(string cloudEventsType) where TEvent : Event
    {
        if (string.IsNullOrWhiteSpace(cloudEventsType))
            throw new ArgumentException("CloudEvents type cannot be null or whitespace.", nameof(cloudEventsType));

        _cloudEventsTypeOverrides[typeof(TEvent)] = cloudEventsType.Trim();
        return this;
    }

    public EventTypeRegistry Build()
    {
        var cloud = new Dictionary<Type, string>();
        foreach (var t in _map.Values.Distinct())
        {
            cloud[t] = _cloudEventsTypeOverrides.TryGetValue(t, out var o)
                ? o
                : t.FullName ?? t.Name;
        }

        return new EventTypeRegistry(_map, cloud);
    }
}
