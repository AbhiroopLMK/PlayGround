using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.Observability;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Wraps Brighter's ASB producers so <see cref="MessageHeaderAzureServiceBusExtensions.EnsureCanonicalSessionIdInBag"/>
/// runs after outbox reload (JSON often renames <c>SessionId</c> → <c>sessionId</c>).
/// </summary>
internal static class SessionIdNormalizingMessageProducer
{
    public static IAmAMessageProducer Wrap(IAmAMessageProducer inner)
    {
        if (inner is AzureServiceBusMessageProducer p)
            return new SessionIdNormalizingAzureServiceBusMessageProducer(p);
        return inner;
    }
}

internal sealed class SessionIdNormalizingAzureServiceBusMessageProducer :
    IAmAMessageProducerSync,
    IAmAMessageProducerAsync,
    IAmABulkMessageProducerAsync
{
    private readonly AzureServiceBusMessageProducer _inner;

    public SessionIdNormalizingAzureServiceBusMessageProducer(AzureServiceBusMessageProducer inner) =>
        _inner = inner;

    public Publication Publication
    {
        get => _inner.Publication;
        set => _inner.Publication = value;
    }

    public Activity? Span
    {
        get => _inner.Span;
        set => _inner.Span = value;
    }

    public IAmAMessageScheduler? Scheduler
    {
        get => _inner.Scheduler;
        set => _inner.Scheduler = value;
    }

    public void Send(Message message)
    {
        message.Header.EnsureCanonicalSessionIdInBag();
        BrighterEventingAzureServiceBusProducerSend.SendWithDelaySync(_inner, message, null);
    }

    public void SendWithDelay(Message message, TimeSpan? delay)
    {
        message.Header.EnsureCanonicalSessionIdInBag();
        BrighterEventingAzureServiceBusProducerSend.SendWithDelaySync(_inner, message, delay);
    }

    public Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        message.Header.EnsureCanonicalSessionIdInBag();
        return BrighterEventingAzureServiceBusProducerSend.SendWithDelayAsync(_inner, message, null, cancellationToken);
    }

    public Task SendWithDelayAsync(Message message, TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        message.Header.EnsureCanonicalSessionIdInBag();
        return BrighterEventingAzureServiceBusProducerSend.SendWithDelayAsync(_inner, message, delay, cancellationToken);
    }

    public async ValueTask<IEnumerable<IAmAMessageBatch>> CreateBatchesAsync(
        IEnumerable<Message> messages,
        CancellationToken cancellationToken)
    {
        foreach (var m in messages)
            m.Header.EnsureCanonicalSessionIdInBag();
        return await _inner.CreateBatchesAsync(messages, cancellationToken);
    }

    public Task SendAsync(IAmAMessageBatch batch, CancellationToken cancellationToken) =>
        _inner.SendAsync(batch, cancellationToken);

    public void Dispose() => _inner.Dispose();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
