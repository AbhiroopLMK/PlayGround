using Paramore.Brighter;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Helpers for the Brighter header bag entry that maps to Azure Service Bus
/// <see cref="Azure.Messaging.ServiceBus.ServiceBusMessage.SessionId"/>.
/// </summary>
/// <remarks>
/// Brighter's <c>AzureServiceBusMessagePublisher</c> uses <c>Bag.TryGetValue("SessionId", ...)</c> only.
/// The <see cref="MessageHeader"/> docs note JSON serializers may change key casing; Postgres outbox
/// stores <c>HeaderBag</c> as JSON, so the key often becomes <c>sessionId</c>. That breaks the lookup:
/// the native session id is not set, while the value may appear only as an application property (and
/// tools may show that key in lower case).
/// </remarks>
public static class MessageHeaderAzureServiceBusExtensions
{
    /// <summary>
    /// Sets <see cref="AzureServiceBusSessionHeaderKeys.SessionId"/> when <paramref name="sessionId"/> is non-empty.
    /// Prefer a <see cref="string"/> value (not <see cref="Paramore.Brighter.Id"/>) so outbox JSON stays unambiguous.
    /// </summary>
    public static void SetAzureServiceBusSessionId(this MessageHeader header, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        header.Bag[AzureServiceBusSessionHeaderKeys.SessionId] = sessionId;
    }

    /// <summary>
    /// If the canonical key is missing, copies from camelCase / legacy keys into <c>SessionId</c>
    /// so Brighter's exact <c>TryGetValue("SessionId", ...)</c> succeeds after outbox deserialization.
    /// </summary>
    public static void EnsureCanonicalSessionIdInBag(this MessageHeader header)
    {
        if (header.Bag.ContainsKey(AzureServiceBusSessionHeaderKeys.SessionId))
            return;

        foreach (var key in new[]
                 {
                     AzureServiceBusSessionHeaderKeys.SessionIdCamelCase,
                     "sessionid",
                     "SESSIONID",
                     AzureServiceBusSessionHeaderKeys.SessionIdLegacyHyphen,
                     "SessionID"
                 })
        {
            if (!header.Bag.TryGetValue(key, out var value) || value is null)
                continue;

            var text = value.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                header.Bag[AzureServiceBusSessionHeaderKeys.SessionId] = text;
                return;
            }
        }
    }
}
