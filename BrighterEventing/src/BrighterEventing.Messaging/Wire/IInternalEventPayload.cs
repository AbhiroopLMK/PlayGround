namespace BrighterEventing.Messaging.Wire;

/// <summary>
/// Mirrors <c>IInternalEventPayload&lt;T&gt;</c> from transaction-request services (RabbitMQ internal events).
/// </summary>
public interface IInternalEventPayload<out T>
{
    string MessageName { get; }

    string Version { get; }

    T Payload { get; }
}
