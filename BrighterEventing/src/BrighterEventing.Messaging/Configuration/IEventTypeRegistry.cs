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
}
