using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Messaging.CosmosDb.Durability;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using System.Reflection;

namespace BrighterEventing.Messaging.CosmosDb.Configuration;

/// <summary>
/// Cosmos DB outbox/inbox wiring for Brighter producers and consumers.
/// <para>
/// Usage (same pattern as PostgreSQL):
/// <code>
/// // Publisher Program.cs
/// services.AddBrighterEventingPublisherMessaging(config, catalog,
///     CosmosDbServiceCollectionExtensions.CreateProducersConfigurer(services, config), assemblies);
///
/// // Subscriber Program.cs
/// services.AddBrighterEventingSubscriberMessaging(config, catalog,
///     CosmosDbServiceCollectionExtensions.CreateConsumersConfigurer(services, config), assemblies);
/// </code>
/// Or use the convenience wrappers <see cref="AddBrighterEventingPublisherMessagingWithCosmosOutbox"/>
/// and <see cref="AddBrighterEventingSubscriberMessagingWithCosmosInbox"/> to hide the configurer argument.
/// </para>
/// </summary>
public static class CosmosDbServiceCollectionExtensions
{
    // ─── Configurer factories (same shape as PostgreSqlPublisherBrighterSetup / PostgreSqlSubscriberBrighterSetup) ──

    /// <summary>
    /// Returns a delegate that wires the Cosmos DB outbox into Brighter's <c>AddProducers</c>.
    /// Pass it as <c>configureProducersBeforeTransport</c> to
    /// <see cref="BrighterPublisherServiceCollectionExtensions.AddBrighterEventingPublisherMessaging"/>.
    /// </summary>
    public static Action<ProducersConfiguration, BrighterPublisherOptions> CreateProducersConfigurer(
        IServiceCollection services,
        IConfiguration configuration)
    {
        _ = services;
        var options = BrighterPublisherServiceCollectionExtensions.BindPublisherOptions(configuration);

        CosmosBrighterOutbox? outbox = null;
        if (options.ImplementOutbox && options.DatabaseType == DatabaseType.CosmosDb)
            outbox = CreateOutbox(options);

        return (producers, o) =>
        {
            if (!o.ImplementOutbox)
                return;

            if (o.DatabaseType != DatabaseType.CosmosDb)
                throw new InvalidOperationException(
                    $"CosmosDbServiceCollectionExtensions cannot configure outbox for DatabaseType '{o.DatabaseType}'.");

            if (outbox is null)
                throw new InvalidOperationException(
                    "Cosmos outbox was not created. Ensure ImplementOutbox=true and DatabaseType=CosmosDb in config.");

            producers.Outbox = outbox;
            producers.TransactionProvider = typeof(CosmosObjectBoxTransactionProvider);
        };
    }

    /// <summary>
    /// Returns a delegate that wires the Cosmos DB inbox into Brighter's <c>AddConsumers</c>.
    /// Pass it as <c>configureConsumersAfterTransport</c> to
    /// <see cref="BrighterSubscriberServiceCollectionExtensions.AddBrighterEventingSubscriberMessaging"/>.
    /// </summary>
    public static Action<ConsumersOptions, BrighterSubscriberOptions> CreateConsumersConfigurer(
        IServiceCollection services,
        IConfiguration configuration)
    {
        _ = services;
        var options = BrighterSubscriberServiceCollectionExtensions.BindSubscriberOptions(configuration);

        CosmosBrighterInbox? inbox = null;
        if (options.ImplementInbox && options.DatabaseType == DatabaseType.CosmosDb)
            inbox = CreateInbox(options);

        return (consumers, o) =>
        {
            if (!o.ImplementInbox)
                return;

            if (o.DatabaseType != DatabaseType.CosmosDb)
                throw new InvalidOperationException(
                    $"CosmosDbServiceCollectionExtensions cannot configure inbox for DatabaseType '{o.DatabaseType}'.");

            if (inbox is null)
                throw new InvalidOperationException(
                    "Cosmos inbox was not created. Ensure ImplementInbox=true and DatabaseType=CosmosDb in config.");

            consumers.InboxConfiguration = new InboxConfiguration(inbox);
        };
    }

    // ─── Convenience wrappers (one-liner alternative to the explicit pattern above) ──────────────────────────────

    /// <summary>
    /// Registers Brighter producers with Cosmos DB outbox. Equivalent to calling
    /// <see cref="BrighterPublisherServiceCollectionExtensions.AddBrighterEventingPublisherMessaging"/> with
    /// <see cref="CreateProducersConfigurer"/>.
    /// </summary>
    public static IServiceCollection AddBrighterEventingPublisherMessagingWithCosmosOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies)
    {
        var configure = CreateProducersConfigurer(services, configuration);
        return services.AddBrighterEventingPublisherMessaging(configuration, configureEventTypes, configure, autoFromAssemblies);
    }

    /// <summary>
    /// Registers Brighter consumers with Cosmos DB inbox. Equivalent to calling
    /// <see cref="BrighterSubscriberServiceCollectionExtensions.AddBrighterEventingSubscriberMessaging"/> with
    /// <see cref="CreateConsumersConfigurer"/>.
    /// </summary>
    public static IServiceCollection AddBrighterEventingSubscriberMessagingWithCosmosInbox(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies)
    {
        var configure = CreateConsumersConfigurer(services, configuration);
        return services.AddBrighterEventingSubscriberMessaging(configuration, configureEventTypes, configure, autoFromAssemblies);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────────────────────────────────────────

    private static CosmosBrighterOutbox CreateOutbox(BrighterPublisherOptions options)
    {
        var endpoint = options.CosmosDb.Endpoint
            ?? throw new InvalidOperationException("BrighterMessaging:Publisher:CosmosDb:Endpoint is required.");
        var key = options.CosmosDb.Key
            ?? throw new InvalidOperationException("BrighterMessaging:Publisher:CosmosDb:Key is required.");

        var client = new CosmosClient(endpoint, key);
        var db = client.CreateDatabaseIfNotExistsAsync(options.CosmosDb.DatabaseId).GetAwaiter().GetResult();
        db.Database.CreateContainerIfNotExistsAsync(options.CosmosDb.OutboxContainerId, "/id").GetAwaiter().GetResult();
        return new CosmosBrighterOutbox(db.Database.GetContainer(options.CosmosDb.OutboxContainerId));
    }

    private static CosmosBrighterInbox CreateInbox(BrighterSubscriberOptions options)
    {
        var endpoint = options.CosmosDb.Endpoint
            ?? throw new InvalidOperationException("BrighterMessaging:Subscriber:CosmosDb:Endpoint is required.");
        var key = options.CosmosDb.Key
            ?? throw new InvalidOperationException("BrighterMessaging:Subscriber:CosmosDb:Key is required.");

        var client = new CosmosClient(endpoint, key);
        var db = client.CreateDatabaseIfNotExistsAsync(options.CosmosDb.DatabaseId).GetAwaiter().GetResult();
        db.Database.CreateContainerIfNotExistsAsync(options.CosmosDb.InboxContainerId, "/id").GetAwaiter().GetResult();
        return new CosmosBrighterInbox(db.Database.GetContainer(options.CosmosDb.InboxContainerId));
    }
}
