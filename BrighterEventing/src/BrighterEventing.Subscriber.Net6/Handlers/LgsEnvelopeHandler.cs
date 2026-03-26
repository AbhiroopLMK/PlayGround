using BrighterEventing.Messaging.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Net6.Handlers;

public class LgsEnvelopeHandler : RequestHandlerAsync<LgsEnvelopeBrighterEvent>
{
    private readonly ILogger<LgsEnvelopeHandler> _logger;

    public LgsEnvelopeHandler(ILogger<LgsEnvelopeHandler> logger)
    {
        _logger = logger;
    }

    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(LgsEnvelopeHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<LgsEnvelopeBrighterEvent> HandleAsync(
        LgsEnvelopeBrighterEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (@event.Envelope is null) throw new ArgumentNullException(nameof(@event.Envelope));
        var e = @event.Envelope;
        _logger.LogInformation(
            "Lgs wrapped: Type={Type}, Id={Id}, Common.MessageId={CommonMessageId}, Common.CorrelationId={CorrelationId}",
            e.Type, e.Id, e.Common?.MessageId, e.Common?.CorrelationId);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
