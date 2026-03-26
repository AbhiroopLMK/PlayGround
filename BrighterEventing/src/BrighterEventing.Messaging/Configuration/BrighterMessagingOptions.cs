namespace BrighterEventing.Messaging.Configuration;

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
    public string LgsChannelName { get; set; } = "order.lgs.wrapped.queue";
    public string RabbitInternalChannelName { get; set; } = "rabbit.internal.wrapped.queue";
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
}
