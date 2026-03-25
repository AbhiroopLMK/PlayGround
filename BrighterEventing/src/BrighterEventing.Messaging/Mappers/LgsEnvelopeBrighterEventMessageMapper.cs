using BrighterEventing.Messaging;
using BrighterEventing.Messaging.Envelope;
using BrighterEventing.Messaging.Events;
using BrighterEventing.Messaging.Wire;
using Newtonsoft.Json;
using Paramore.Brighter;

namespace BrighterEventing.Messaging.Mappers;

/// <summary>
/// Maps <see cref="LgsEnvelopeBrighterEvent"/> to/from Brighter <see cref="Message"/>; builds <see cref="LgsPublishedMessage"/>
/// from <see cref="LgsEnvelopeBrighterEvent.LgsWire"/> on publish and deserializes the body on consume.
/// </summary>
/// <remarks>
/// Implements <see cref="IAmAMessageMapperAsync{TRequest}"/> (not the sync <see cref="IAmAMessageMapper{TRequest}"/>):
/// <see cref="Paramore.Brighter.CommandProcessor.DepositPostAsync{TRequest}"/> uses <c>MapMessageAsync</c>, which
/// resolves only async mappers from the registry.
/// </remarks>
public sealed class LgsEnvelopeBrighterEventMessageMapper : IAmAMessageMapperAsync<LgsEnvelopeBrighterEvent>
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public IRequestContext? Context { get; set; }

    public Task<Message> MapToMessageAsync(
        LgsEnvelopeBrighterEvent request,
        Publication publication,
        CancellationToken cancellationToken = default)
    {
        if (request.LgsWire is null)
            throw new InvalidOperationException($"{nameof(LgsEnvelopeBrighterEvent.LgsWire)} must be set when publishing.");

        var topic = publication.Topic ?? new RoutingKey(MessagingRoutingKeys.LgsWrapped);
        var envelope = MapWireToPublished(request.LgsWire, request.EnvelopeOptions);
        envelope.SessionId = request.Id;
        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        var body = new MessageBody(json);
        var header = new MessageHeader(
            messageId: request.Id,
            topic: topic,
            messageType: MessageType.MT_EVENT);

        header.Bag.Add("session-id", request.Id);
        return Task.FromResult(new Message(header, body));
    }

    public Task<LgsEnvelopeBrighterEvent> MapToRequestAsync(Message message, CancellationToken cancellationToken = default)
    {
        var envelope = JsonConvert.DeserializeObject<LgsPublishedMessage>(message.Body.Value, JsonSettings)
            ?? throw new InvalidOperationException("Lgs envelope body could not be deserialized.");
        var result = new LgsEnvelopeBrighterEvent(message.Header.MessageId)
        {
            Envelope = envelope
        };
        return Task.FromResult(result);
    }

    private static LgsPublishedMessage MapWireToPublished(LgsEventWire source, EnvelopeMapOptions? options)
    {
        options ??= new EnvelopeMapOptions();

        var common = new CommonEventMetadata
        {
            MessageId = source.Id,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            MessageType = source.Type,
            SchemaVersion = source.SpecVersion,
            OccurredAtUtc = source.Time,
            TopicOrRoutingKey = source.Type,
            ContentType = source.DataContentType
        };

        return new LgsPublishedMessage
        {
            SpecVersion = source.SpecVersion,
            Type = source.Type,
            Source = source.Source,
            Id = source.Id,
            SessionId = source.SessionId,
            Time = source.Time,
            DataContentType = source.DataContentType,
            DataType = source.Data.DataType,
            EventData = source.Data.EventData,
            Common = common
        };
    }
}
