using System.Collections.Generic;
using System.Reflection;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Maps <c>BrighterMessaging:*:Publications</c> / <c>Subscriptions</c> to Brighter's transport types.
/// Azure Service Bus: <see cref="PublicationBinding.Topic"/> is the Service Bus topic path;
/// <see cref="PublicationBinding.RoutingKey"/> is the CloudEvents subject; each subscription gets a SQL rule on
/// <c>[cloudEvents:subject]</c> (see <see cref="BrighterCloudEventsSubjectApplicationPropertyKey"/>).
/// </summary>
public static class BrighterMessagingBrokerRegistration
{
    /// <summary>
    /// Brighter's ASB producer maps <see cref="MessageHeader.Subject"/> to this application property (Paramore
    /// <c>cloudEvents:subject</c>), not to the broker's native <c>Subject</c> field on the Service Bus message.
    /// </summary>
    internal const string BrighterCloudEventsSubjectApplicationPropertyKey = "cloudEvents:subject";

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

    /// <remarks>
    /// Brighter's Azure Service Bus producer registry keys on <c>(topic, CloudEvents type)</c>, not CLR type.
    /// Each <see cref="AzureServiceBusPublication{T}"/> sets <see cref="Publication.Type"/> from the event class so
    /// multiple domain events can share the same Service Bus topic. The registry still allows only one row per
    /// <c>(topic, event CLR type)</c> when subject differs; duplicate config rows are merged (first subject wins);
    /// use <see cref="GetRegisteredRoutingKeysForEventType"/> and <c>PublishRoutingKey</c> for other subjects.
    /// </remarks>
    public static AzureServiceBusPublication[] BuildAzureServiceBusPublications(
        BrighterPublisherOptions options,
        IEventTypeRegistry registry)
    {
        ValidatePublications(options, registry);
        var seen = new HashSet<(Type EventType, string TopicPath)>();
        var list = new List<AzureServiceBusPublication>();
        foreach (var p in options.Publications)
        {
            var eventClrType = registry.Resolve(p.EventType);
            var path = GetAzureServiceBusPublicationTopicPath(p);
            if (!seen.Add((eventClrType, path)))
                continue;

            var topicPath = new RoutingKey(path);
            var subject = GetAzureServiceBusPublicationSubject(p);
            var cloudEventsType = registry.GetCloudEventsType(eventClrType);
            list.Add(InvokeAddAzureServiceBusPublication(eventClrType, topicPath, subject, cloudEventsType));
        }

        return list.ToArray();
    }

    /// <summary>
    /// ASB topic entity path: explicit <see cref="PublicationBinding.Topic"/> or <see cref="PublicationBinding.RoutingKey"/> when Topic is unset.
    /// </summary>
    public static string GetAzureServiceBusPublicationTopicPath(PublicationBinding p) =>
        !string.IsNullOrWhiteSpace(p.Topic) ? p.Topic.Trim() : p.RoutingKey.Trim();

    /// <summary>
    /// CloudEvents subject: <see cref="PublicationBinding.RoutingKey"/> (matches subscription subject filter).
    /// </summary>
    public static string GetAzureServiceBusPublicationSubject(PublicationBinding p) => p.RoutingKey.Trim();

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
            var subscriptionEntityName = ResolveSubscriptionName(s, options);
            var topicPath = ResolveChannelName(s, options);
            var topicRoutingKey = new RoutingKey(topicPath);
            var eventClrType = registry.Resolve(s.EventType);
            var subConfig = CloneAsbSubscriptionConfigurationWithSubjectFilter(
                subscriptionConfiguration,
                s.RoutingKey.Trim());
            list.Add(InvokeCreateAzureServiceBusSubscription(
                eventClrType,
                subscriptionEntityName,
                topicPath,
                topicRoutingKey,
                timeout,
                options,
                requeueDelay,
                subConfig));
        }

        return list.ToArray();
    }

    private static AzureServiceBusSubscriptionConfiguration CloneAsbSubscriptionConfigurationWithSubjectFilter(
        AzureServiceBusSubscriptionConfiguration template,
        string cloudEventsSubject)
    {
        var escaped = cloudEventsSubject.Replace("'", "''", StringComparison.Ordinal);
        // SQL rule: user properties are referenced by name, not sys.ApplicationProperties[...] (that path is invalid on the broker).
        // Brackets are required when the name contains ':' — see https://learn.microsoft.com/azure/service-bus-messaging/topic-filters-sql-syntax
        var prop = BrighterCloudEventsSubjectApplicationPropertyKey;
        return new AzureServiceBusSubscriptionConfiguration
        {
            MaxDeliveryCount = template.MaxDeliveryCount,
            DeadLetteringOnMessageExpiration = template.DeadLetteringOnMessageExpiration,
            LockDuration = template.LockDuration,
            DefaultMessageTimeToLive = template.DefaultMessageTimeToLive,
            QueueIdleBeforeDelete = template.QueueIdleBeforeDelete,
            RequireSession = template.RequireSession,
            UseServiceBusQueue = template.UseServiceBusQueue,
            SqlFilter = $"[{prop}] = '{escaped}'",
        };
    }

    private static RmqPublication InvokeAddRmqPublication(Type eventType, RoutingKey rk)
    {
        var m = AddRmqPublicationForEventMethod.MakeGenericMethod(eventType);
        return (RmqPublication)m.Invoke(null, new object[] { rk })!;
    }

    private static AzureServiceBusPublication InvokeAddAzureServiceBusPublication(
        Type eventType,
        RoutingKey topicPath,
        string subject,
        CloudEventsType cloudEventsType)
    {
        var m = AddAzureServiceBusPublicationForEventMethod.MakeGenericMethod(eventType);
        return (AzureServiceBusPublication)m.Invoke(null, new object[] { topicPath, subject, cloudEventsType })!;
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
        string subscriptionName,
        string topicPath,
        RoutingKey rk,
        TimeSpan timeout,
        BrighterSubscriberOptions options,
        TimeSpan? requeueDelay,
        AzureServiceBusSubscriptionConfiguration subscriptionConfiguration)
    {
        var m = CreateAzureServiceBusSubscriptionForEventMethod.MakeGenericMethod(eventType);
        return (Subscription)m.Invoke(null, new object?[]
        {
            subscriptionName,
            topicPath,
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

    private static AzureServiceBusPublication AddAzureServiceBusPublicationForEvent<T>(
        RoutingKey topicPath,
        string subject,
        CloudEventsType cloudEventsType) where T : Event =>
        new AzureServiceBusPublication<T>
        {
            MakeChannels = OnMissingChannel.Create,
            Topic = topicPath,
            Subject = subject,
            Type = cloudEventsType
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

    /// <remarks>
    /// Brighter's ASB topic consumer uses the subscription's routing key as the Service Bus topic path, channel name as
    /// the subscription entity under that topic, and Brighter's subscription name for identification (see Paramore.Brighter
    /// <c>AzureServiceBusTopicConsumer</c>). Config <see cref="SubscriptionBinding.RoutingKey"/> is the CloudEvents
    /// subject and is applied via SQL filter on the subscription, not as the topic name.
    /// </remarks>
    private static Subscription CreateAzureServiceBusSubscriptionForEvent<T>(
        string subscriptionEntityName,
        string topicPath,
        RoutingKey topicRoutingKey,
        TimeSpan timeout,
        BrighterSubscriberOptions options,
        TimeSpan? requeueDelay,
        AzureServiceBusSubscriptionConfiguration subscriptionConfiguration) where T : Event =>
        new AzureServiceBusSubscription<T>(
            new SubscriptionName(topicPath),
            new ChannelName(subscriptionEntityName),
            topicRoutingKey,
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
        if (options.Transport == BrokerType.AzureServiceBus)
        {
            if (!string.IsNullOrWhiteSpace(s.Topic))
                return s.Topic.Trim();
            if (!string.IsNullOrWhiteSpace(s.ChannelName))
                return s.ChannelName.Trim();
            return ResolveSubscriptionName(s, options);
        }

        if (!string.IsNullOrWhiteSpace(s.ChannelName))
            return s.ChannelName.Trim();

        throw new InvalidOperationException(
            "BrighterMessaging:Subscriber:Subscriptions:ChannelName is required when Transport=RabbitMQ.");
    }
}
