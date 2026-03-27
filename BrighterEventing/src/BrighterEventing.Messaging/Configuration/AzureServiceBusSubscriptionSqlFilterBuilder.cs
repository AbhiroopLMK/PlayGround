using System.Linq;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Legacy SQL filter string builder (not used for subscription provisioning). Rules are applied as
/// <see cref="AzureServiceBus.AzureServiceBusCorrelationRuleFilterBuilder"/> via
/// <see cref="AzureServiceBus.AzureServiceBusCorrelationRulesHostedService"/>.
/// </summary>
[Obsolete("Subscription filters are correlation rules; this type is retained for reference only.")]
public static class AzureServiceBusSubscriptionSqlFilterBuilder
{
    public static string BuildOrFilter(IReadOnlyList<AsbSubscriptionFilterRule> rules)
    {
        if (rules == null || rules.Count == 0)
            throw new ArgumentException("At least one filter rule is required.", nameof(rules));

        var parts = new List<string>();
        foreach (var rule in rules)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0)
                throw new InvalidOperationException("Each AzureServiceBusFilterRules entry must have at least one Condition.");

            var inner = string.Join(" AND ", rule.Conditions.Select(BuildConditionSql));
            parts.Add($"({inner})");
        }

        return parts.Count == 1 ? parts[0] : string.Join(" OR ", parts);
    }

    private static string BuildConditionSql(AsbSubscriptionFilterCondition c)
    {
        if (string.IsNullOrWhiteSpace(c.Value))
            throw new InvalidOperationException("Each filter condition must have a non-empty Value.");

        var escaped = EscapeSqlString(c.Value);

        return c.Kind switch
        {
            AsbFilterPropertyKind.CloudEventsSubjectApplicationProperty =>
                $"[{BrighterMessagingBrokerRegistration.BrighterCloudEventsSubjectApplicationPropertyKey}] = '{escaped}'",

            AsbFilterPropertyKind.BrokerSubject =>
                $"subject = '{escaped}'",

            AsbFilterPropertyKind.BrokerLabel =>
                $"sys.Label = '{escaped}'",

            AsbFilterPropertyKind.Custom =>
                BuildCustomPropertySql(c.PropertyName, escaped),

            _ => throw new ArgumentOutOfRangeException(nameof(c.Kind), c.Kind, null)
        };
    }

    private static string BuildCustomPropertySql(string? propertyName, string escapedValue)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new InvalidOperationException("Custom filter conditions must set PropertyName (e.g. serviceBusEventType).");

        var key = propertyName.Trim();
        // Bracketed identifiers for user properties; see Azure SQL filter syntax.
        if (key.Contains(']', StringComparison.Ordinal) || key.Contains('[', StringComparison.Ordinal))
            throw new InvalidOperationException("PropertyName must not contain '[' or ']'.");

        return $"[{key}] = '{escapedValue}'";
    }

    private static string EscapeSqlString(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
