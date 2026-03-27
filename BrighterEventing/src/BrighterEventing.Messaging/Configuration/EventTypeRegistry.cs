using Paramore.Brighter;

namespace BrighterEventing.Messaging.Configuration;

public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly Dictionary<string, Type> _map;
    private readonly Dictionary<Type, string> _cloudEventsTypeByClrType;

    public EventTypeRegistry(
        IReadOnlyDictionary<string, Type> mappings,
        IReadOnlyDictionary<Type, string> cloudEventsTypeByClrType)
    {
        _map = new Dictionary<string, Type>(mappings, StringComparer.OrdinalIgnoreCase);
        _cloudEventsTypeByClrType = new Dictionary<Type, string>(cloudEventsTypeByClrType);
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

    public CloudEventsType GetCloudEventsType(Type clrType)
    {
        if (_cloudEventsTypeByClrType.TryGetValue(clrType, out var s))
            return new CloudEventsType(s);

        return new CloudEventsType(clrType.FullName ?? clrType.Name);
    }
}
