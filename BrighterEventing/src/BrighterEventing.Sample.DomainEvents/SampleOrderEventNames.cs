namespace BrighterEventing.Sample.DomainEvents;

/// <summary>Stable identifiers for this sample (Lgs <c>Type</c> and Rabbit <c>MessageName</c>).</summary>
public static class SampleOrderEventNames
{
    public const string OrderCreated = "orders.order.created.v1";

    public const string OrderUpdated = "orders.order.updated.v1";

    public const string OrderCancelled = "orders.order.cancelled.v1";
}
