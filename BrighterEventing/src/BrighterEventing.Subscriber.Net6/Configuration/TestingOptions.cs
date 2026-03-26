namespace BrighterEventing.Subscriber.Net6.Configuration;

public class TestingOptions
{
    public const string SectionName = "Testing";

    public int SimulateFailureCount { get; set; } = 0;
}
