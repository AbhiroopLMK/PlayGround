using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paramore.Brighter.ServiceActivator;

namespace BrighterEventing.Subscriber;

/// <summary>
/// Same behavior as <see cref="Paramore.Brighter.ServiceActivator.Extensions.Hosting.ServiceActivatorHostedService"/>,
/// but <see cref="StopAsync"/> honors the host <paramref name="cancellationToken"/>.
/// </summary>
/// <remarks>
/// Upstream returns <c>_dispatcher.End()</c> without linking to the stopping token, so graceful RabbitMQ teardown
/// (channel/connection close, especially over TLS to a remote broker) can block forever and ignore
/// <see cref="HostOptions.ShutdownTimeout"/>. Waiting with <see cref="Task.WaitAsync(CancellationToken)"/> lets the
/// generic host finish shutdown when the token fires; the dispatcher may still be closing in the background.
/// </remarks>
internal sealed class CancellableBrighterDispatcherHostedService : IHostedService
{
    private readonly ILogger<CancellableBrighterDispatcherHostedService> _logger;
    private readonly IDispatcher _dispatcher;

    public CancellableBrighterDispatcherHostedService(
        ILogger<CancellableBrighterDispatcherHostedService> logger,
        IDispatcher dispatcher)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting hosted service dispatcher");
        _dispatcher.Receive();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping hosted service dispatcher");
        var end = _dispatcher.End();
        if (end.IsCompleted)
        {
            await end.ConfigureAwait(false);
            return;
        }

        try
        {
            await end.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Dispatcher shutdown did not finish before the host stop token fired (see HostOptions.ShutdownTimeout). " +
                "The process will exit; RabbitMQ resources may still be closing on a background thread.");
        }
    }
}
