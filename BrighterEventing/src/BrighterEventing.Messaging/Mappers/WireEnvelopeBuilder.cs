using BrighterEventing.Messaging.AzureServiceBus;
using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Wire;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Mappers;

/// <summary>
/// Transport-agnostic Lgs (Azure) vs Rabbit internal JSON wire shapes. App-specific mappers supply
/// message names, sources, session id, and payload; this type does not reference application event classes.
/// </summary>
public static class WireEnvelopeBuilder
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string ResolveTransport(IConfiguration configuration)
    {
        var t = configuration["BrighterMessaging:Publisher:Transport"]
            ?? configuration["BrighterMessaging:Subscriber:Transport"]
            ?? configuration["Transport"];
        return string.IsNullOrWhiteSpace(t) ? BrokerType.RabbitMQ : t.Trim();
    }

    /// <summary>
    /// Builds either an Lgs-shaped message (Azure Service Bus) or Rabbit internal envelope from the same logical payload.
    /// For Azure Service Bus, <paramref name="publication"/>.<c>Topic</c> is the Service Bus topic path (routing key);
    /// subject comes from <paramref name="publishRoutingKey"/> when set, otherwise <paramref name="publication"/>.<c>Subject</c>.
    /// </summary>
    public static Message BuildForConfiguredTransport(
        Id messageId,
        string? publishRoutingKey,
        Publication publication,
        IConfiguration configuration,
        string lgsType,
        string lgsSource,
        string sessionId,
        string rabbitMessageName,
        string rabbitSchemaVersion,
        object payload)
    {
        if (ResolveTransport(configuration) == BrokerType.AzureServiceBus)
        {
            if (publication.Topic is null)
                throw new InvalidOperationException("Publication Topic is not set.");

            var subject = !string.IsNullOrWhiteSpace(publishRoutingKey)
                ? publishRoutingKey.Trim()
                : publication.Subject ?? string.Empty;

            return BuildLgsMessage(
                messageId,
                publication.Topic,
                subject,
                lgsType,
                lgsSource,
                sessionId,
                JObject.FromObject(payload));
        }

        var topic = ResolveRabbitTopic(publishRoutingKey, publication);
        return BuildRabbitMessage(messageId, topic, rabbitMessageName, rabbitSchemaVersion, payload);
    }

    /// <summary>RabbitMQ: routing key for the exchange (override <paramref name="publishRoutingKey"/> or publication topic).</summary>
    public static RoutingKey ResolveRabbitTopic(string? publishRoutingKey, Publication publication)
    {
        if (publication.Topic is null)
            throw new InvalidOperationException("Publication Topic is not set.");

        if (!string.IsNullOrWhiteSpace(publishRoutingKey))
            return new RoutingKey(publishRoutingKey.Trim());

        return publication.Topic;
    }

    public static Message BuildLgsMessage(
        Id messageId,
        RoutingKey topic,
        string cloudEventsSubject,
        string type,
        string source,
        string sessionId,
        JObject eventData)
    {
        var wire = new LgsEventWire
        {
            SpecVersion = "1.0",
            Type = type,
            Source = source,
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Time = DateTime.UtcNow,
            DataContentType = "application/json",
            Data = new LgsEventDataWire
            {
                DataType = "application/json",
                EventData = eventData
            }
        };

        var options = new EnvelopeMapOptions();
        var eventMetadata = new EventMetadata
        {
            MessageId = wire.Id,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            MessageType = wire.Type,
            SchemaVersion = wire.SpecVersion,
            OccurredAtUtc = wire.Time,
            TopicOrRoutingKey = wire.Type,
            ContentType = wire.DataContentType
        };

        var envelope = new LgsPublishedMessage
        {
            SpecVersion = wire.SpecVersion,
            Type = wire.Type,
            Source = wire.Source,
            Id = wire.Id,
            SessionId = wire.SessionId,
            Time = wire.Time,
            DataContentType = wire.DataContentType,
            DataType = wire.Data.DataType,
            EventData = wire.Data.EventData,
            Common = eventMetadata
        };

        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        var body = new MessageBody(json);
        var header = new MessageHeader(
            messageId: messageId,
            topic: topic,
            messageType: MessageType.MT_EVENT);
        header.Type = new CloudEventsType(type);
        header.Subject = cloudEventsSubject;
        header.SetAzureServiceBusSessionId(sessionId);
        return new Message(header, body);
    }

    public static Message BuildRabbitMessage(Id messageId, RoutingKey topic, string messageName, string version, object payload)
    {
        var wire = new RabbitInternalEventWire
        {
            MessageName = messageName,
            Version = version,
            Payload = payload,
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = Guid.NewGuid().ToString("N"),
            ContentType = "application/json"
        };

        var options = new EnvelopeMapOptions
        {
            OccurredAtUtc = DateTime.UtcNow,
            ContentType = wire.ContentType
        };

        var occurred = options.OccurredAtUtc ?? DateTime.UtcNow;
        var eventMetadata = new EventMetadata
        {
            MessageId = wire.MessageId,
            CorrelationId = wire.CorrelationId,
            CausationId = options.CausationId,
            MessageType = wire.MessageName,
            SchemaVersion = wire.Version,
            OccurredAtUtc = occurred,
            TopicOrRoutingKey = wire.MessageName,
            ContentType = options.ContentType
        };

        var envelope = new RabbitPublishedMessage
        {
            MessageName = wire.MessageName,
            Version = wire.Version,
            Payload = payload,
            Common = eventMetadata
        };

        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        var body = new MessageBody(json);
        var header = new MessageHeader(
            messageId: messageId,
            topic: topic,
            messageType: MessageType.MT_EVENT);
        return new Message(header, body);
    }
}
