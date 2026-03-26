using BrighterEventing.Messaging.AzureServiceBus;
using BrighterEventing.Messaging.Events;
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

    /// <summary>Registers Brighter producers for BrighterEventing (RabbitMQ or Azure Service Bus).</summary>
    /// <param name="autoFromAssemblies">
    /// Optional extra assemblies for Brighter <c>AutoFromAssemblies</c> (handlers, mappers). When omitted or empty,
    /// <see cref="BrighterEventingAssemblyRegistration.ResolveAutoFromAssemblies"/> uses the entry assembly and the shared messaging assembly.
    /// </param>
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] autoFromAssemblies) =>
        services.AddBrighterEventingPublisherMessaging(configuration, configureProducersBeforeTransport: null, autoFromAssemblies);

    /// <summary>Registers Brighter producers; optional callback runs inside <c>AddProducers</c> before transport wiring (e.g. PostgreSQL outbox).</summary>
    /// <param name="autoFromAssemblies">
    /// Optional extra assemblies for Brighter <c>AutoFromAssemblies</c>. When omitted or empty,
    /// <see cref="BrighterEventingAssemblyRegistration.ResolveAutoFromAssemblies"/> uses the entry assembly and the shared messaging assembly.
    /// </param>
    public static IServiceCollection AddBrighterEventingPublisherMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
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
            new RmqPublication<LgsEnvelopeBrighterEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey(MessagingRoutingKeys.LgsWrapped)
            },
            new RmqPublication<RabbitInternalEnvelopeBrighterEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped)
            }
        ]).Create();
    }

    private static void ConfigureAzureServiceBus(dynamic producers, BrighterPublisherOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString))
            throw new InvalidOperationException("Azure Service Bus connection string is required when Transport=AzureServiceBus.");

        var connection = new ServiceBusConnectionStringClientProvider(options.AzureServiceBus.ConnectionString!);
        producers.ProducerRegistry = new SessionAwareAzureServiceBusProducerRegistryFactory(connection,
        [
            new AzureServiceBusPublication<LgsEnvelopeBrighterEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey(MessagingRoutingKeys.LgsWrapped),
            },
            new AzureServiceBusPublication<RabbitInternalEnvelopeBrighterEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped)
            }
        ]).Create();
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
