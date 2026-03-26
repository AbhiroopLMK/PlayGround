using BrighterEventing.Messaging.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter;
using Paramore.Brighter.Outbox.PostgreSql;

namespace BrighterEventing.Messaging.PostgreSql.Configuration;

public static class PostgreSqlPublisherBrighterSetup
{
    /// <summary>
    /// Registers PostgreSQL relational outbox dependencies when configured, and returns a callback for
    /// <see cref="BrighterPublisherServiceCollectionExtensions.AddBrighterEventingPublisherMessaging"/> (configure producers before transport).
    /// </summary>
    public static Action<dynamic, BrighterPublisherOptions> CreateProducersConfigurer(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BrighterPublisherServiceCollectionExtensions.BindPublisherOptions(configuration);
        var connectionString = configuration.GetConnectionString(options.OutboxConnectionStringName);

        RelationalDatabaseConfiguration? relationalConfig = null;
        if (options.ImplementOutbox && options.DatabaseType == DatabaseType.PostgreSql)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException($"ConnectionStrings:{options.OutboxConnectionStringName} is required when ImplementOutbox=true.");

            relationalConfig = new RelationalDatabaseConfiguration(
                connectionString!,
                databaseName: options.DatabaseName,
                outBoxTableName: options.OutboxTableName);
            services.AddSingleton<IAmARelationalDatabaseConfiguration>(relationalConfig);
        }

        return (producers, o) =>
        {
            if (!o.ImplementOutbox)
                return;

            if (o.DatabaseType != DatabaseType.PostgreSql)
                throw new InvalidOperationException($"Unsupported outbox database type '{o.DatabaseType}'.");

            producers.Outbox = new PostgreSqlOutbox(relationalConfig!);
            if (!string.IsNullOrWhiteSpace(o.ConnectionProviderType))
                producers.ConnectionProvider = Type.GetType(o.ConnectionProviderType!, throwOnError: true)!;

            if (!string.IsNullOrWhiteSpace(o.TransactionProviderType))
                producers.TransactionProvider = Type.GetType(o.TransactionProviderType!, throwOnError: true)!;
        };
    }
}
