namespace BrighterEventing.Subscriber.Configuration;

/// <summary>
/// Shared messaging configuration for both RabbitMQ and Azure Service Bus consumers.
/// Used for retry, dead-letter, backoff, and receive timeout.
/// </summary>
public class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>
    /// Consumer-level settings (retry, requeue, timeout).
    /// </summary>
    public ConsumerOptions Consumer { get; set; } = new();

    /// <summary>
    /// Azure Service Bus–specific subscription settings (dead-letter, lock, TTL).
    /// Ignored when Transport is RabbitMQ.
    /// </summary>
    public AzureServiceBusConsumerOptions AzureServiceBus { get; set; } = new();
}

public class ConsumerOptions
{
    /// <summary>
    /// Max number of requeues before the message is dead-lettered (e.g. 3).
    /// Use -1 for unlimited (not recommended in production).
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds before requeueing a failed message (backoff).
    /// </summary>
    public int RequeueDelayMs { get; set; } = 5000;

    /// <summary>
    /// Message receive/processing timeout in milliseconds.
    /// </summary>
    public int ReceiveTimeoutMs { get; set; } = 400;
}

public class AzureServiceBusConsumerOptions
{
    /// <summary>
    /// Max delivery count before ASB moves the message to the dead-letter queue.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 5;

    /// <summary>
    /// Lock duration in seconds (how long the message is locked per receive).
    /// </summary>
    public int LockDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to dead-letter messages when they expire (TTL).
    /// </summary>
    public bool DeadLetteringOnMessageExpiration { get; set; } = true;

    /// <summary>
    /// Default message time-to-live in days (0 = 1 minute per ASB default).
    /// </summary>
    public int DefaultMessageTimeToLiveDays { get; set; } = 3;
}
