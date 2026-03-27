using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Wraps <see cref="ServiceBusConnectionStringClientProvider"/> construction so malformed strings surface as actionable errors.
/// </summary>
public static class ServiceBusConnectionStringHelper
{
    public static ServiceBusConnectionStringClientProvider CreateClientProvider(string connectionString)
    {
        try
        {
            return new ServiceBusConnectionStringClientProvider(connectionString);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException(
                "Invalid Azure Service Bus connection string (URI could not be parsed). " +
                "Use the namespace connection string from Azure Portal: your Service Bus namespace → Shared access policies → RootManageSharedAccessKey → Primary connection string. " +
                "Do not put the Cosmos DB account URI in this field. " +
                "Expected shape: Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=...",
                ex);
        }
    }
}
