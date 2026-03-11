using BrighterEventing.Subscriber.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using Paramore.Brighter.ServiceActivator.Extensions.Hosting;

namespace BrighterEventing.Subscriber;

internal static class Program
{
    private const string InboxTableName = "Inbox";

    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // secrets.json (gitignored) overwrites appsettings when present.
        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

        var config = builder.Configuration;
        var connectionString = config.GetConnectionString("BrighterInbox")
            ?? throw new InvalidOperationException("ConnectionStrings:BrighterInbox is required.");
        var transport = config["Transport"] ?? "RabbitMQ";

        var inboxConfig = new Paramore.Brighter.RelationalDatabaseConfiguration(
            connectionString,
            databaseName: "neondb",
            inboxTableName: InboxTableName);

        builder.Services.AddSingleton<Paramore.Brighter.IAmARelationalDatabaseConfiguration>(inboxConfig);

        builder.Services.AddBrighter(options =>
        {
            options.HandlerLifetime = ServiceLifetime.Scoped;
            options.MapperLifetime = ServiceLifetime.Singleton;
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
                // Azure Service Bus: use the type from your Brighter.MessagingGateway.AzureServiceBus package (e.g. ServiceBusConnectionStringClientProvider in 10.3+)
                throw new NotSupportedException(
                    "Azure Service Bus transport: configure the client provider from Paramore.Brighter.MessagingGateway.AzureServiceBus. " +
                    "For this sample use Transport=RabbitMQ in appsettings, or upgrade to Brighter 10.3+ and add the correct client provider type.");
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
                        makeChannels: OnMissingChannel.Create,
                        messagePumpType: MessagePumpType.Proactor),
                    new RmqSubscription<Contracts.Events.GreetingMadeEvent>(
                        new SubscriptionName(subscriptionName),
                        new ChannelName("greeting.made.queue"),
                        new RoutingKey("greeting.made"),
                        makeChannels: OnMissingChannel.Create,
                        messagePumpType: MessagePumpType.Proactor)
                ];
                consumers.DefaultChannelFactory = new ChannelFactory(new RmqMessageConsumerFactory(connection));
            }

            consumers.InboxConfiguration = new InboxConfiguration(
                new Paramore.Brighter.Inbox.Postgres.PostgreSqlInbox(inboxConfig));
        });

        builder.Services.AddHostedService<Paramore.Brighter.ServiceActivator.Extensions.Hosting.ServiceActivatorHostedService>();

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
