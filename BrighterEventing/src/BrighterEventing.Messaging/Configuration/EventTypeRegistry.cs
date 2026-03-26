using Paramore.Brighter;

namespace BrighterEventing.Messaging.Configuration;

public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly Dictionary<string, Type> _map;

    public EventTypeRegistry(IReadOnlyDictionary<string, Type> mappings)
    {
        _map = new Dictionary<string, Type>(mappings, StringComparer.OrdinalIgnoreCase);
        RegisteredEventTypes = _map.Values.Distinct().ToArray();
    }

    public IReadOnlyCollection<Type> RegisteredEventTypes { get; }

    public Type Resolve(string eventTypeFromConfig)
    {
        if (string.IsNullOrWhiteSpace(eventTypeFromConfig))
            throw new InvalidOperationException("EventType is required.");

        var key = eventTypeFromConfig.Trim();
        if (_map.TryGetValue(key, out var type))
            return type;

        throw new InvalidOperationException(
            $"Unknown EventType '{eventTypeFromConfig}'. Register it with {nameof(EventTypeCatalogBuilder)}.{nameof(EventTypeCatalogBuilder.Map)}<TEvent>(...).");
    }
}
