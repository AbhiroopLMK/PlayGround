using System.Reflection;
using Paramore.Brighter;

internal static class Program
{
    private static void Main()
    {
        foreach (var c in typeof(MessageHeader).GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine("MessageHeader ctor: " + c);

        Console.WriteLine("--- Publication ---");
        foreach (var p in typeof(Publication).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine("Publication." + p.Name);

        var pubType = typeof(Paramore.Brighter.MessagingGateway.AzureServiceBus.AzureServiceBusPublication<>).MakeGenericType(typeof(DummyE));
        Console.WriteLine("--- AzureServiceBusPublication<DummyE> ---");
        foreach (var p in pubType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine("ASBPub." + p.Name + " : " + p.PropertyType.Name);

        var subType = typeof(Paramore.Brighter.MessagingGateway.AzureServiceBus.AzureServiceBusSubscription<>).MakeGenericType(typeof(DummyE));
        Console.WriteLine("--- AzureServiceBusSubscription<DummyE> ctor ---");
        foreach (var c in subType.GetConstructors())
            Console.WriteLine(c.ToString());
    }

    private sealed class DummyE : Event
    {
        public DummyE() : base(Id.Random()) { }
    }
}
