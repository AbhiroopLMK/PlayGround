using System.Collections.Generic;
using System.Threading.Tasks;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.ClientProvider;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Extends Brighter's factory by wrapping each producer so session id bag keys are normalized before send.
/// </summary>
public sealed class SessionAwareAzureServiceBusMessageProducerFactory : AzureServiceBusMessageProducerFactory
{
    public SessionAwareAzureServiceBusMessageProducerFactory(
        IServiceBusClientProvider clientProvider,
        IEnumerable<AzureServiceBusPublication> publications,
        int bulkSendBatchSize)
        : base(clientProvider, publications, bulkSendBatchSize)
    {
    }

    public new Dictionary<ProducerKey, IAmAMessageProducer> Create()
    {
        var producers = base.Create();
        var wrapped = new Dictionary<ProducerKey, IAmAMessageProducer>();
        foreach (var kv in producers)
            wrapped[kv.Key] = SessionIdNormalizingMessageProducer.Wrap(kv.Value);
        return wrapped;
    }

    public new Task<Dictionary<ProducerKey, IAmAMessageProducer>> CreateAsync() =>
        Task.FromResult(Create());
}
