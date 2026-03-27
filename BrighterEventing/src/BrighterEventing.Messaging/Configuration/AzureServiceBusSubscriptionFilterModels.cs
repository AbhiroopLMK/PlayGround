namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Which message field a subscription filter condition targets (mapped to <c>CorrelationRuleFilter</c>).
/// </summary>
public enum AsbFilterPropertyKind
{
    /// <summary>Brighter / CloudEvents application property <c>cloudEvents:subject</c> (maps from <see cref="Paramore.Brighter.MessageHeader.Subject"/>).</summary>
    CloudEventsSubjectApplicationProperty,

    /// <summary>Broker system property <c>subject</c> (maps to CorrelationFilter "Subject" in the portal).</summary>
    BrokerSubject,

    /// <summary>Broker system property <c>sys.Label</c>.</summary>
    BrokerLabel,

    /// <summary>User-defined application property; set <see cref="AsbSubscriptionFilterCondition.PropertyName"/> (e.g. <c>serviceBusEventType</c>).</summary>
    Custom
}

/// <summary>
/// One predicate in a rule. Multiple conditions on the same rule are combined with <c>AND</c> (CorrelationFilter semantics).
/// </summary>
public sealed class AsbSubscriptionFilterCondition
{
    public AsbFilterPropertyKind Kind { get; set; }

    /// <summary>Required when <see cref="Kind"/> is <see cref="AsbFilterPropertyKind.Custom"/>.</summary>
    public string? PropertyName { get; set; }

    public string Value { get; set; } = "";
}

/// <summary>
/// One named correlation rule on the subscription. Multiple rules combine as OR (any rule may match).
/// </summary>
public sealed class AsbSubscriptionFilterRule
{
    /// <summary>Optional label (for documentation; not sent to Brighter's single-rule create).</summary>
    public string? Name { get; set; }

    public List<AsbSubscriptionFilterCondition> Conditions { get; set; } = new();
}
