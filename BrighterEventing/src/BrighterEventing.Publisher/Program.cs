using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Messaging.PostgreSql.Configuration;
using BrighterEventing.Publisher.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BrighterEventing.Publisher;

internal static class Program
{
    private const string OutboxTableName = "Outbox";

    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

        var config = builder.Configuration;
        var outboxConnectionString = config.GetConnectionString("BrighterOutbox")
            ?? throw new InvalidOperationException("ConnectionStrings:BrighterOutbox is required.");

        builder.Services.AddDbContext<BrighterOutboxDbContext>(options =>
            options.UseNpgsql(outboxConnectionString));

        builder.Services.AddBrighterEventingPublisherMessaging(
            config,
            PostgreSqlPublisherBrighterSetup.CreateProducersConfigurer(builder.Services, config));

        builder.Services.AddHostedService<PublisherHostedService>();

        var host = builder.Build();

        await EnsureOutboxTableAsync(outboxConnectionString);
        await EnsureDemoOrdersTableAsync(outboxConnectionString);

        await host.RunAsync();
        return 0;
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
