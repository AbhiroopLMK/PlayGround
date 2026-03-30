namespace BrighterEventing.Messaging.Configuration;

// Options bind from BrighterMessaging:Publisher / BrighterMessaging:Subscriber (see SectionName on each options type).

/// <summary>
/// One publisher registration: domain event type plus broker-specific routing.
/// For RabbitMQ, <see cref="RoutingKey"/> is the exchange routing key. For Azure Service Bus,
/// <see cref="Topic"/> is the Service Bus topic entity path when set; <see cref="RoutingKey"/> is the
/// CloudEvents subject (and subscription filter). When <see cref="Topic"/> is empty for ASB, <see cref="RoutingKey"/>
/// is used for both the topic path and subject (legacy).
/// </summary>
public sealed class PublicationBinding
{
    /// <summary>Domain event, e.g. <c>OrderCreatedEvent</c> or <c>OrderCreated</c>.</summary>
    public string EventType { get; set; } = "";

    /// <summary>
    /// RabbitMQ: exchange routing key. Azure Service Bus: subject when <see cref="Topic"/> is set; otherwise topic path and subject.
    /// </summary>
    public string RoutingKey { get; set; } = "";

    /// <summary>
    /// Azure Service Bus only: topic entity path (Brighter <c>Publication.Topic</c>). Ignored for RabbitMQ.
    /// </summary>
    public string Topic { get; set; } = "";

}

/// <summary>
/// One consumer subscription: domain event type, routing/subject, and broker-specific names.
/// For Azure Service Bus, multiple handlers can share the same <see cref="Topic"/> (Service Bus topic) and differ by
/// <see cref="RoutingKey"/> (subject filter). Each binding still uses its own <see cref="SubscriptionName"/> (subscription entity under the topic).
/// </summary>
public sealed class SubscriptionBinding
{
    /// <summary>Domain event, e.g. <c>OrderCreatedEvent</c> or <c>OrderCreated</c>.</summary>
    public string EventType { get; set; } = "";

    /// <summary>
    /// RabbitMQ: routing key. Azure Service Bus: used when <see cref="AzureServiceBusFilterRules"/> is empty to build a
    /// single correlation condition on the <c>cloudEvents:subject</c> application property (must match the publisher).
    /// When you declare rules explicitly, use <see cref="AsbFilterPropertyKind.System"/> with <c>PropertyName</c>
    /// <c>subject</c> for the broker subject, or <see cref="AsbFilterPropertyKind.Custom"/> with
    /// <c>cloudEvents:subject</c> for the CloudEvents app property.
    /// </summary>
    public string RoutingKey { get; set; } = "";

    /// <summary>
    /// Azure Service Bus only: Service Bus <strong>topic</strong> entity path (passed to Brighter as the subscription routing key / topic).
    /// When empty, <see cref="ChannelName"/> is used as the topic path. Ignored for RabbitMQ.
    /// </summary>
    public string Topic { get; set; } = "";

    /// <summary>
    /// RabbitMQ: subscription name. Azure Service Bus: the <strong>subscription</strong> name under the topic (not the topic name).
    /// Maps to Brighter <c>ChannelName</c> for ASB. When empty, uses <see cref="AzureServiceBusSubscriberOptions.SubscriptionName"/>.
    /// </summary>
    public string SubscriptionName { get; set; } = "";

    /// <summary>
    /// RabbitMQ: queue name. Azure Service Bus: legacy fallback for the topic path when <see cref="Topic"/> is empty.
    /// When both <see cref="Topic"/> and <see cref="ChannelName"/> are empty for ASB, defaults to <see cref="SubscriptionName"/>.
    /// </summary>
    public string ChannelName { get; set; } = "";

    /// <summary>
    /// Azure Service Bus only: correlation rules (OR across rules; AND within each rule’s <see cref="AsbSubscriptionFilterCondition"/>).
    /// Each condition uses <see cref="AsbFilterPropertyKind.System"/> (broker field via <see cref="AsbSubscriptionFilterCondition.PropertyName"/>)
    /// or <see cref="AsbFilterPropertyKind.Custom"/> (application property). When empty, defaults to one custom condition on
    /// <c>cloudEvents:subject</c> using <see cref="RoutingKey"/> as the value.
    /// </summary>
    public List<AsbSubscriptionFilterRule>? AzureServiceBusFilterRules { get; set; }
}

public static class BrokerType
{
    public const string RabbitMQ = "RabbitMQ";
    public const string AzureServiceBus = "AzureServiceBus";
}

public static class DatabaseType
{
    public const string None = "None";
    public const string PostgreSql = "PostgreSql";
    public const string CosmosDb = "CosmosDb";
}

public sealed class BrighterPublisherOptions
{
    public const string SectionName = "BrighterMessaging:Publisher";

    public string Transport { get; set; } = BrokerType.RabbitMQ;
    public bool ImplementOutbox { get; set; } = true;
    public string DatabaseType { get; set; } = Configuration.DatabaseType.PostgreSql;
    public string DatabaseName { get; set; } = "neondb";
    public string OutboxTableName { get; set; } = "Outbox";
    public string OutboxConnectionStringName { get; set; } = "BrighterOutbox";
    public string? ConnectionProviderType { get; set; } = "Paramore.Brighter.PostgreSql.PostgreSqlConnectionProvider, Paramore.Brighter.PostgreSql";
    public string? TransactionProviderType { get; set; }

    public RabbitPublisherOptions RabbitMQ { get; set; } = new();
    public AzureServiceBusPublisherOptions AzureServiceBus { get; set; } = new();
    public CosmosDbOptions CosmosDb { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Application-defined publications. Multiple entries per event type are allowed (e.g. different ASB subjects on the same topic).
    /// </summary>
    public List<PublicationBinding> Publications { get; set; } = new();
}

public sealed class BrighterSubscriberOptions
{
    public const string SectionName = "BrighterMessaging:Subscriber";

    public string Transport { get; set; } = BrokerType.RabbitMQ;
    public bool ImplementInbox { get; set; } = true;
    public string DatabaseType { get; set; } = Configuration.DatabaseType.PostgreSql;
    public string DatabaseName { get; set; } = "neondb";
    public string InboxTableName { get; set; } = "Inbox";
    public string InboxConnectionStringName { get; set; } = "BrighterInbox";

    public ConsumerRetryOptions Consumer { get; set; } = new();
    public RabbitSubscriberOptions RabbitMQ { get; set; } = new();
    public AzureServiceBusSubscriberOptions AzureServiceBus { get; set; } = new();
    public CosmosDbOptions CosmosDb { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Application-defined subscriptions (routing keys, queue/channel names). Multiple entries per event type are allowed.
    /// </summary>
    public List<SubscriptionBinding> Subscriptions { get; set; } = new();
}

public sealed class CosmosDbOptions
{
    public string? Endpoint { get; set; }
    public string? Key { get; set; }
    public string DatabaseId { get; set; } = "brighter-eventing";
    public string OutboxContainerId { get; set; } = "outbox";
    public string InboxContainerId { get; set; } = "inbox";
}

public sealed class RetryOptions
{
    public int OutboxProducerMaxRetryAttempts { get; set; } = 1;
}

public sealed class ConsumerRetryOptions
{
    public int MaxRetryCount { get; set; } = 3;
    public int RequeueDelayMs { get; set; } = 5000;
    public int ReceiveTimeoutMs { get; set; } = 400;
}

public sealed class RabbitPublisherOptions
{
    public string? AmqpUri { get; set; }
    public string? HostName { get; set; }
    public string? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Exchange { get; set; } = "brighter.eventing.exchange";
    public string? ClientProvidedName { get; set; }
}

public sealed class RabbitSubscriberOptions
{
    public string? AmqpUri { get; set; }
    public string? HostName { get; set; }
    public string? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Exchange { get; set; } = "brighter.eventing.exchange";
    public string SubscriptionName { get; set; } = "brighter-eventing-subscriber";
    public string? ClientProvidedName { get; set; }
}

public sealed class AzureServiceBusPublisherOptions
{
    public string? ConnectionString { get; set; }
}

public sealed class AzureServiceBusSubscriberOptions
{
    public string? ConnectionString { get; set; }
    public string SubscriptionName { get; set; } = "brighter-eventing-sub";
    public int MaxDeliveryCount { get; set; } = 5;
    public int LockDurationSeconds { get; set; } = 60;
    public bool DeadLetteringOnMessageExpiration { get; set; } = true;
    public int DefaultMessageTimeToLiveDays { get; set; } = 3;

    /// <summary>
    /// When true, Brighter uses session receivers (<c>AcceptNextSessionAsync</c>); Azure subscriptions must be
    /// session-enabled and messages must carry a session id. Default true.
    /// </summary>
    public bool RequireSession { get; set; } = true;
}
