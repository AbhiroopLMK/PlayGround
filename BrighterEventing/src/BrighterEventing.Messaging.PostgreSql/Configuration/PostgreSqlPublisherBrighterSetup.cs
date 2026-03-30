using BrighterEventing.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.Outbox.PostgreSql;
using Paramore.Brighter.PostgreSql;
using Paramore.Brighter.PostgreSql.EntityFrameworkCore;

namespace BrighterEventing.Messaging.PostgreSql.Configuration;

/// <summary>PostgreSQL relational outbox wiring for <c>AddBrighterEventingPublisherMessaging</c> (full .NET hosts).</summary>
public static class PostgreSqlPublisherBrighterSetup
{
    /// <summary>
    /// Overload for hosts that manage their own EF Core <typeparamref name="TContext"/>.
    /// Uses <see cref="PostgreSqlEntityFrameworkTransactionProvider{T}"/> for transactions and
    /// <see cref="PostgreSqlConnectionProvider"/> for connections (same pattern as Brighter EF Core samples)
    /// so <c>DepositPostAsync</c> can participate in the same EF Core transaction as the domain write.
    /// </summary>
    public static Action<ProducersConfiguration, BrighterPublisherOptions> CreateProducersConfigurer<TContext>(
        IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        var relationalConfig = BuildRelationalConfig(services, configuration);

        return (producers, o) =>
        {
            if (!o.ImplementOutbox)
                return;

            if (o.DatabaseType != DatabaseType.PostgreSql)
                throw new InvalidOperationException($"Unsupported outbox database type '{o.DatabaseType}'.");

            producers.Outbox = new PostgreSqlOutbox(relationalConfig!);
            producers.TransactionProvider = typeof(PostgreSqlEntityFrameworkTransactionProvider<TContext>);
            producers.ConnectionProvider = typeof(PostgreSqlConnectionProvider);
        };
    }

    /// <summary>
    /// Overload for hosts that use a raw Npgsql connection (no EF Core DbContext).
    /// Falls back to <c>PostgreSqlConnectionProvider</c> for both connection and transaction.
    /// </summary>
    public static Action<ProducersConfiguration, BrighterPublisherOptions> CreateProducersConfigurer(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BrighterPublisherServiceCollectionExtensions.BindPublisherOptions(configuration);
        var relationalConfig = BuildRelationalConfig(services, configuration);

        return (producers, o) =>
        {
            if (!o.ImplementOutbox)
                return;

            if (o.DatabaseType != DatabaseType.PostgreSql)
                throw new InvalidOperationException($"Unsupported outbox database type '{o.DatabaseType}'.");

            producers.Outbox = new PostgreSqlOutbox(relationalConfig!);

            var connectionProviderTypeName = !string.IsNullOrWhiteSpace(o.ConnectionProviderType)
                ? o.ConnectionProviderType
                : "Paramore.Brighter.PostgreSql.PostgreSqlConnectionProvider, Paramore.Brighter.PostgreSql";

            var transactionProviderTypeName = !string.IsNullOrWhiteSpace(o.TransactionProviderType)
                ? o.TransactionProviderType
                : connectionProviderTypeName;

            producers.ConnectionProvider = Type.GetType(connectionProviderTypeName, throwOnError: true)!;
            producers.TransactionProvider = Type.GetType(transactionProviderTypeName, throwOnError: true)!;
        };
    }

    private static RelationalDatabaseConfiguration? BuildRelationalConfig(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BrighterPublisherServiceCollectionExtensions.BindPublisherOptions(configuration);
        var connectionString = configuration.GetConnectionString(options.OutboxConnectionStringName);

        if (!options.ImplementOutbox || options.DatabaseType != DatabaseType.PostgreSql)
            return null;

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"ConnectionStrings:{options.OutboxConnectionStringName} is required when ImplementOutbox=true.");

        var relationalConfig = new RelationalDatabaseConfiguration(
            connectionString!,
            databaseName: options.DatabaseName,
            outBoxTableName: options.OutboxTableName);

        services.AddSingleton<IAmARelationalDatabaseConfiguration>(relationalConfig);
        return relationalConfig;
    }
}
