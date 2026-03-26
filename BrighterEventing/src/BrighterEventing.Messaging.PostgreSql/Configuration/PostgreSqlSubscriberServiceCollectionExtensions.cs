using BrighterEventing.Messaging.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BrighterEventing.Messaging.PostgreSql.Configuration;

public static class PostgreSqlSubscriberServiceCollectionExtensions
{
    /// <summary>
    /// Registers Brighter subscriber messaging with PostgreSQL inbox. Prefer calling
    /// <see cref="BrighterSubscriberServiceCollectionExtensions.AddBrighterEventingSubscriberMessaging"/> from the core messaging project with
    /// <see cref="PostgreSqlSubscriberBrighterSetup.CreateConsumersConfigurer"/> so "Go to definition" lands on the core API.
    /// </summary>
    public static IServiceCollection AddBrighterEventingSubscriberMessagingWithPostgreSqlInbox(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] autoFromAssemblies)
    {
        var configure = PostgreSqlSubscriberBrighterSetup.CreateConsumersConfigurer(services, configuration);
        return services.AddBrighterEventingSubscriberMessaging(configuration, configure, autoFromAssemblies);
    }
}
