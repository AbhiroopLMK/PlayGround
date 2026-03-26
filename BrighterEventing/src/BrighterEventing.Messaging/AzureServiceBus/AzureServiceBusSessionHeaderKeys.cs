namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Bag key used by Brighter's ASB gateway (<c>ASBConstants.SessionIdKey</c>) when mapping to
/// <see cref="Azure.Messaging.ServiceBus.ServiceBusMessage.SessionId"/>.
/// </summary>
public static class AzureServiceBusSessionHeaderKeys
{
    /// <summary>Canonical key — Brighter uses an exact dictionary lookup for this string only.</summary>
    public const string SessionId = "SessionId";

    /// <summary>After JSON round-trip (e.g. Postgres outbox), serializers often emit camelCase keys.</summary>
    public const string SessionIdCamelCase = "sessionId";

    /// <summary>Alternate spelling sometimes seen in tooling or legacy mappers.</summary>
    public const string SessionIdLegacyHyphen = "session-id";
}
