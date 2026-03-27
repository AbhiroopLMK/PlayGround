using System.Reflection;
using BrighterEventing.Messaging.Configuration;

namespace BrighterEventing.Sample.DomainEvents;

/// <summary>
/// Central place for sample event type ↔ config key ↔ CloudEvents <c>type</c> wiring used by all POC hosts.
/// </summary>
public static class SampleEventCatalog
{
    /// <summary>
    /// Pass to Brighter <c>AutoFromAssemblies</c> (last parameter of <c>AddBrighterEventing*Messaging</c>): this assembly
    /// contains the sample events and their <c>IAmAMessageMapper&lt;T&gt;</c> implementations.
    /// </summary>
    public static Assembly Assembly => typeof(SampleEventCatalog).Assembly;

    /// <summary>
    /// Registers the three order events with Brighter's <see cref="EventTypeCatalogBuilder"/> (CLR keys + LGS CloudEvents types).
    /// </summary>
    public static EventTypeCatalogBuilder AddSampleOrderEvents(this EventTypeCatalogBuilder catalog)
    {
        catalog.Map<OrderCreatedEvent>(nameof(OrderCreatedEvent), "OrderCreated")
            .WithCloudEventsType<OrderCreatedEvent>(SampleOrderEventNames.OrderCreated);
        catalog.Map<OrderUpdatedEvent>(nameof(OrderUpdatedEvent), "OrderUpdated")
            .WithCloudEventsType<OrderUpdatedEvent>(SampleOrderEventNames.OrderUpdated);
        catalog.Map<OrderCancelledEvent>(nameof(OrderCancelledEvent), "OrderCancelled")
            .WithCloudEventsType<OrderCancelledEvent>(SampleOrderEventNames.OrderCancelled);
        return catalog;
    }
}
