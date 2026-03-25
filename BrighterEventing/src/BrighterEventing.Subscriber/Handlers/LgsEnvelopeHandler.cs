using BrighterEventing.Messaging.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Handlers;

public class LgsEnvelopeHandler(ILogger<LgsEnvelopeHandler> logger) : RequestHandlerAsync<LgsEnvelopeBrighterEvent>
{
    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(LgsEnvelopeHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<LgsEnvelopeBrighterEvent> HandleAsync(
        LgsEnvelopeBrighterEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event.Envelope);
        var e = @event.Envelope;
        logger.LogInformation(
            "Lgs wrapped: Type={Type}, Id={Id}, Common.MessageId={CommonMessageId}, Common.CorrelationId={CorrelationId}, Common.MessageType={Cm}",
            e.Type, e.Id, e.Common?.MessageId, e.Common?.CorrelationId, e.Common?.MessageType);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
