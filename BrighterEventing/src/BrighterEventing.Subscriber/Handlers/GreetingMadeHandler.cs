using BrighterEventing.Contracts.Events;
using BrighterEventing.Subscriber.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Paramore.Brighter;

namespace BrighterEventing.Subscriber.Handlers;

/// <summary>
/// Handles GreetingMadeEvent. Inbox de-duplication: duplicates are rejected (onceOnly: true). When Testing:SimulateFailureCount &gt; 0, the first N invocations throw to test retry and dead-letter.
/// </summary>
public class GreetingMadeHandler(
    ILogger<GreetingMadeHandler> logger,
    IOptionsSnapshot<TestingOptions> testingOptions) : RequestHandlerAsync<GreetingMadeEvent>
{
    private static int _simulatedFailureCount;

    [Paramore.Brighter.Inbox.Attributes.UseInboxAsync(step: 0, contextKey: typeof(GreetingMadeHandler), onceOnly: true, onceOnlyAction: Paramore.Brighter.Inbox.OnceOnlyAction.Throw)]
    public override async Task<GreetingMadeEvent> HandleAsync(
        GreetingMadeEvent @event,
        CancellationToken cancellationToken = default)
    {
        var simulateCount = testingOptions.Value.SimulateFailureCount;
        if (simulateCount > 0)
        {
            var current = Interlocked.Increment(ref _simulatedFailureCount);
            if (current <= simulateCount)
            {
                logger.LogWarning("Simulated failure {Current}/{Total} for retry/DLQ testing.", current, simulateCount);
                throw new InvalidOperationException($"Simulated failure {current}/{simulateCount} for testing.");
            }
        }

        logger.LogInformation("Greeting received: {PersonName} - {Greeting}", @event.PersonName, @event.Greeting);
        return await base.HandleAsync(@event, cancellationToken);
    }
}
