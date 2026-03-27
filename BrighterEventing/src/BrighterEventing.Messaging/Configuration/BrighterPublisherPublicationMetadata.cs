using Microsoft.Extensions.Configuration;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Resolves optional <see cref="PublicationBinding.ServiceBusEventType"/> from config for a Brighter <see cref="Publication"/>
/// (matched by ASB topic path + CloudEvents subject).
/// </summary>
public static class BrighterPublisherPublicationMetadata
{
    /// <summary>
    /// Returns <c>ServiceBusEventType</c> for the publication row that matches <paramref name="publication"/>'s topic and subject, if any.
    /// </summary>
    public static string? TryGetServiceBusEventType(IConfiguration configuration, Publication publication)
    {
        if (publication.Topic is null)
            return null;

        var options = BrighterPublisherServiceCollectionExtensions.BindPublisherOptions(configuration);
        var topicPath = publication.Topic.ToString();
        var subject = publication.Subject ?? "";

        foreach (var p in options.Publications)
        {
            if (string.IsNullOrWhiteSpace(p.ServiceBusEventType))
                continue;

            var configTopic = BrighterMessagingBrokerRegistration.GetAzureServiceBusPublicationTopicPath(p);
            var configSubject = BrighterMessagingBrokerRegistration.GetAzureServiceBusPublicationSubject(p);
            if (string.Equals(configTopic, topicPath, StringComparison.Ordinal)
                && string.Equals(configSubject, subject, StringComparison.Ordinal))
                return p.ServiceBusEventType.Trim();
        }

        return null;
    }
}
