using BrighterEventing.Messaging.AzureServiceBus;
using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Messaging.CosmosDb.Durability;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;
using Polly.Retry;
using System.Reflection;

namespace BrighterEventing.Messaging.CosmosDb.Configuration;

/// <summary>
/// Brighter registration for hosts that use <strong>Cosmos DB</strong> for outbox/inbox instead of PostgreSQL.
/// Transport (RabbitMQ / Azure Service Bus) and publication/subscription wiring match
/// <see cref="BrighterPublisherServiceCollectionExtensions"/> / <see cref="BrighterSubscriberServiceCollectionExtensions"/>;
/// only durability storage differs.
/// </summary>
public static class CosmosDbServiceCollectionExtensions
{
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies)
    {
        var builder = new EventTypeCatalogBuilder();
        configureEventTypes(builder);
        var registry = builder.Build();
        services.AddSingleton<IEventTypeRegistry>(registry);
        return services.AddBrighterEventingPublisherMessaging(configuration, registry, autoFromAssemblies);
    }

    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        params Assembly[] autoFromAssemblies)
    {
        var options = BrighterPublisherServiceCollectionExtensions.BindPublisherOptions(configuration);

        var outbox = options.ImplementOutbox && options.DatabaseType == DatabaseType.CosmosDb
            ? CreateOutbox(options)
            : null;

        var resiliencePipelineRegistry = new ResiliencePipelineRegistry<string>();
        resiliencePipelineRegistry.TryAddBuilder(CommandProcessor.OutboxProducer, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, options.Retry.OutboxProducerMaxRetryAttempts),
                Delay = TimeSpan.Zero,
            }));

        services.AddBrighter(brighterOptions =>
        {
            brighterOptions.HandlerLifetime = ServiceLifetime.Scoped;
            brighterOptions.MapperLifetime = ServiceLifetime.Singleton;
            brighterOptions.ResiliencePipelineRegistry = resiliencePipelineRegistry;
        })
        .AddProducers(producers =>
        {
            if (outbox != null) producers.Outbox = outbox;

            if (options.Transport == BrokerType.AzureServiceBus)
            {
                ConfigureAzureServiceBus(producers, options, eventTypeRegistry);
            }
            else
            {
                ConfigureRabbitMq(producers, options, eventTypeRegistry);
            }
        })
        .AutoFromAssemblies(BrighterEventingAssemblyRegistration.ResolveAutoFromAssemblies(autoFromAssemblies));

        return services;
    }

    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies)
    {
        var builder = new EventTypeCatalogBuilder();
        configureEventTypes(builder);
        var registry = builder.Build();
        services.AddSingleton<IEventTypeRegistry>(registry);
        return services.AddBrighterEventingSubscriberMessaging(configuration, registry, autoFromAssemblies);
    }

    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        params Assembly[] autoFromAssemblies)
    {
        var options = BrighterSubscriberServiceCollectionExtensions.BindSubscriberOptions(configuration);

        var inbox = options.ImplementInbox && options.DatabaseType == DatabaseType.CosmosDb
            ? CreateInbox(options)
            : null;

        var timeout = TimeSpan.FromMilliseconds(options.Consumer.ReceiveTimeoutMs);
        var requeueDelay = options.Consumer.RequeueDelayMs > 0
            ? TimeSpan.FromMilliseconds(options.Consumer.RequeueDelayMs)
            : (TimeSpan?)null;

        var resiliencePipelineRegistry = new ResiliencePipelineRegistry<string>();
        resiliencePipelineRegistry.TryAddBuilder(CommandProcessor.OutboxProducer, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, options.Retry.OutboxProducerMaxRetryAttempts),
                Delay = TimeSpan.Zero,
            }));
        BrighterSubscriberResilienceRegistration.RegisterConsumerRetryPipelinesForEventTypes(
            resiliencePipelineRegistry,
            options,
            eventTypeRegistry);

        services.AddBrighter(brighterOptions =>
        {
            brighterOptions.HandlerLifetime = ServiceLifetime.Scoped;
            brighterOptions.MapperLifetime = ServiceLifetime.Singleton;
            brighterOptions.ResiliencePipelineRegistry = resiliencePipelineRegistry;
        })
        .AutoFromAssemblies(BrighterEventingAssemblyRegistration.ResolveAutoFromAssemblies(autoFromAssemblies));

        services.AddConsumers(consumers =>
        {
            if (options.Transport == BrokerType.AzureServiceBus)
            {
                ConfigureAzureServiceBusConsumers(services, consumers, options, eventTypeRegistry, timeout, requeueDelay);
            }
            else
            {
                ConfigureRabbitMqConsumers(consumers, options, eventTypeRegistry, timeout, requeueDelay);
            }

            if (inbox != null) consumers.InboxConfiguration = new InboxConfiguration(inbox);
        });

        return services;
    }

    private static CosmosBrighterOutbox CreateOutbox(BrighterPublisherOptions options)
    {
        var endpoint = options.CosmosDb.Endpoint ?? throw new InvalidOperationException("BrighterMessaging:Publisher:CosmosDb:Endpoint is required.");
        var key = options.CosmosDb.Key ?? throw new InvalidOperationException("BrighterMessaging:Publisher:CosmosDb:Key is required.");
        var client = new CosmosClient(endpoint, key);
        var db = client.CreateDatabaseIfNotExistsAsync(options.CosmosDb.DatabaseId).GetAwaiter().GetResult();
        db.Database.CreateContainerIfNotExistsAsync(options.CosmosDb.OutboxContainerId, "/id").GetAwaiter().GetResult();
        var container = db.Database.GetContainer(options.CosmosDb.OutboxContainerId);
        return new CosmosBrighterOutbox(container);
    }

    private static CosmosBrighterInbox CreateInbox(BrighterSubscriberOptions options)
    {
        var endpoint = options.CosmosDb.Endpoint ?? throw new InvalidOperationException("BrighterMessaging:Subscriber:CosmosDb:Endpoint is required.");
        var key = options.CosmosDb.Key ?? throw new InvalidOperationException("BrighterMessaging:Subscriber:CosmosDb:Key is required.");
        var client = new CosmosClient(endpoint, key);
        var db = client.CreateDatabaseIfNotExistsAsync(options.CosmosDb.DatabaseId).GetAwaiter().GetResult();
        db.Database.CreateContainerIfNotExistsAsync(options.CosmosDb.InboxContainerId, "/id").GetAwaiter().GetResult();
        var container = db.Database.GetContainer(options.CosmosDb.InboxContainerId);
        return new CosmosBrighterInbox(container);
    }

    private static void ConfigureRabbitMq(dynamic producers, BrighterPublisherOptions options, IEventTypeRegistry eventTypeRegistry)
    {
        var amqpUri = RabbitMqAmqpUri.Resolve(
            options.RabbitMQ.AmqpUri,
            options.RabbitMQ.HostName,
            options.RabbitMQ.Port,
            options.RabbitMQ.Username,
            options.RabbitMQ.Password);

        var connection = new RmqMessagingGatewayConnection
        {
            AmpqUri = new AmqpUriSpecification(new Uri(amqpUri)),
            Exchange = new Exchange(options.RabbitMQ.Exchange)
        };
        if (!string.IsNullOrWhiteSpace(options.RabbitMQ.ClientProvidedName))
            connection.Name = options.RabbitMQ.ClientProvidedName;

        producers.ProducerRegistry = new RmqProducerRegistryFactory(connection,
            BrighterMessagingBrokerRegistration.BuildRmqPublications(options, eventTypeRegistry)).Create();
    }

    private static void ConfigureAzureServiceBus(dynamic producers, BrighterPublisherOptions options, IEventTypeRegistry eventTypeRegistry)
    {
        if (string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString))
            throw new InvalidOperationException("Azure Service Bus connection string is required when Transport=AzureServiceBus.");

        var connection = new ServiceBusConnectionStringClientProvider(options.AzureServiceBus.ConnectionString!);
        producers.ProducerRegistry = new BrighterEventing.Messaging.AzureServiceBus.SessionAwareAzureServiceBusProducerRegistryFactory(connection,
            BrighterMessagingBrokerRegistration.BuildAzureServiceBusPublications(options, eventTypeRegistry)).Create();
    }

    private static void ConfigureAzureServiceBusConsumers(
        IServiceCollection services,
        ConsumersOptions consumers,
        BrighterSubscriberOptions options,
        IEventTypeRegistry eventTypeRegistry,
        TimeSpan timeout,
        TimeSpan? requeueDelay)
    {
        if (string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString))
            throw new InvalidOperationException("Azure Service Bus connection string is required when Transport=AzureServiceBus.");

        var clientProvider = new ServiceBusConnectionStringClientProvider(options.AzureServiceBus.ConnectionString!);
        var subscriptionConfiguration = new AzureServiceBusSubscriptionConfiguration
        {
            MaxDeliveryCount = options.AzureServiceBus.MaxDeliveryCount,
            LockDuration = TimeSpan.FromSeconds(options.AzureServiceBus.LockDurationSeconds),
            DeadLetteringOnMessageExpiration = options.AzureServiceBus.DeadLetteringOnMessageExpiration,
            RequireSession = options.AzureServiceBus.RequireSession,
            DefaultMessageTimeToLive = options.AzureServiceBus.DefaultMessageTimeToLiveDays > 0 ? TimeSpan.FromDays(options.AzureServiceBus.DefaultMessageTimeToLiveDays) : TimeSpan.FromMinutes(1)
        };
        consumers.Subscriptions = BrighterMessagingBrokerRegistration.BuildAzureServiceBusSubscriptions(
            options,
            eventTypeRegistry,
            timeout,
            requeueDelay,
            subscriptionConfiguration);
        consumers.DefaultChannelFactory = new AzureServiceBusChannelFactory(new AzureServiceBusConsumerFactory(clientProvider));

        services.AddHostedService<AzureServiceBusCorrelationRulesHostedService>();
    }

    private static void ConfigureRabbitMqConsumers(
        ConsumersOptions consumers,
        BrighterSubscriberOptions options,
        IEventTypeRegistry eventTypeRegistry,
        TimeSpan timeout,
        TimeSpan? requeueDelay)
    {
        var amqpUri = RabbitMqAmqpUri.Resolve(
            options.RabbitMQ.AmqpUri,
            options.RabbitMQ.HostName,
            options.RabbitMQ.Port,
            options.RabbitMQ.Username,
            options.RabbitMQ.Password);
        var connection = new RmqMessagingGatewayConnection
        {
            AmpqUri = new AmqpUriSpecification(new Uri(amqpUri)),
            Exchange = new Exchange(options.RabbitMQ.Exchange)
        };
        if (!string.IsNullOrWhiteSpace(options.RabbitMQ.ClientProvidedName))
            connection.Name = options.RabbitMQ.ClientProvidedName;
        consumers.Subscriptions = BrighterMessagingBrokerRegistration.BuildRmqSubscriptions(
            options,
            eventTypeRegistry,
            timeout,
            requeueDelay);
        consumers.DefaultChannelFactory = new ChannelFactory(new RmqMessageConsumerFactory(connection));
    }
}
