using Paramore.Brighter;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Maps configuration <c>EventType</c> strings (e.g. <c>OrderCreatedEvent</c>) to concrete Brighter <see cref="Event"/> CLR types.
/// Each application registers its own events; <c>BrighterEventing.Messaging</c> stays free of app-specific types.
/// </summary>
public interface IEventTypeRegistry
{
    /// <summary>Distinct event types registered for this host (for Polly, diagnostics, etc.).</summary>
    IReadOnlyCollection<Type> RegisteredEventTypes { get; }

    /// <summary>Resolves a configured event name to a CLR type.</summary>
    /// <exception cref="InvalidOperationException">Unknown or empty <paramref name="eventTypeFromConfig"/>.</exception>
    Type Resolve(string eventTypeFromConfig);

    /// <summary>
    /// CloudEvents <c>type</c> for Brighter producer registry lookup. Must match <see cref="MessageHeader.Type"/> on
    /// outgoing messages (the same string your mappers use as LGS / CloudEvents <c>type</c>).
    /// </summary>
    CloudEventsType GetCloudEventsType(Type clrType);
}
