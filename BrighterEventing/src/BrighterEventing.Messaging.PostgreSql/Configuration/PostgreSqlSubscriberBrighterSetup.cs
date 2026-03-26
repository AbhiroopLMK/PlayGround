using BrighterEventing.Messaging.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using Paramore.Brighter.Inbox.Postgres;
using Paramore.Brighter.PostgreSql;

namespace BrighterEventing.Messaging.PostgreSql.Configuration;

public static class PostgreSqlSubscriberBrighterSetup
{
    /// <summary>
    /// Registers PostgreSQL relational inbox dependencies when configured, and returns a callback for
    /// <see cref="BrighterSubscriberServiceCollectionExtensions.AddBrighterEventingSubscriberMessaging"/> (configure consumers after transport).
    /// </summary>
    public static Action<ConsumersOptions, BrighterSubscriberOptions> CreateConsumersConfigurer(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BrighterSubscriberServiceCollectionExtensions.BindSubscriberOptions(configuration);
        var inboxConnectionString = configuration.GetConnectionString(options.InboxConnectionStringName);

        RelationalDatabaseConfiguration? relationalConfig = null;
        if (options.ImplementInbox && options.DatabaseType == DatabaseType.PostgreSql)
        {
            if (string.IsNullOrWhiteSpace(inboxConnectionString))
                throw new InvalidOperationException($"ConnectionStrings:{options.InboxConnectionStringName} is required when ImplementInbox=true.");

            relationalConfig = new RelationalDatabaseConfiguration(
                inboxConnectionString!,
                databaseName: options.DatabaseName,
                inboxTableName: options.InboxTableName);
            services.AddSingleton<IAmARelationalDatabaseConfiguration>(relationalConfig);
        }

        return (consumers, o) =>
        {
            if (!o.ImplementInbox)
                return;

            if (o.DatabaseType != DatabaseType.PostgreSql || relationalConfig is null)
                throw new InvalidOperationException($"Unsupported inbox database type '{o.DatabaseType}'.");

            consumers.InboxConfiguration = new InboxConfiguration(new PostgreSqlInbox(relationalConfig));
        };
    }
}
