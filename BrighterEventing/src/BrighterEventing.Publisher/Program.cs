using BrighterEventing.Messaging;
using BrighterEventing.Messaging.Events;
using BrighterEventing.Publisher.Configuration;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.Outbox.PostgreSql;
using Paramore.Brighter.PostgreSql.EntityFrameworkCore;
using Polly;
using Polly.Registry;
using Polly.Retry;

namespace BrighterEventing.Publisher;

internal static class Program
{
    private const string OutboxTableName = "Outbox";

    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

        var config = builder.Configuration;
        var connectionString = config.GetConnectionString("BrighterOutbox")
            ?? throw new InvalidOperationException("ConnectionStrings:BrighterOutbox is required.");
        var transport = config["Transport"] ?? TransportType.RabbitMQ;

        builder.Services.AddDbContext<BrighterOutboxDbContext>(options =>
            options.UseNpgsql(connectionString));

        var outboxConfig = new Paramore.Brighter.RelationalDatabaseConfiguration(
            connectionString,
            databaseName: "neondb",
            outBoxTableName: OutboxTableName);

        builder.Services.AddSingleton<Paramore.Brighter.IAmARelationalDatabaseConfiguration>(outboxConfig);

        var resiliencePipelineRegistry = new ResiliencePipelineRegistry<string>();
        resiliencePipelineRegistry.TryAddBuilder(CommandProcessor.OutboxProducer, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
            }));

        builder.Services.AddBrighter(options =>
        {
            options.HandlerLifetime = ServiceLifetime.Scoped;
            options.MapperLifetime = ServiceLifetime.Singleton;
            options.ResiliencePipelineRegistry = resiliencePipelineRegistry;
        })
        .AddProducers(producers =>
        {
            producers.Outbox = new PostgreSqlOutbox(outboxConfig);
            producers.ConnectionProvider = typeof(Paramore.Brighter.PostgreSql.PostgreSqlConnectionProvider);
            producers.TransactionProvider = typeof(PostgreSqlEntityFrameworkTransactionProvider<BrighterOutboxDbContext>);

            if (transport == TransportType.AzureServiceBus)
            {
                ConfigureAzureServiceBus(producers, config);
            }
            else
            {
                ConfigureRabbitMQ(producers, config);
            }
        })
        .AutoFromAssemblies(
        [
            typeof(Program).Assembly,
            typeof(LgsEnvelopeBrighterEvent).Assembly
        ]);

        builder.Services.AddHostedService<PublisherHostedService>();

        var host = builder.Build();

        await EnsureOutboxTableAsync(connectionString);
        await EnsureDemoOrdersTableAsync(connectionString);

        await host.RunAsync();
        return 0;
    }

    private static void ConfigureRabbitMQ(
        dynamic producers,
        IConfiguration config)
    {
        var amqpUri = config["RabbitMQ:AmqpUri"] ?? "amqp://guest:guest@localhost:5672";
        var exchangeName = config["RabbitMQ:Exchange"] ?? "brighter.eventing.exchange";

        var connection = new RmqMessagingGatewayConnection
        {
            AmpqUri = new AmqpUriSpecification(new Uri(amqpUri)),
            Exchange = new Exchange(exchangeName)
        };

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

    private static void ConfigureAzureServiceBus(
        dynamic producers,
        IConfiguration config)
    {
        var connectionString = config["AzureServiceBus:ConnectionString"]
            ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString is required when Transport=AzureServiceBus.");

        var connection = new ServiceBusConnectionStringClientProvider(connectionString);

        producers.ProducerRegistry = new AzureServiceBusProducerRegistryFactory(connection,
        [
            new AzureServiceBusPublication<LgsEnvelopeBrighterEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey(MessagingRoutingKeys.LgsWrapped)
            },
            new AzureServiceBusPublication<RabbitInternalEnvelopeBrighterEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey(MessagingRoutingKeys.RabbitInternalWrapped)
            }
        ]).Create();
    }

    private static async Task EnsureOutboxTableAsync(string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var ddl = Paramore.Brighter.Outbox.PostgreSql.PostgreSqlOutboxBuilder.GetDDL(OutboxTableName);
            foreach (var stmt in ddl.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = stmt.Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = s + ";";
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Outbox table setup: {ex.Message}");
        }
    }

    private static async Task EnsureDemoOrdersTableAsync(string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "DemoOrders" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "OrderKey" character varying(128) NOT NULL,
                    "CreatedUtc" timestamp without time zone NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DemoOrders table setup: {ex.Message}");
        }
    }
}
