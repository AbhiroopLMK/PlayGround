using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Messaging.CosmosDb.Durability;
using BrighterEventing.Messaging.Events;
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

public static class CosmosDbServiceCollectionExtensions
{
    private const string ConsumerRetryPipelineName = "ConsumerRetryPipeline";

    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] autoFromAssemblies)
    {
        var options = new BrighterPublisherOptions();
        configuration.GetSection(BrighterPublisherOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.Transport))
            options.Transport = configuration["Transport"] ?? BrokerType.RabbitMQ;
        BindFallbackPublisherSettingsFromLegacyConfig(configuration, options);

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
                ConfigureAzureServiceBus(producers, options);
            }
            else
            {
                ConfigureRabbitMq(producers, options);
            }
        })
        .AutoFromAssemblies(BrighterEventingAssemblyRegistration.ResolveAutoFromAssemblies(autoFromAssemblies));

        return services;
    }

    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] autoFromAssemblies)
    {
        var options = new BrighterSubscriberOptions();
        configuration.GetSection(BrighterSubscriberOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.Transport))
            options.Transport = configuration["Transport"] ?? BrokerType.RabbitMQ;
        BindFallbackSubscriberSettingsFromLegacyConfig(configuration, options);

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
        resiliencePipelineRegistry.TryAddBuilder(ConsumerRetryPipelineName, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, options.Consumer.MaxRetryCount),
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, options.Consumer.RequeueDelayMs)),
                BackoffType = DelayBackoffType.Exponential,
            }));

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
                ConfigureAzureServiceBusConsumers(consumers, options, timeout, requeueDelay);
            }
            else
            {
                ConfigureRabbitMqConsumers(consumers, options, timeout, requeueDelay);
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

    private static void ConfigureRabbitMq(dynamic producers, BrighterPublisherOptions options)
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
        [
            new RmqPublication<LgsEnvelopeBrighterEvent> { MakeChannels = OnMissingChannel.Create, Topic = new RoutingKey(MessagingRoutingKeys.LgsWrapped) },
            new RmqPublication<RabbitInternalEnvelopeBrighterEvent> { MakeChannels = OnMissingChannel.Create, Topic = new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped) }
        ]).Create();
    }

    private static void ConfigureAzureServiceBus(dynamic producers, BrighterPublisherOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString))
            throw new InvalidOperationException("Azure Service Bus connection string is required when Transport=AzureServiceBus.");

        var connection = new ServiceBusConnectionStringClientProvider(options.AzureServiceBus.ConnectionString!);
        producers.ProducerRegistry = new BrighterEventing.Messaging.AzureServiceBus.SessionAwareAzureServiceBusProducerRegistryFactory(connection,
        [
            new AzureServiceBusPublication<LgsEnvelopeBrighterEvent> { MakeChannels = OnMissingChannel.Create, Topic = new RoutingKey(MessagingRoutingKeys.LgsWrapped) },
            new AzureServiceBusPublication<RabbitInternalEnvelopeBrighterEvent> { MakeChannels = OnMissingChannel.Create, Topic = new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped) }
        ]).Create();
    }

    private static void ConfigureAzureServiceBusConsumers(ConsumersOptions consumers, BrighterSubscriberOptions options, TimeSpan timeout, TimeSpan? requeueDelay)
    {
        if (string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString))
            throw new InvalidOperationException("Azure Service Bus connection string is required when Transport=AzureServiceBus.");

        var clientProvider = new ServiceBusConnectionStringClientProvider(options.AzureServiceBus.ConnectionString!);
        var subscriptionConfiguration = new AzureServiceBusSubscriptionConfiguration
        {
            MaxDeliveryCount = options.AzureServiceBus.MaxDeliveryCount,
            LockDuration = TimeSpan.FromSeconds(options.AzureServiceBus.LockDurationSeconds),
            DeadLetteringOnMessageExpiration = options.AzureServiceBus.DeadLetteringOnMessageExpiration,
            RequireSession = true,
            DefaultMessageTimeToLive = options.AzureServiceBus.DefaultMessageTimeToLiveDays > 0 ? TimeSpan.FromDays(options.AzureServiceBus.DefaultMessageTimeToLiveDays) : TimeSpan.FromMinutes(1)
        };
        consumers.Subscriptions = new Subscription[]
        {
            new AzureServiceBusSubscription<LgsEnvelopeBrighterEvent>(new SubscriptionName(options.AzureServiceBus.SubscriptionName), new ChannelName(options.AzureServiceBus.SubscriptionName), new RoutingKey(MessagingRoutingKeys.LgsWrapped), timeOut: timeout, makeChannels: OnMissingChannel.Create, requeueCount: options.Consumer.MaxRetryCount, requeueDelay: requeueDelay, messagePumpType: MessagePumpType.Proactor, subscriptionConfiguration: subscriptionConfiguration),
            new AzureServiceBusSubscription<RabbitInternalEnvelopeBrighterEvent>(new SubscriptionName(options.AzureServiceBus.SubscriptionName), new ChannelName(options.AzureServiceBus.SubscriptionName), new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped), timeOut: timeout, makeChannels: OnMissingChannel.Create, requeueCount: options.Consumer.MaxRetryCount, requeueDelay: requeueDelay, messagePumpType: MessagePumpType.Proactor, subscriptionConfiguration: subscriptionConfiguration)
        };
        consumers.DefaultChannelFactory = new AzureServiceBusChannelFactory(new AzureServiceBusConsumerFactory(clientProvider));
    }

    private static void ConfigureRabbitMqConsumers(ConsumersOptions consumers, BrighterSubscriberOptions options, TimeSpan timeout, TimeSpan? requeueDelay)
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
        consumers.Subscriptions = new Subscription[]
        {
            new RmqSubscription<LgsEnvelopeBrighterEvent>(new SubscriptionName(options.RabbitMQ.SubscriptionName), new ChannelName(options.RabbitMQ.LgsChannelName), new RoutingKey(MessagingRoutingKeys.LgsWrapped), timeOut: timeout, makeChannels: OnMissingChannel.Create, requeueCount: options.Consumer.MaxRetryCount, requeueDelay: requeueDelay, messagePumpType: MessagePumpType.Proactor),
            new RmqSubscription<RabbitInternalEnvelopeBrighterEvent>(new SubscriptionName(options.RabbitMQ.SubscriptionName), new ChannelName(options.RabbitMQ.RabbitInternalChannelName), new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped), timeOut: timeout, makeChannels: OnMissingChannel.Create, requeueCount: options.Consumer.MaxRetryCount, requeueDelay: requeueDelay, messagePumpType: MessagePumpType.Proactor)
        };
        consumers.DefaultChannelFactory = new ChannelFactory(new RmqMessageConsumerFactory(connection));
    }

    private static void BindFallbackPublisherSettingsFromLegacyConfig(IConfiguration configuration, BrighterPublisherOptions options)
    {
        options.Transport = configuration["Transport"] ?? options.Transport;
        options.RabbitMQ.AmqpUri ??= configuration["RabbitMQ:AmqpUri"];
        options.RabbitMQ.HostName ??= configuration["RabbitMqSettings:HostName"];
        options.RabbitMQ.Port ??= configuration["RabbitMqSettings:Port"];
        options.RabbitMQ.Username ??= configuration["RabbitMqSettings:Username"];
        options.RabbitMQ.Password ??= configuration["RabbitMqSettings:Password"];
        options.RabbitMQ.ClientProvidedName ??= configuration["RabbitMqSettings:ClientProvidedName"];
        options.RabbitMQ.Exchange = configuration["RabbitMQ:Exchange"] ?? options.RabbitMQ.Exchange;
        options.AzureServiceBus.ConnectionString ??= configuration["AzureServiceBus:ConnectionString"];
    }

    private static void BindFallbackSubscriberSettingsFromLegacyConfig(IConfiguration configuration, BrighterSubscriberOptions options)
    {
        options.Transport = configuration["Transport"] ?? options.Transport;
        options.Consumer.MaxRetryCount = ReadInt(configuration["Messaging:Consumer:MaxRetryCount"], options.Consumer.MaxRetryCount);
        options.Consumer.RequeueDelayMs = ReadInt(configuration["Messaging:Consumer:RequeueDelayMs"], options.Consumer.RequeueDelayMs);
        options.Consumer.ReceiveTimeoutMs = ReadInt(configuration["Messaging:Consumer:ReceiveTimeoutMs"], options.Consumer.ReceiveTimeoutMs);
        options.AzureServiceBus.ConnectionString ??= configuration["AzureServiceBus:ConnectionString"];
        options.AzureServiceBus.SubscriptionName = configuration["AzureServiceBus:SubscriptionName"] ?? options.AzureServiceBus.SubscriptionName;
        options.RabbitMQ.AmqpUri ??= configuration["RabbitMQ:AmqpUri"];
        options.RabbitMQ.HostName ??= configuration["RabbitMqSettings:HostName"];
        options.RabbitMQ.Port ??= configuration["RabbitMqSettings:Port"];
        options.RabbitMQ.Username ??= configuration["RabbitMqSettings:Username"];
        options.RabbitMQ.Password ??= configuration["RabbitMqSettings:Password"];
        options.RabbitMQ.Exchange = configuration["RabbitMQ:Exchange"] ?? options.RabbitMQ.Exchange;
        options.RabbitMQ.SubscriptionName = configuration["RabbitMQ:SubscriptionName"] ?? options.RabbitMQ.SubscriptionName;
        options.RabbitMQ.ClientProvidedName ??= configuration["RabbitMqSettings:ClientProvidedName"];
    }

    private static int ReadInt(string? raw, int fallback) => int.TryParse(raw, out var i) ? i : fallback;
}
