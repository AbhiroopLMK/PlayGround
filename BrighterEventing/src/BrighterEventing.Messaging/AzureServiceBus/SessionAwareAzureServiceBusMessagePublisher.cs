using Azure.Messaging.ServiceBus;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Optional: same as Brighter's conversion but normalizes session bag keys first (e.g. for tests or custom send paths).
/// </summary>
public static class SessionAwareAzureServiceBusMessagePublisher
{
    public static ServiceBusMessage ConvertToServiceBusMessage(Message message)
    {
        message.Header.EnsureCanonicalSessionIdInBag();
        return BrighterEventingServiceBusMessageConverter.ConvertToServiceBusMessage(message);
    }
}
