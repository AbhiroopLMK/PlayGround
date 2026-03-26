using System.Reflection;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Builds Brighter producer publications and consumer subscriptions from configuration, using an
/// <see cref="IEventTypeRegistry"/> so the messaging layer does not reference application event types.
/// </summary>
public static class BrighterMessagingBrokerRegistration
{
    private static readonly MethodInfo AddRmqPublicationForEventMethod =
        typeof(BrighterMessagingBrokerRegistration).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == nameof(AddRmqPublicationForEvent) && m.IsGenericMethodDefinition);

    private static readonly MethodInfo AddAzureServiceBusPublicationForEventMethod =
        typeof(BrighterMessagingBrokerRegistration).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == nameof(AddAzureServiceBusPublicationForEvent) && m.IsGenericMethodDefinition);

    private static readonly MethodInfo CreateRmqSubscriptionForEventMethod =
        typeof(BrighterMessagingBrokerRegistration).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == nameof(CreateRmqSubscriptionForEvent) && m.IsGenericMethodDefinition);

    private static readonly MethodInfo CreateAzureServiceBusSubscriptionForEventMethod =
        typeof(BrighterMessagingBrokerRegistration).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == nameof(CreateAzureServiceBusSubscriptionForEvent) && m.IsGenericMethodDefinition);

    /// <summary>
    /// Routing keys from <see cref="BrighterPublisherOptions.Publications"/> for the given domain event type (config order).
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredRoutingKeysForEventType(
        BrighterPublisherOptions options,
        string eventType,
        IEventTypeRegistry registry)
    {
        if (options.Publications == null || options.Publications.Count == 0)
            return Array.Empty<string>();

        var target = registry.Resolve(eventType);
        var keys = new List<string>();
        foreach (var p in options.Publications)
        {
            if (registry.Resolve(p.EventType) != target)
                continue;
            if (!string.IsNullOrWhiteSpace(p.RoutingKey))
                keys.Add(p.RoutingKey.Trim());
        }

        return keys;
    }

    /// <remarks>
    /// One producer registration per <see cref="BrighterPublisherOptions.Publications"/> row so alternate
    /// routing keys (e.g. two rows for the same <c>EventType</c>) each get a matching producer for <c>PostAsync</c>.
    /// </remarks>
    public static RmqPublication[] BuildRmqPublications(BrighterPublisherOptions options, IEventTypeRegistry registry)
    {
        ValidatePublications(options, registry);
        var list = new List<RmqPublication>();
        foreach (var p in options.Publications)
        {
            var eventClrType = registry.Resolve(p.EventType);
            var rk = new RoutingKey(p.RoutingKey);
            list.Add(InvokeAddRmqPublication(eventClrType, rk));
        }

        return list.ToArray();
    }

    public static AzureServiceBusPublication[] BuildAzureServiceBusPublications(
        BrighterPublisherOptions options,
        IEventTypeRegistry registry)
    {
        ValidatePublications(options, registry);
        var list = new List<AzureServiceBusPublication>();
        foreach (var p in options.Publications)
        {
            var eventClrType = registry.Resolve(p.EventType);
            var rk = new RoutingKey(p.RoutingKey);
            list.Add(InvokeAddAzureServiceBusPublication(eventClrType, rk));
        }

        return list.ToArray();
    }

    public static Subscription[] BuildRmqSubscriptions(
        BrighterSubscriberOptions options,
        IEventTypeRegistry registry,
        TimeSpan timeout,
        TimeSpan? requeueDelay)
    {
        ValidateSubscriptions(options, registry);
        var list = new List<Subscription>();
        foreach (var s in options.Subscriptions)
        {
            var subName = ResolveSubscriptionName(s, options);
            var channelName = ResolveChannelName(s, options);
            var rk = new RoutingKey(s.RoutingKey);
            var eventClrType = registry.Resolve(s.EventType);
            list.Add(InvokeCreateRmqSubscription(
                eventClrType,
                subName,
                channelName,
                rk,
                timeout,
                options,
                requeueDelay));
        }

        return list.ToArray();
    }

    public static Subscription[] BuildAzureServiceBusSubscriptions(
        BrighterSubscriberOptions options,
        IEventTypeRegistry registry,
        TimeSpan timeout,
        TimeSpan? requeueDelay,
        AzureServiceBusSubscriptionConfiguration subscriptionConfiguration)
    {
        ValidateSubscriptions(options, registry);
        var list = new List<Subscription>();
        foreach (var s in options.Subscriptions)
        {
            var subName = ResolveSubscriptionName(s, options);
            var channelName = ResolveChannelName(s, options);
            var rk = new RoutingKey(s.RoutingKey);
            var eventClrType = registry.Resolve(s.EventType);
            list.Add(InvokeCreateAzureServiceBusSubscription(
                eventClrType,
                subName,
                channelName,
                rk,
                timeout,
                options,
                requeueDelay,
                subscriptionConfiguration));
        }

        return list.ToArray();
    }

    private static RmqPublication InvokeAddRmqPublication(Type eventType, RoutingKey rk)
    {
        var m = AddRmqPublicationForEventMethod.MakeGenericMethod(eventType);
        return (RmqPublication)m.Invoke(null, new object[] { rk })!;
    }

    private static AzureServiceBusPublication InvokeAddAzureServiceBusPublication(Type eventType, RoutingKey rk)
    {
        var m = AddAzureServiceBusPublicationForEventMethod.MakeGenericMethod(eventType);
        return (AzureServiceBusPublication)m.Invoke(null, new object[] { rk })!;
    }

    private static Subscription InvokeCreateRmqSubscription(
        Type eventType,
        string subName,
        string channelName,
        RoutingKey rk,
        TimeSpan timeout,
        BrighterSubscriberOptions options,
        TimeSpan? requeueDelay)
    {
        var m = CreateRmqSubscriptionForEventMethod.MakeGenericMethod(eventType);
        return (Subscription)m.Invoke(null, new object?[]
        {
            subName,
            channelName,
            rk,
            timeout,
            options,
            requeueDelay
        })!;
    }

    private static Subscription InvokeCreateAzureServiceBusSubscription(
        Type eventType,
        string subName,
        string channelName,
        RoutingKey rk,
        TimeSpan timeout,
        BrighterSubscriberOptions options,
        TimeSpan? requeueDelay,
        AzureServiceBusSubscriptionConfiguration subscriptionConfiguration)
    {
        var m = CreateAzureServiceBusSubscriptionForEventMethod.MakeGenericMethod(eventType);
        return (Subscription)m.Invoke(null, new object?[]
        {
            subName,
            channelName,
            rk,
            timeout,
            options,
            requeueDelay,
            subscriptionConfiguration
        })!;
    }

    private static RmqPublication AddRmqPublicationForEvent<T>(RoutingKey rk) where T : Event =>
        new RmqPublication<T>
        {
            MakeChannels = OnMissingChannel.Create,
            Topic = rk
        };

    private static AzureServiceBusPublication AddAzureServiceBusPublicationForEvent<T>(RoutingKey rk) where T : Event =>
        new AzureServiceBusPublication<T>
        {
            MakeChannels = OnMissingChannel.Create,
            Topic = rk
        };

    private static Subscription CreateRmqSubscriptionForEvent<T>(
        string subName,
        string channelName,
        RoutingKey rk,
        TimeSpan timeout,
        BrighterSubscriberOptions options,
        TimeSpan? requeueDelay) where T : Event =>
        new RmqSubscription<T>(
            new SubscriptionName(subName),
            new ChannelName(channelName),
            rk,
            timeOut: timeout,
            makeChannels: OnMissingChannel.Create,
            requeueCount: options.Consumer.MaxRetryCount,
            requeueDelay: requeueDelay,
            messagePumpType: MessagePumpType.Proactor);

    private static Subscription CreateAzureServiceBusSubscriptionForEvent<T>(
        string subName,
        string channelName,
        RoutingKey rk,
        TimeSpan timeout,
        BrighterSubscriberOptions options,
        TimeSpan? requeueDelay,
        AzureServiceBusSubscriptionConfiguration subscriptionConfiguration) where T : Event =>
        new AzureServiceBusSubscription<T>(
            new SubscriptionName(subName),
            new ChannelName(channelName),
            rk,
            timeOut: timeout,
            makeChannels: OnMissingChannel.Create,
            requeueCount: options.Consumer.MaxRetryCount,
            requeueDelay: requeueDelay,
            messagePumpType: MessagePumpType.Proactor,
            subscriptionConfiguration: subscriptionConfiguration);

    private static void ValidatePublications(BrighterPublisherOptions options, IEventTypeRegistry registry)
    {
        if (options.Publications == null || options.Publications.Count == 0)
        {
            throw new InvalidOperationException(
                "Configure at least one BrighterMessaging:Publisher:Publications entry (EventType + RoutingKey).");
        }

        for (var i = 0; i < options.Publications.Count; i++)
        {
            var p = options.Publications[i];
            if (string.IsNullOrWhiteSpace(p.RoutingKey))
            {
                throw new InvalidOperationException(
                    $"BrighterMessaging:Publisher:Publications[{i}].RoutingKey is required.");
            }

            if (string.IsNullOrWhiteSpace(p.EventType))
            {
                throw new InvalidOperationException(
                    $"BrighterMessaging:Publisher:Publications[{i}].EventType is required.");
            }

            _ = registry.Resolve(p.EventType);
        }
    }

    private static void ValidateSubscriptions(BrighterSubscriberOptions options, IEventTypeRegistry registry)
    {
        if (options.Subscriptions == null || options.Subscriptions.Count == 0)
        {
            throw new InvalidOperationException(
                "Configure at least one BrighterMessaging:Subscriber:Subscriptions entry (EventType, RoutingKey, and for RabbitMQ ChannelName).");
        }

        for (var i = 0; i < options.Subscriptions.Count; i++)
        {
            var s = options.Subscriptions[i];
            if (string.IsNullOrWhiteSpace(s.RoutingKey))
            {
                throw new InvalidOperationException(
                    $"BrighterMessaging:Subscriber:Subscriptions[{i}].RoutingKey is required.");
            }

            if (string.IsNullOrWhiteSpace(s.EventType))
            {
                throw new InvalidOperationException(
                    $"BrighterMessaging:Subscriber:Subscriptions[{i}].EventType is required.");
            }

            _ = registry.Resolve(s.EventType);
        }
    }

    private static string ResolveSubscriptionName(SubscriptionBinding s, BrighterSubscriberOptions options)
    {
        if (!string.IsNullOrWhiteSpace(s.SubscriptionName))
            return s.SubscriptionName.Trim();

        return options.Transport == BrokerType.AzureServiceBus
            ? options.AzureServiceBus.SubscriptionName
            : options.RabbitMQ.SubscriptionName;
    }

    private static string ResolveChannelName(SubscriptionBinding s, BrighterSubscriberOptions options)
    {
        if (!string.IsNullOrWhiteSpace(s.ChannelName))
            return s.ChannelName.Trim();

        if (options.Transport == BrokerType.AzureServiceBus)
            return ResolveSubscriptionName(s, options);

        throw new InvalidOperationException(
            "BrighterMessaging:Subscriber:Subscriptions:ChannelName is required when Transport=RabbitMQ.");
    }
}
