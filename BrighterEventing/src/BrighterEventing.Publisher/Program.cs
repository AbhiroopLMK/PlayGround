using BrighterEventing.Publisher.Commands;
using BrighterEventing.Publisher.Configuration;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.RMQ.Async;
using Paramore.Brighter.Outbox.PostgreSql;
using Paramore.Brighter.PostgreSql.EntityFrameworkCore;
using Npgsql;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;
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

        // secrets.json (gitignored) overwrites appsettings when present.
        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

        var config = builder.Configuration;
        var connectionString = config.GetConnectionString("BrighterOutbox")
            ?? throw new InvalidOperationException("ConnectionStrings:BrighterOutbox is required.");
        var transport = config["Transport"] ?? TransportType.RabbitMQ;

        // DbContext for outbox transaction (HLD: Reliability via Outbox)
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
        .AutoFromAssemblies([typeof(Program).Assembly], [typeof(Contracts.Events.OrderCreatedEvent)]);

        builder.Services.AddHostedService<PublisherHostedService>();

        var host = builder.Build();

        await EnsureOutboxTableAsync(host.Services, connectionString);

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
            new RmqPublication<Contracts.Events.OrderCreatedEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey("order.created")
            },
            new RmqPublication<Contracts.Events.GreetingMadeEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey("greeting.made")
            }
        ]).Create();
    }

    private static void ConfigureAzureServiceBus(
        dynamic producers,
        IConfiguration config)
    {
        var connectionString = config["AzureServiceBus:ConnectionString"]
            ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString is required when Transport=AzureServiceBus.");
        var topicName = config["AzureServiceBus:TopicName"] ?? "brighter-events";

        var connection = new ServiceBusConnectionStringClientProvider(connectionString);

        producers.ProducerRegistry = new AzureServiceBusProducerRegistryFactory(connection,
        [
            new AzureServiceBusPublication<Contracts.Events.OrderCreatedEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey("order.created")
            },
            new AzureServiceBusPublication<Contracts.Events.GreetingMadeEvent>
            {
                MakeChannels = OnMissingChannel.Create,
                Topic = new RoutingKey("greeting.made")
            }
        ]).Create();
    }

    private static async Task EnsureOutboxTableAsync(IServiceProvider services, string connectionString)
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
}
