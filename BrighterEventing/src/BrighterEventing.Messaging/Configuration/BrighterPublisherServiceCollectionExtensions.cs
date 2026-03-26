using BrighterEventing.Messaging.AzureServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Polly;
using Polly.Registry;
using Polly.Retry;
using System.Reflection;

namespace BrighterEventing.Messaging.Configuration;

public static class BrighterPublisherServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="BrighterPublisherOptions"/> from configuration (including legacy fallback keys).
    /// </summary>
    public static BrighterPublisherOptions BindPublisherOptions(IConfiguration configuration)
    {
        var options = new BrighterPublisherOptions();
        configuration.GetSection(BrighterPublisherOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.Transport))
            options.Transport = configuration["Transport"] ?? BrokerType.RabbitMQ;
        BindFallbackPublisherSettingsFromLegacyConfig(configuration, options);
        return options;
    }

    /// <summary>Registers Brighter producers (RabbitMQ or Azure Service Bus). Registers <see cref="IEventTypeRegistry"/> from <paramref name="configureEventTypes"/>.</summary>
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies) =>
        services.AddBrighterEventingPublisherMessaging(
            configuration,
            configureEventTypes,
            configureProducersBeforeTransport: null,
            autoFromAssemblies);

    /// <summary>Registers Brighter producers; optional callback runs inside <c>AddProducers</c> before transport wiring (e.g. PostgreSQL outbox).</summary>
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        Action<dynamic, BrighterPublisherOptions>? configureProducersBeforeTransport,
        params Assembly[] autoFromAssemblies)
    {
        var builder = new EventTypeCatalogBuilder();
        configureEventTypes(builder);
        var registry = builder.Build();
        services.AddSingleton<IEventTypeRegistry>(registry);
        return services.AddBrighterEventingPublisherMessaging(
            configuration,
            registry,
            configureProducersBeforeTransport,
            autoFromAssemblies);
    }

    /// <summary>Registers Brighter producers using an existing <see cref="IEventTypeRegistry"/> (e.g. built in a composition root).</summary>
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        params Assembly[] autoFromAssemblies) =>
        services.AddBrighterEventingPublisherMessaging(
            configuration,
            eventTypeRegistry,
            configureProducersBeforeTransport: null,
            autoFromAssemblies);

    /// <summary>Registers Brighter producers; optional callback runs inside <c>AddProducers</c> before transport wiring (e.g. PostgreSQL outbox).</summary>
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        Action<dynamic, BrighterPublisherOptions>? configureProducersBeforeTransport,
        params Assembly[] autoFromAssemblies)
    {
        var options = BindPublisherOptions(configuration);

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
            configureProducersBeforeTransport?.Invoke(producers, options);

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
        producers.ProducerRegistry = new SessionAwareAzureServiceBusProducerRegistryFactory(connection,
            BrighterMessagingBrokerRegistration.BuildAzureServiceBusPublications(options, eventTypeRegistry)).Create();
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
}
