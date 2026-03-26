using Microsoft.Extensions.DependencyInjection;

namespace BrighterEventing.Messaging.Configuration;

public static class EventCatalogServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IEventTypeRegistry"/> from a fluent catalog (call before Brighter messaging registration).</summary>
    public static IServiceCollection AddBrighterEventTypeCatalog(
        this IServiceCollection services,
        Action<EventTypeCatalogBuilder> configure)
    {
        var builder = new EventTypeCatalogBuilder();
        configure(builder);
        services.AddSingleton<IEventTypeRegistry>(builder.Build());
        return services;
    }
}
