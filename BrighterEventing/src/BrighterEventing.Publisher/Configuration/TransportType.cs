namespace BrighterEventing.Publisher.Configuration;

/// <summary>
/// Transport type for clean architecture: swap Azure Service Bus and RabbitMQ via configuration.
/// Maps to HLD driver: Transport Flexibility.
/// </summary>
public static class TransportType
{
    public const string RabbitMQ = "RabbitMQ";
    public const string AzureServiceBus = "AzureServiceBus";
}
