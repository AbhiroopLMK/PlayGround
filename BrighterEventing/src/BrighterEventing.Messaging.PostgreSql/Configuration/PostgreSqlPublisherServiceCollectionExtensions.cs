using BrighterEventing.Messaging.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BrighterEventing.Messaging.PostgreSql.Configuration;

public static class PostgreSqlPublisherServiceCollectionExtensions
{
    /// <summary>
    /// Registers Brighter publisher messaging with PostgreSQL outbox. Prefer calling
    /// <see cref="BrighterPublisherServiceCollectionExtensions.AddBrighterEventingPublisherMessaging"/> from the core messaging project with
    /// <see cref="PostgreSqlPublisherBrighterSetup.CreateProducersConfigurer"/> so "Go to definition" lands on the core API.
    /// </summary>
    public static IServiceCollection AddBrighterEventingPublisherMessagingWithPostgreSqlOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeCatalogBuilder> configureEventTypes,
        params Assembly[] autoFromAssemblies)
    {
        var configure = PostgreSqlPublisherBrighterSetup.CreateProducersConfigurer(services, configuration);
        return services.AddBrighterEventingPublisherMessaging(configuration, configureEventTypes, configure, autoFromAssemblies);
    }

    public static IServiceCollection AddBrighterEventingPublisherMessagingWithPostgreSqlOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        IEventTypeRegistry eventTypeRegistry,
        params Assembly[] autoFromAssemblies)
    {
        var configure = PostgreSqlPublisherBrighterSetup.CreateProducersConfigurer(services, configuration);
        return services.AddBrighterEventingPublisherMessaging(configuration, eventTypeRegistry, configure, autoFromAssemblies);
    }
}
