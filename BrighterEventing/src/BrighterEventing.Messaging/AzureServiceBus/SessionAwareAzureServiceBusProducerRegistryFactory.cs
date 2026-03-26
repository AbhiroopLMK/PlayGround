using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Same as <see cref="AzureServiceBusProducerRegistryFactory"/> but uses <see cref="SessionAwareAzureServiceBusMessageProducerFactory"/>
/// so messages deserialized from the Postgres outbox get canonical <c>SessionId</c> in the header bag before ASB conversion.
/// </summary>
public sealed class SessionAwareAzureServiceBusProducerRegistryFactory : IAmAProducerRegistryFactory
{
    private readonly IServiceBusClientProvider _clientProvider;
    private readonly IEnumerable<AzureServiceBusPublication> _asbPublications;
    private readonly int _bulkSendBatchSize;

    public SessionAwareAzureServiceBusProducerRegistryFactory(
        IServiceBusClientProvider clientProvider,
        IEnumerable<AzureServiceBusPublication> asbPublications,
        int bulkSendBatchSize = 10)
    {
        _clientProvider = clientProvider;
        _asbPublications = asbPublications;
        _bulkSendBatchSize = bulkSendBatchSize;
    }

    public IAmAProducerRegistry Create()
    {
        var producerFactory = new SessionAwareAzureServiceBusMessageProducerFactory(
            _clientProvider,
            _asbPublications,
            _bulkSendBatchSize);

        return new ProducerRegistry(producerFactory.Create());
    }

    public Task<IAmAProducerRegistry> CreateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Create());
}
