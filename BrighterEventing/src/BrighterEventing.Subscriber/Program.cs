using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Messaging.PostgreSql.Configuration;
using BrighterEventing.Subscriber.Configuration;
using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paramore.Brighter.ServiceActivator.Extensions.Hosting;

namespace BrighterEventing.Subscriber;

// Sample subscriber: PostgreSQL inbox + Brighter consumers (ASB or Rabbit). Net6 host uses Cosmos package or no DB.

internal static class Program
{
    private const string InboxTableName = "Inbox";
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

        var config = builder.Configuration;
        var inboxConnectionString = config.GetConnectionString("BrighterInbox")
            ?? throw new InvalidOperationException("ConnectionStrings:BrighterInbox is required.");
        builder.Services.Configure<TestingOptions>(config.GetSection(TestingOptions.SectionName));
        builder.Services.AddBrighterEventingSubscriberMessaging(
            config,
            catalog => catalog.AddSampleOrderEvents(),
            PostgreSqlSubscriberBrighterSetup.CreateConsumersConfigurer(builder.Services, config),
            typeof(OrderCreatedEvent).Assembly);

        builder.Services.AddHostedService<Paramore.Brighter.ServiceActivator.Extensions.Hosting.ServiceActivatorHostedService>();

        builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        var host = builder.Build();

        await EnsureInboxTableAsync(inboxConnectionString);

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
