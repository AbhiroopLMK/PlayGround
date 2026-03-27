using Azure.Messaging.ServiceBus;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Brighter's <see cref="AzureServiceBusMessagePublisher.ConvertToServiceBusMessage"/> sets CloudEvents subject as an
/// application property only; many org subscriptions use CorrelationFilter on the broker <see cref="ServiceBusMessage.Subject"/>.
/// </summary>
public static class BrighterEventingServiceBusMessageConverter
{
    public static ServiceBusMessage ConvertToServiceBusMessage(Message message)
    {
        var sb = AzureServiceBusMessagePublisher.ConvertToServiceBusMessage(message);
        if (!string.IsNullOrEmpty(message.Header.Subject))
            sb.Subject = message.Header.Subject;
        return sb;
    }
}
