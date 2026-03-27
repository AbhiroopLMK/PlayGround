using Azure.Messaging.ServiceBus.Administration;
using BrighterEventing.Messaging.Configuration;

namespace BrighterEventing.Messaging.AzureServiceBus;

/// <summary>
/// Builds <see cref="CorrelationRuleFilter"/> from the same condition model used in config (AND within a rule).
/// Brighter only supports SQL strings on subscription create; correlation rules are applied via
/// <see cref="AzureServiceBusCorrelationRulesHostedService"/>.
/// </summary>
public static class AzureServiceBusCorrelationRuleFilterBuilder
{
    /// <summary>
    /// Maps <see cref="AsbFilterPropertyKind.BrokerSubject"/> and <see cref="AsbFilterPropertyKind.BrokerLabel"/> to
    /// <see cref="CorrelationRuleFilter.Subject"/> (serializes as broker label/subject in the management API).
    /// </summary>
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
                case AsbFilterPropertyKind.CloudEventsSubjectApplicationProperty:
                    filter.ApplicationProperties[BrighterMessagingBrokerRegistration.BrighterCloudEventsSubjectApplicationPropertyKey] =
                        c.Value;
                    break;

                case AsbFilterPropertyKind.BrokerSubject:
                case AsbFilterPropertyKind.BrokerLabel:
                    if (subjectSet != null && !string.Equals(subjectSet, c.Value, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "Correlation filters cannot express different broker subject and label in one rule with this SDK; use separate rules or the same value.");
                    subjectSet = c.Value;
                    filter.Subject = c.Value;
                    break;

                case AsbFilterPropertyKind.Custom:
                    if (string.IsNullOrWhiteSpace(c.PropertyName))
                        throw new InvalidOperationException("Custom filter conditions must set PropertyName (e.g. serviceBusEventType).");
                    filter.ApplicationProperties[c.PropertyName.Trim()] = c.Value;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(c.Kind), c.Kind, null);
            }
        }

        return filter;
    }
}
