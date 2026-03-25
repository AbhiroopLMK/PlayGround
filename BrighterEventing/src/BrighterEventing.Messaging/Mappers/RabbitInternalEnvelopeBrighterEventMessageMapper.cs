using BrighterEventing.Messaging;
using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Events;
using BrighterEventing.Messaging.Wire;
using Newtonsoft.Json;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Mappers;

/// <summary>
/// Maps <see cref="RabbitInternalEnvelopeBrighterEvent"/> to/from Brighter <see cref="Message"/>; builds
/// <see cref="RabbitPublishedMessage"/> from <see cref="RabbitInternalEnvelopeBrighterEvent.RabbitWire"/> on publish.
/// </summary>
/// <remarks>
/// Implements <see cref="IAmAMessageMapperAsync{TRequest}"/> so <see cref="Paramore.Brighter.CommandProcessor.DepositPostAsync{TRequest}"/>
/// uses this mapper (async pipeline only).
/// </remarks>
public sealed class RabbitInternalEnvelopeBrighterEventMessageMapper : IAmAMessageMapperAsync<RabbitInternalEnvelopeBrighterEvent>
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public IRequestContext? Context { get; set; }

    public Task<Message> MapToMessageAsync(
        RabbitInternalEnvelopeBrighterEvent request,
        Publication publication,
        CancellationToken cancellationToken = default)
    {
        if (request.RabbitWire is null)
            throw new InvalidOperationException($"{nameof(RabbitInternalEnvelopeBrighterEvent.RabbitWire)} must be set when publishing.");

        var topic = publication.Topic ?? new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped);
        var envelope = MapWireToPublished(request.RabbitWire, request.EnvelopeOptions);
        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        var body = new MessageBody(json);
        var header = new MessageHeader(
            messageId: request.Id,
            topic: topic,
            messageType: MessageType.MT_EVENT);
        return Task.FromResult(new Message(header, body));
    }

    public Task<RabbitInternalEnvelopeBrighterEvent> MapToRequestAsync(Message message, CancellationToken cancellationToken = default)
    {
        var envelope = JsonConvert.DeserializeObject<RabbitPublishedMessage>(message.Body.Value, JsonSettings)
            ?? throw new InvalidOperationException("Rabbit internal envelope body could not be deserialized.");
        var result = new RabbitInternalEnvelopeBrighterEvent(message.Header.MessageId)
        {
            Envelope = envelope
        };
        return Task.FromResult(result);
    }

    private static RabbitPublishedMessage MapWireToPublished(RabbitInternalEventWire source, EnvelopeMapOptions? options)
    {
        options ??= new EnvelopeMapOptions();

        var occurred = options.OccurredAtUtc ?? DateTime.UtcNow;

        var common = new CommonEventMetadata
        {
            MessageId = options.MessageId,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            MessageType = source.MessageName,
            SchemaVersion = source.Version,
            OccurredAtUtc = occurred,
            TopicOrRoutingKey = source.MessageName,
            ContentType = options.ContentType
        };

        return new RabbitPublishedMessage
        {
            MessageName = source.MessageName,
            Version = source.Version,
            Payload = source.Payload,
            Common = common
        };
    }
}
