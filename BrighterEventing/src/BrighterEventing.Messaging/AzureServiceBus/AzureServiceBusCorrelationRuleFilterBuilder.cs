using Azure.Messaging.ServiceBus.Administration;
using BrighterEventing.Messaging.Configuration;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Builds <see cref="CorrelationRuleFilter"/> from config conditions (AND within a rule). Rules are applied after host
/// startup by <see cref="AzureServiceBusCorrelationRulesHostedService"/>.
/// </summary>
public static class AzureServiceBusCorrelationRuleFilterBuilder
{
    /// <summary>Maps <see cref="AsbFilterPropertyKind.System"/> to <see cref="CorrelationRuleFilter"/> system fields.</summary>
    public static CorrelationRuleFilter BuildAndFilter(IReadOnlyList<AsbSubscriptionFilterCondition> conditions)
    {
        if (conditions == null || conditions.Count == 0)
            throw new ArgumentException("At least one filter condition is required.", nameof(conditions));

        var filter = new CorrelationRuleFilter();
        string? subjectSet = null;

        foreach (var c in conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Value))
                throw new InvalidOperationException("Each filter condition must have a non-empty Value.");

            switch (c.Kind)
            {
                case AsbFilterPropertyKind.System:
                    ApplySystemProperty(filter, c.PropertyName, c.Value, ref subjectSet);
                    break;

                case AsbFilterPropertyKind.Custom:
                    if (string.IsNullOrWhiteSpace(c.PropertyName))
                        throw new InvalidOperationException(
                            "Custom filter conditions must set PropertyName (e.g. serviceBusEventType or cloudEvents:subject).");
                    filter.ApplicationProperties[c.PropertyName.Trim()] = c.Value;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(c.Kind), c.Kind, null);
            }
        }

        return filter;
    }

    private static void ApplySystemProperty(
        CorrelationRuleFilter filter,
        string? propertyName,
        string value,
        ref string? subjectSet)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new InvalidOperationException(
                "System filter conditions must set PropertyName (e.g. subject, correlationId, sessionId).");

        var key = propertyName.Trim();
        if (key.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
            key = key[4..].TrimStart();

        if (key.Length == 0)
            throw new InvalidOperationException("System PropertyName is empty after trimming.");

        // Subject is the only field we historically allowed to merge with "label"; keep single-subject semantics.
        if (string.Equals(key, "subject", StringComparison.OrdinalIgnoreCase))
        {
            if (subjectSet != null && !string.Equals(subjectSet, value, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Correlation filters cannot set subject to different values in one rule; use separate rules.");
            subjectSet = value;
            filter.Subject = value;
            return;
        }

        if (string.Equals(key, "contenttype", StringComparison.OrdinalIgnoreCase))
        {
            filter.ContentType = value;
            return;
        }

        if (string.Equals(key, "correlationid", StringComparison.OrdinalIgnoreCase))
        {
            filter.CorrelationId = value;
            return;
        }

        if (string.Equals(key, "messageid", StringComparison.OrdinalIgnoreCase))
        {
            filter.MessageId = value;
            return;
        }

        if (string.Equals(key, "replyto", StringComparison.OrdinalIgnoreCase))
        {
            filter.ReplyTo = value;
            return;
        }

        if (string.Equals(key, "replytosessionid", StringComparison.OrdinalIgnoreCase))
        {
            filter.ReplyToSessionId = value;
            return;
        }

        if (string.Equals(key, "sessionid", StringComparison.OrdinalIgnoreCase))
        {
            filter.SessionId = value;
            return;
        }

        if (string.Equals(key, "to", StringComparison.OrdinalIgnoreCase))
        {
            filter.To = value;
            return;
        }

        throw new InvalidOperationException(
            $"Unknown system PropertyName '{propertyName}'. Supported: subject, contentType, correlationId, messageId, " +
            "replyTo, replyToSessionId, sessionId, to (optional sys. prefix). For broker label or user properties use Kind=Custom.");
    }
}
