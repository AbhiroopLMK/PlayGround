using BrighterEventing.Subscriber.Configuration;
using BrighterEventing.Subscriber.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using Paramore.Brighter.ServiceActivator.Extensions.Hosting;
using Polly;
using Polly.Registry;
using Polly.Retry;

namespace BrighterEventing.Subscriber;

internal static class Program
{
    private const string InboxTableName = "Inbox";
    private const string ConsumerRetryPipelineName = "ConsumerRetryPipeline";

    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // secrets.json (gitignored) overwrites appsettings when present.
        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

        var config = builder.Configuration;
        var connectionString = config.GetConnectionString("BrighterInbox")
            ?? throw new InvalidOperationException("ConnectionStrings:BrighterInbox is required.");
        var transport = config["Transport"] ?? "RabbitMQ";

        builder.Services.Configure<MessagingOptions>(config.GetSection(MessagingOptions.SectionName));
        builder.Services.Configure<TestingOptions>(config.GetSection(TestingOptions.SectionName));

        var messaging = config.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();
        var consumer = messaging.Consumer;
        var asbOpts = messaging.AzureServiceBus;

        var timeout = TimeSpan.FromMilliseconds(consumer.ReceiveTimeoutMs);
        var requeueDelay = consumer.RequeueDelayMs > 0 ? TimeSpan.FromMilliseconds(consumer.RequeueDelayMs) : (TimeSpan?)null;

        var inboxConfig = new Paramore.Brighter.RelationalDatabaseConfiguration(
            connectionString,
            databaseName: "neondb",
            inboxTableName: InboxTableName);

        builder.Services.AddSingleton<Paramore.Brighter.IAmARelationalDatabaseConfiguration>(inboxConfig);

        var resiliencePipelineRegistry = new ResiliencePipelineRegistry<string>();
        // Required by Brighter when using a custom registry (used for outbox producer path).
        resiliencePipelineRegistry.TryAddBuilder(CommandProcessor.OutboxProducer, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
            }));
        // Non-generic (for sync handlers / OutboxProducer). Generic pipelines are registered per event type below.
        resiliencePipelineRegistry.TryAddBuilder(ConsumerRetryPipelineName, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, consumer.MaxRetryCount),
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, consumer.RequeueDelayMs)),
                BackoffType = DelayBackoffType.Exponential,
            }));
        // Generic pipeline required by UseResiliencePipelineAsync for OrderCreatedEvent.
        resiliencePipelineRegistry.TryAddBuilder<Contracts.Events.OrderCreatedEvent>(ConsumerRetryPipelineName, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions<Contracts.Events.OrderCreatedEvent>
            {
                MaxRetryAttempts = Math.Max(0, consumer.MaxRetryCount),
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, consumer.RequeueDelayMs)),
                BackoffType = DelayBackoffType.Exponential,
            }));

        builder.Services.AddBrighter(options =>
        {
            options.HandlerLifetime = ServiceLifetime.Scoped;
            options.MapperLifetime = ServiceLifetime.Singleton;
            options.ResiliencePipelineRegistry = resiliencePipelineRegistry;
        })
        .AutoFromAssemblies([typeof(Program).Assembly, typeof(Contracts.Events.OrderCreatedEvent).Assembly]);

        builder.Services.AddConsumers(consumers =>
        {
            if (transport == "AzureServiceBus")
            {
                var asbConnectionString = config["AzureServiceBus:ConnectionString"]
                    ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString is required when Transport=AzureServiceBus.");
                var topicName = config["AzureServiceBus:TopicName"] ?? "brighter-events";
                var subscriptionName = config["AzureServiceBus:SubscriptionName"] ?? "brighter-eventing-sub";

                var clientProvider = new ServiceBusConnectionStringClientProvider(asbConnectionString);

                var subscriptionConfiguration = new AzureServiceBusSubscriptionConfiguration
                {
                    MaxDeliveryCount = asbOpts.MaxDeliveryCount,
                    LockDuration = TimeSpan.FromSeconds(asbOpts.LockDurationSeconds),
                    DeadLetteringOnMessageExpiration = asbOpts.DeadLetteringOnMessageExpiration,
                    DefaultMessageTimeToLive = asbOpts.DefaultMessageTimeToLiveDays > 0
                        ? TimeSpan.FromDays(asbOpts.DefaultMessageTimeToLiveDays)
                        : TimeSpan.FromMinutes(1)
                };

                consumers.Subscriptions =
                [
                    new AzureServiceBusSubscription<Contracts.Events.OrderCreatedEvent>(
                        new SubscriptionName(subscriptionName),
                        new ChannelName(subscriptionName),
                        new RoutingKey("order.created"),
                        timeOut: timeout,
                        makeChannels: OnMissingChannel.Create,
                        requeueCount: consumer.MaxRetryCount,
                        requeueDelay: requeueDelay,
                        messagePumpType: MessagePumpType.Proactor,
                        subscriptionConfiguration: subscriptionConfiguration),
                    new AzureServiceBusSubscription<Contracts.Events.GreetingMadeEvent>(
                        new SubscriptionName(subscriptionName),
                        new ChannelName(subscriptionName),
                        new RoutingKey("greeting.made"),
                        timeOut: timeout,
                        makeChannels: OnMissingChannel.Create,
                        requeueCount: consumer.MaxRetryCount,
                        requeueDelay: requeueDelay,
                        messagePumpType: MessagePumpType.Proactor,
                        subscriptionConfiguration: subscriptionConfiguration)
                ];
                consumers.DefaultChannelFactory = new AzureServiceBusChannelFactory(new AzureServiceBusConsumerFactory(clientProvider));
            }
            else
            {
                var amqpUri = config["RabbitMQ:AmqpUri"] ?? "amqp://guest:guest@localhost:5672";
                var exchangeName = config["RabbitMQ:Exchange"] ?? "brighter.eventing.exchange";
                var subscriptionName = config["RabbitMQ:SubscriptionName"] ?? "brighter-eventing-subscriber";
                var connection = new RmqMessagingGatewayConnection
                {
                    AmpqUri = new AmqpUriSpecification(new Uri(amqpUri)),
                    Exchange = new Exchange(exchangeName)
                };
                consumers.Subscriptions =
                [
                    new RmqSubscription<Contracts.Events.OrderCreatedEvent>(
                        new SubscriptionName(subscriptionName),
                        new ChannelName("order.created.queue"),
                        new RoutingKey("order.created"),
                        timeOut: timeout,
                        makeChannels: OnMissingChannel.Create,
                        requeueCount: consumer.MaxRetryCount,
                        requeueDelay: requeueDelay,
                        messagePumpType: MessagePumpType.Proactor),
                    new RmqSubscription<Contracts.Events.GreetingMadeEvent>(
                        new SubscriptionName(subscriptionName),
                        new ChannelName("greeting.made.queue"),
                        new RoutingKey("greeting.made"),
                        timeOut: timeout,
                        makeChannels: OnMissingChannel.Create,
                        requeueCount: consumer.MaxRetryCount,
                        requeueDelay: requeueDelay,
                        messagePumpType: MessagePumpType.Proactor)
                ];
                consumers.DefaultChannelFactory = new ChannelFactory(new RmqMessageConsumerFactory(connection));
            }

            consumers.InboxConfiguration = new InboxConfiguration(
                new Paramore.Brighter.Inbox.Postgres.PostgreSqlInbox(inboxConfig));
        });

        builder.Services.AddHostedService<Paramore.Brighter.ServiceActivator.Extensions.Hosting.ServiceActivatorHostedService>();

        // Allow time for message pumps to stop before disposal (reduces ObjectDisposedException on Ctrl+C).
        builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        var host = builder.Build();

        await EnsureInboxTableAsync(connectionString);

        await host.RunAsync();
        return 0;
    }

    private static async Task EnsureInboxTableAsync(string connectionString)
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var ddl = Paramore.Brighter.Inbox.Postgres.PostgreSqlInboxBuilder.GetDDL(InboxTableName);
            // Run full DDL as a single command (Brighter may return one or more statements)
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Inbox table setup: {ex.Message}");
        }
    }
}
