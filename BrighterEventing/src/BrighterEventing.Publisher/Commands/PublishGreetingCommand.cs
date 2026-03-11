using Paramore.Brighter;

namespace BrighterEventing.Publisher.Commands;

/// <summary>
/// Internal command to trigger publishing a GreetingMadeEvent via the outbox.
/// </summary>
public class PublishGreetingCommand : Command
{
    public PublishGreetingCommand() : base(Id.Random())
    {
    }

    public string Greeting { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
}
