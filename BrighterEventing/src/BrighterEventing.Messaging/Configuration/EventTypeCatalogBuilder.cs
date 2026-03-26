using Paramore.Brighter;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>Fluent builder for <see cref="IEventTypeRegistry"/>.</summary>
public sealed class EventTypeCatalogBuilder
{
    private readonly Dictionary<string, Type> _map = new(StringComparer.OrdinalIgnoreCase);

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

    public EventTypeRegistry Build() => new EventTypeRegistry(_map);
}
