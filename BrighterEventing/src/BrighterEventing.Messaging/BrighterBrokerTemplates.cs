using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;

namespace BrighterEventing.Messaging;

/// <summary>
/// <para>
/// <b>Read this first.</b> These are plain object graphs that Paramore Brighter expects: every publication and
/// subscription is tied to a concrete event type <typeparamref name="TEvent"/> at compile time
/// (<c>RmqPublication&lt;T&gt;</c>, <c>AzureServiceBusPublication&lt;T&gt;</c>, etc.).
/// </para>
/// <para>
/// <b>Why config + reflection exist elsewhere:</b> <c>appsettings.json</c> only gives string names such as
/// <c>OrderCreatedEvent</c>. The core maps those strings to CLR types (<see cref="Configuration.IEventTypeRegistry"/>),
/// then uses <c>MakeGenericMethod</c> once per type to call the generic factories below. That is the only “magic”:
/// it is a bridge from JSON to Brighter’s generic API, not extra business logic.
/// </para>
/// <para>
/// <b>Junior-friendly alternative:</b> skip JSON for broker wiring and assign arrays built here directly in
/// <c>Program.cs</c> (e.g. <c>consumers.Subscriptions = new[] { BrighterBrokerTemplates.AzureServiceBusSubscription&lt;OrderCreatedEvent&gt;(...) };</c>).
/// You keep full transparency; you lose central config-only deployment.
/// </para>
/// <para>
/// <b>Azure Service Bus filters:</b> <see cref="Configuration.AsbFilterPropertyKind.System"/> or
/// <see cref="Configuration.AsbFilterPropertyKind.Custom"/> plus <see cref="Configuration.AsbSubscriptionFilterCondition.PropertyName"/>
/// and <see cref="Configuration.AsbSubscriptionFilterCondition.Value"/> on
/// <see cref="Configuration.SubscriptionBinding.AzureServiceBusFilterRules"/>.
/// </para>
/// </summary>
public static class BrighterBrokerTemplates
{
    public static RmqPublication RmqPublication<TEvent>(RoutingKey exchangeRoutingKey) where TEvent : Event =>
        new RmqPublication<TEvent>
        {
            MakeChannels = OnMissingChannel.Create,
            Topic = exchangeRoutingKey
        };

    public static AzureServiceBusPublication AzureServiceBusPublication<TEvent>(
        RoutingKey serviceBusTopicPath,
        string cloudEventsSubject,
        CloudEventsType cloudEventsType) where TEvent : Event =>
        new AzureServiceBusPublication<TEvent>
        {
            MakeChannels = OnMissingChannel.Create,
            Topic = serviceBusTopicPath,
            Subject = cloudEventsSubject,
            Type = cloudEventsType
        };

    public static Subscription RmqSubscription<TEvent>(
        string subscriptionName,
        string channelName,
        RoutingKey routingKey,
        TimeSpan receiveTimeout,
        int maxRetryCount,
        TimeSpan? requeueDelay) where TEvent : Event =>
        new RmqSubscription<TEvent>(
            new SubscriptionName(subscriptionName),
            new ChannelName(channelName),
            routingKey,
            timeOut: receiveTimeout,
            makeChannels: OnMissingChannel.Create,
            requeueCount: maxRetryCount,
            requeueDelay: requeueDelay,
            messagePumpType: MessagePumpType.Proactor);

    public static Subscription AzureServiceBusSubscription<TEvent>(
        string serviceBusTopicPath,
        string subscriptionEntityNameUnderTopic,
        RoutingKey topicPathAsRoutingKey,
        TimeSpan receiveTimeout,
        int maxRetryCount,
        TimeSpan? requeueDelay,
        AzureServiceBusSubscriptionConfiguration subscriptionConfiguration) where TEvent : Event =>
        new AzureServiceBusSubscription<TEvent>(
            new SubscriptionName(serviceBusTopicPath),
            new ChannelName(subscriptionEntityNameUnderTopic),
            topicPathAsRoutingKey,
            timeOut: receiveTimeout,
            makeChannels: OnMissingChannel.Create,
            requeueCount: maxRetryCount,
            requeueDelay: requeueDelay,
            messagePumpType: MessagePumpType.Proactor,
            subscriptionConfiguration: subscriptionConfiguration);
}
