using BrighterEventing.Contracts.Events;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;

namespace BrighterEventing.Subscriber.Handlers;

/// <summary>
/// Handles GreetingMadeEvent. Inbox middleware records to Inbox table after handler (when [UseInboxAsync] is applied).
/// </summary>
public class GreetingMadeHandler(ILogger<GreetingMadeHandler> logger) : RequestHandlerAsync<GreetingMadeEvent>
{
    [Paramore.Brighter.Inbox.Attributes.UseInboxAsync(step: 0, contextKey: typeof(GreetingMadeHandler), onceOnly: false)]
    public override async Task<GreetingMadeEvent> HandleAsync(
        GreetingMadeEvent @event,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Greeting received: {PersonName} - {Greeting}", @event.PersonName, @event.Greeting);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
