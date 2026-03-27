using BrighterEventing.Messaging.AzureServiceBus;
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

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Wires Paramore Brighter <c>AddConsumers</c> for RabbitMQ or Azure Service Bus. Optional inbox callback runs after
/// transport (from the PostgreSQL or Cosmos hosting package). Consumer retry uses Polly pipelines per event type.
/// </summary>
public static class BrighterSubscriberServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="BrighterSubscriberOptions"/> from configuration (including legacy fallback keys).
    /// </summary>
    public static BrighterSubscriberOptions BindSubscriberOptions(IConfiguration configuration)
    {
        var options = new BrighterSubscriberOptions();
        configuration.GetSection(BrighterSubscriberOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.Transport))
            options.Transport = configuration["Transport"] ?? BrokerType.RabbitMQ;
        BindFallbackSubscriberSettingsFromLegacyConfig(configuration, options);
        return options;
    }

    /// <summary>Registers Brighter consumers. Registers <see cref="IEventTypeRegistry"/> from <paramref name="configureEventTypes"/>.</summary>
    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies) =>
        services.AddBrighterEventingSubscriberMessaging(
            configuration,
            configureEventTypes,
            configureConsumersAfterTransport: null,
            autoFromAssemblies);

    /// <summary>Registers Brighter consumers; optional callback runs after transport wiring inside <c>AddConsumers</c> (e.g. PostgreSQL inbox).</summary>
    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        Action<ConsumersOptions, BrighterSubscriberOptions>? configureConsumersAfterTransport,
        params Assembly[] autoFromAssemblies)
    {
        var builder = new EventTypeCatalogBuilder();
        configureEventTypes(builder);
        var registry = builder.Build();
        services.AddSingleton<IEventTypeRegistry>(registry);
        return services.AddBrighterEventingSubscriberMessaging(
            configuration,
            registry,
            configureConsumersAfterTransport,
            autoFromAssemblies);
    }

    /// <summary>Registers Brighter consumers using an existing <see cref="IEventTypeRegistry"/>.</summary>
    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        params Assembly[] autoFromAssemblies) =>
        services.AddBrighterEventingSubscriberMessaging(
            configuration,
            eventTypeRegistry,
            configureConsumersAfterTransport: null,
            autoFromAssemblies);

    /// <summary>Registers Brighter consumers; optional callback runs after transport wiring inside <c>AddConsumers</c> (e.g. PostgreSQL inbox).</summary>
    public static IServiceCollection AddBrighterEventingSubscriberMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        Action<ConsumersOptions, BrighterSubscriberOptions>? configureConsumersAfterTransport,
        params Assembly[] autoFromAssemblies)
    {
        var options = BindSubscriberOptions(configuration);

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

            configureConsumersAfterTransport?.Invoke(consumers, options);
        });

        return services;
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
            DefaultMessageTimeToLive = options.AzureServiceBus.DefaultMessageTimeToLiveDays > 0
                ? TimeSpan.FromDays(options.AzureServiceBus.DefaultMessageTimeToLiveDays)
                : TimeSpan.FromMinutes(1)
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
        dynamic consumers,
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

    private static void BindFallbackSubscriberSettingsFromLegacyConfig(IConfiguration configuration, BrighterSubscriberOptions options)
    {
        options.Transport = configuration["Transport"] ?? options.Transport;
        options.Consumer.MaxRetryCount = ReadInt(configuration["Messaging:Consumer:MaxRetryCount"], options.Consumer.MaxRetryCount);
        options.Consumer.RequeueDelayMs = ReadInt(configuration["Messaging:Consumer:RequeueDelayMs"], options.Consumer.RequeueDelayMs);
        options.Consumer.ReceiveTimeoutMs = ReadInt(configuration["Messaging:Consumer:ReceiveTimeoutMs"], options.Consumer.ReceiveTimeoutMs);

        options.AzureServiceBus.ConnectionString ??= configuration["AzureServiceBus:ConnectionString"];
        options.AzureServiceBus.SubscriptionName = configuration["AzureServiceBus:SubscriptionName"] ?? options.AzureServiceBus.SubscriptionName;
        options.AzureServiceBus.MaxDeliveryCount = ReadInt(configuration["Messaging:AzureServiceBus:MaxDeliveryCount"], options.AzureServiceBus.MaxDeliveryCount);
        options.AzureServiceBus.LockDurationSeconds = ReadInt(configuration["Messaging:AzureServiceBus:LockDurationSeconds"], options.AzureServiceBus.LockDurationSeconds);
        options.AzureServiceBus.DeadLetteringOnMessageExpiration = ReadBool(configuration["Messaging:AzureServiceBus:DeadLetteringOnMessageExpiration"], options.AzureServiceBus.DeadLetteringOnMessageExpiration);
        options.AzureServiceBus.DefaultMessageTimeToLiveDays = ReadInt(configuration["Messaging:AzureServiceBus:DefaultMessageTimeToLiveDays"], options.AzureServiceBus.DefaultMessageTimeToLiveDays);
        options.AzureServiceBus.RequireSession = ReadBool(configuration["Messaging:AzureServiceBus:RequireSession"], options.AzureServiceBus.RequireSession);

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
    private static bool ReadBool(string? raw, bool fallback) => bool.TryParse(raw, out var b) ? b : fallback;
}
