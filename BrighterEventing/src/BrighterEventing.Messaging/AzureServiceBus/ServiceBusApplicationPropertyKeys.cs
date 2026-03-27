namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>Common Azure Service Bus user property names used with org subscriptions (Correlation / SQL filters).</summary>
public static class ServiceBusApplicationPropertyKeys
{
    /// <summary>Custom property often used alongside CloudEvents metadata for fine-grained subscription rules.</summary>
    public const string ServiceBusEventType = "serviceBusEventType";
}
