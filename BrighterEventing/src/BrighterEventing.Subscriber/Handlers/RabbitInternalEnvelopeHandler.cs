using BrighterEventing.Messaging.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Handlers;

public class RabbitInternalEnvelopeHandler(ILogger<RabbitInternalEnvelopeHandler> logger)
    : RequestHandlerAsync<RabbitInternalEnvelopeBrighterEvent>
{
    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(RabbitInternalEnvelopeHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<RabbitInternalEnvelopeBrighterEvent> HandleAsync(
        RabbitInternalEnvelopeBrighterEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event.Envelope);
        var e = @event.Envelope;
        logger.LogInformation(
            "Rabbit internal wrapped: MessageName={MessageName}, Version={Version}, Common.MessageId={MessageId}, Common.CorrelationId={CorrelationId}",
            e.MessageName, e.Version, e.Common?.MessageId, e.Common?.CorrelationId);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
