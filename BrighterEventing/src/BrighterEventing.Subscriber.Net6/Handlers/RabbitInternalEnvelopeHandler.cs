using BrighterEventing.Messaging.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox.Attributes;
using Paramore.Brighter.Policies.Attributes;

namespace BrighterEventing.Subscriber.Net6.Handlers;

public class RabbitInternalEnvelopeHandler : RequestHandlerAsync<RabbitInternalEnvelopeBrighterEvent>
{
    private readonly ILogger<RabbitInternalEnvelopeHandler> _logger;

    public RabbitInternalEnvelopeHandler(ILogger<RabbitInternalEnvelopeHandler> logger)
    {
        _logger = logger;
    }

    [UseResiliencePipelineAsync("ConsumerRetryPipeline", step: 1)]
    [UseInboxAsync(step: 0, contextKey: typeof(RabbitInternalEnvelopeHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<RabbitInternalEnvelopeBrighterEvent> HandleAsync(
        RabbitInternalEnvelopeBrighterEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (@event.Envelope is null) throw new ArgumentNullException(nameof(@event.Envelope));
        var e = @event.Envelope;
        _logger.LogInformation(
            "Rabbit internal wrapped: MessageName={MessageName}, Version={Version}, Common.MessageId={MessageId}",
            e.MessageName, e.Version, e.Common?.MessageId);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
