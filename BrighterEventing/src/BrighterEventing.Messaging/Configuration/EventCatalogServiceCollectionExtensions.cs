using Microsoft.Extensions.DependencyInjection;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Use when you build <see cref="IEventTypeRegistry"/> in two steps (e.g. tests). Host apps usually pass
/// <c>catalog => ...</c> directly into <see cref="BrighterPublisherServiceCollectionExtensions.AddBrighterEventingPublisherMessaging"/>.
/// </summary>
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
