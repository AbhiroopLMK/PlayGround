using System.Reflection;
using Azure.Messaging.ServiceBus;
using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.AzureServiceBus;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.AzureServiceBusWrappers;
using Paramore.Brighter.Tasks;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Sends through Brighter's private <c>GetSenderAsync</c> but builds <see cref="ServiceBusMessage"/> with
/// <see cref="BrighterEventingServiceBusMessageConverter"/> so broker <see cref="ServiceBusMessage.Subject"/> is set.
/// </summary>
internal static class BrighterEventingAzureServiceBusProducerSend
{
    private static readonly MethodInfo GetSenderAsyncMethod =
        typeof(AzureServiceBusMessageProducer).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "GetSenderAsync" && m.GetParameters().Length == 1);

    private static readonly Func<RoutingKey, object> BuildGetSenderArgument = CreateGetSenderArgumentFactory();

    private static Func<RoutingKey, object> CreateGetSenderArgumentFactory()
    {
        var p = GetSenderAsyncMethod.GetParameters()[0].ParameterType;
        // Brighter versions differ: string channel name vs RoutingKey.
        if (p == typeof(string))
            return topic => topic.ToString();
        if (p == typeof(RoutingKey))
            return topic => topic;
        throw new InvalidOperationException(
            $"Unexpected GetSenderAsync parameter type {p.FullName}; expected string or RoutingKey.");
    }

    public static void SendWithDelaySync(AzureServiceBusMessageProducer inner, Message message, TimeSpan? delay = null) =>
        BrighterAsyncContext.Run(() => SendWithDelayAsync(inner, message, delay, CancellationToken.None));

    public static async Task SendWithDelayAsync(
        AzureServiceBusMessageProducer inner,
        Message message,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        delay ??= TimeSpan.Zero;
        if (message.Header.Topic is null)
            throw new ArgumentException("Topic must not be null.", nameof(message));

        var task = (Task)GetSenderAsyncMethod.Invoke(inner, new object[] { BuildGetSenderArgument(message.Header.Topic) })!;
        await task.ConfigureAwait(false);
        var sender = (IServiceBusSenderWrapper)task.GetType().GetProperty("Result")!.GetValue(task)!;

        var azureServiceBusMessage = BrighterEventingServiceBusMessageConverter.ConvertToServiceBusMessage(message);
        try
        {
            if (delay == TimeSpan.Zero)
                await sender.SendAsync(azureServiceBusMessage, cancellationToken).ConfigureAwait(false);
            else
            {
                var when = new DateTimeOffset(DateTime.UtcNow.Add(delay.Value));
                await sender.ScheduleMessageAsync(azureServiceBusMessage, when, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await sender.CloseAsync().ConfigureAwait(false);
        }
    }
}
