using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BrighterEventing.Messaging.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Brighter's subscription creation only supports a single <see cref="SqlRuleFilter"/> named <c>sqlFilter</c>.
/// This service clears that path by leaving <see cref="Paramore.Brighter.MessagingGateway.AzureServiceBus.AzureServiceBusSubscriptionConfiguration.SqlFilter"/>
/// empty so Brighter creates the default rule, then replaces subscription rules with <see cref="CorrelationRuleFilter"/>
/// entries that match <see cref="SubscriptionBinding.AzureServiceBusFilterRules"/> (OR across rules).
/// </summary>
/// <remarks>
/// Work is scheduled on the thread pool so <see cref="IHostedService.StartAsync"/> returns immediately. Using the
/// host startup <c>CancellationToken</c> for long polls caused <see cref="TaskCanceledException"/> (timeouts / early
/// cancellation). Sync uses <see cref="IHostApplicationLifetime.ApplicationStopping"/> instead.
/// </remarks>
public sealed class AzureServiceBusCorrelationRulesHostedService : IHostedService
{
    private const int MaxRuleNameLength = 50;

    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureServiceBusCorrelationRulesHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public AzureServiceBusCorrelationRulesHostedService(
        IConfiguration configuration,
        ILogger<AzureServiceBusCorrelationRulesHostedService> logger,
        IHostApplicationLifetime lifetime)
    {
        _configuration = configuration;
        _logger = logger;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunSyncAsync(_lifetime.ApplicationStopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunSyncAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteSyncAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Service Bus correlation rule sync failed.");
        }
    }

    private async Task ExecuteSyncAsync(CancellationToken stoppingToken)
    {
        var options = BrighterSubscriberServiceCollectionExtensions.BindSubscriberOptions(_configuration);
        if (options.Transport != BrokerType.AzureServiceBus)
            return;

        if (string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString))
        {
            _logger.LogDebug("Skipping ASB correlation rule sync: no connection string.");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var client = new ServiceBusAdministrationClient(options.AzureServiceBus.ConnectionString);
        try
        {
            await SyncCorrelationRulesAsync(client, options, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            if (client is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SyncCorrelationRulesAsync(
        ServiceBusAdministrationClient client,
        BrighterSubscriberOptions options,
        CancellationToken cancellationToken)
    {
        var groups = options.Subscriptions.GroupBy(b => (
            Topic: BrighterMessagingBrokerRegistration.ResolveAzureServiceBusTopicPath(b, options),
            Subscription: BrighterMessagingBrokerRegistration.ResolveAzureServiceBusSubscriptionName(b, options)));

        foreach (var g in groups)
        {
            var topic = g.Key.Topic;
            var subscription = g.Key.Subscription;
            var mergedRules = BuildMergedRules(g);
            if (mergedRules.Count == 0)
                continue;

            try
            {
                if (!await WaitForSubscriptionAsync(client, topic, subscription, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogWarning(
                        "Subscription {Subscription} on topic {Topic} was not found after waiting; skipping correlation rule sync.",
                        subscription,
                        topic);
                    continue;
                }

                for (var i = 0; i < mergedRules.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rule = mergedRules[i];
                    if (rule.Conditions == null || rule.Conditions.Count == 0)
                        throw new InvalidOperationException($"Rule '{rule.Name}' must have at least one Condition.");

                    var filter = AzureServiceBusCorrelationRuleFilterBuilder.BuildAndFilter(rule.Conditions);
                    var ruleName = SanitizeRuleName(rule.Name, i);
                    await UpsertCorrelationRuleAsync(client, topic, subscription, ruleName, filter, cancellationToken)
                        .ConfigureAwait(false);
                }

                await TryDeleteRuleAsync(client, topic, subscription, "sqlFilter", cancellationToken).ConfigureAwait(false);
                if (mergedRules.Count > 0)
                    await TryDeleteRuleAsync(client, topic, subscription, "$Default", cancellationToken)
                        .ConfigureAwait(false);

                _logger.LogInformation(
                    "Synchronized {Count} correlation rule(s) on subscription {Subscription} (topic {Topic}).",
                    mergedRules.Count,
                    subscription,
                    topic);
            }
            catch (Exception ex) when (ex is OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync correlation rules for subscription {Subscription} on topic {Topic}.", subscription, topic);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Brighter may create the subscription slightly after we start; poll with retries for HTTP timeouts.
    /// </summary>
    private static async Task<bool> WaitForSubscriptionAsync(
        ServiceBusAdministrationClient client,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await client.SubscriptionExistsAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false))
                    return true;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static List<AsbSubscriptionFilterRule> BuildMergedRules(IGrouping<(string Topic, string Subscription), SubscriptionBinding> g)
    {
        var list = new List<AsbSubscriptionFilterRule>();
        foreach (var binding in g)
        {
            if (binding.AzureServiceBusFilterRules is { Count: > 0 })
            {
                list.AddRange(binding.AzureServiceBusFilterRules);
            }
            else
            {
                list.Add(new AsbSubscriptionFilterRule
                {
                    Name = $"legacy_{SanitizeRuleName(binding.EventType, 0)}",
                    Conditions = new List<AsbSubscriptionFilterCondition>
                    {
                        new()
                        {
                            Kind = AsbFilterPropertyKind.Custom,
                            PropertyName = BrighterMessagingBrokerRegistration.BrighterCloudEventsSubjectApplicationPropertyKey,
                            Value = binding.RoutingKey.Trim()
                        }
                    }
                });
            }
        }

        return list;
    }

    private static async Task UpsertCorrelationRuleAsync(
        ServiceBusAdministrationClient client,
        string topicName,
        string subscriptionName,
        string ruleName,
        CorrelationRuleFilter filter,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteRuleAsync(topicName, subscriptionName, ruleName, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
        }

        await client
            .CreateRuleAsync(
                topicName,
                subscriptionName,
                new CreateRuleOptions(ruleName) { Filter = filter },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task TryDeleteRuleAsync(
        ServiceBusAdministrationClient client,
        string topicName,
        string subscriptionName,
        string ruleName,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteRuleAsync(topicName, subscriptionName, ruleName, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
        }
    }

    private static string SanitizeRuleName(string? name, int index)
    {
        var raw = string.IsNullOrWhiteSpace(name) ? $"rule_{index}" : name.Trim();
        var sb = new System.Text.StringBuilder();
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '-' or '_')
                sb.Append(c);
            else if (char.IsWhiteSpace(c))
                sb.Append('_');
        }

        var s = sb.ToString();
        if (s.Length > MaxRuleNameLength)
            s = s[..MaxRuleNameLength];
        return string.IsNullOrEmpty(s) ? $"rule_{index}" : s;
    }
}
