using System.Reflection;
using Polly;
using Polly.Registry;
using Polly.Retry;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Brighter's <c>UseResiliencePipelineAsync</c> resolves a <strong>generic</strong> pipeline per request type.
/// Register one generic <c>TryAddBuilder&lt;T&gt;</c> per event type in
/// <see cref="IEventTypeRegistry.RegisteredEventTypes"/> (same retry policy as the shared consumer options).
/// </summary>
/// <remarks>
/// Same pattern as <see cref="BrighterMessagingBrokerRegistration"/>: config/registry yields a list of
/// <see cref="Type"/>; Brighter’s Polly registration is generic per <c>T</c>, so one <c>MakeGenericMethod</c> per event type.
/// </remarks>
public static class BrighterSubscriberResilienceRegistration
{
    public const string ConsumerRetryPipelineName = "ConsumerRetryPipeline";

    private static readonly MethodInfo AddConsumerRetryForEventTypeMethod =
        typeof(BrighterSubscriberResilienceRegistration).GetMethod(
            nameof(AddConsumerRetryForEventType),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    public static void RegisterConsumerRetryPipelinesForEventTypes(
        ResiliencePipelineRegistry<string> registry,
        BrighterSubscriberOptions options,
        IEventTypeRegistry eventTypeRegistry)
    {
        foreach (var eventType in eventTypeRegistry.RegisteredEventTypes)
        {
            AddConsumerRetryForEventTypeMethod.MakeGenericMethod(eventType).Invoke(
                null,
                new object[] { registry, options });
        }
    }

    private static void AddConsumerRetryForEventType<T>(
        ResiliencePipelineRegistry<string> registry,
        BrighterSubscriberOptions options)
    {
        registry.TryAddBuilder<T>(ConsumerRetryPipelineName, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = Math.Max(0, options.Consumer.MaxRetryCount),
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, options.Consumer.RequeueDelayMs)),
                BackoffType = DelayBackoffType.Exponential,
            }));
    }
}
