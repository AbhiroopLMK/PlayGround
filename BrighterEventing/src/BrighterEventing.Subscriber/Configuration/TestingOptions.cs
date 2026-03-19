namespace BrighterEventing.Subscriber.Configuration;

/// <summary>
/// Options for testing retry, backoff, and dead-letter behavior (e.g. simulate failures).
/// </summary>
public class TestingOptions
{
    public const string SectionName = "Testing";

    /// <summary>
    /// When &gt; 0, the first N handler invocations (across the handler that checks this) will throw
    /// so you can observe retries and eventual dead-letter. Set to 0 to disable.
    /// </summary>
    public int SimulateFailureCount { get; set; } = 0;
}
