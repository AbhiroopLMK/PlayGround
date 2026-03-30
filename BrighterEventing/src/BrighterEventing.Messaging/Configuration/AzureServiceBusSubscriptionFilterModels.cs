namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Whether a filter condition targets a Service Bus <b>system</b> property (maps to
/// <see cref="Azure.Messaging.ServiceBus.Administration.CorrelationRuleFilter"/> fields) or a <b>custom</b>
/// application property (maps to <see cref="Azure.Messaging.ServiceBus.Administration.CorrelationRuleFilter.ApplicationProperties"/>).
/// </summary>
public enum AsbFilterPropertyKind
{
    /// <summary>
    /// Broker/system field; set <see cref="AsbSubscriptionFilterCondition.PropertyName"/> to the field (e.g. <c>subject</c>,
    /// <c>correlationId</c>, <c>sessionId</c>). Names are matched case-insensitively; optional <c>sys.</c> prefix is ignored.
    /// </summary>
    System,

    /// <summary>
    /// User-defined application property; <see cref="AsbSubscriptionFilterCondition.PropertyName"/> is required
    /// (e.g. <c>serviceBusEventType</c>, <c>cloudEvents:subject</c>).
    /// </summary>
    Custom
}

/// <summary>
/// One predicate in a rule. Multiple conditions on the same rule are combined with <c>AND</c> (CorrelationFilter semantics).
/// </summary>
public sealed class AsbSubscriptionFilterCondition
{
    public AsbFilterPropertyKind Kind { get; set; }

    /// <summary>
    /// Required for both <see cref="AsbFilterPropertyKind.System"/> and <see cref="AsbFilterPropertyKind.Custom"/>.
    /// System: e.g. <c>subject</c>, <c>contentType</c>, <c>correlationId</c>, <c>messageId</c>, <c>replyTo</c>,
    /// <c>replyToSessionId</c>, <c>sessionId</c>, <c>to</c>. Custom: full application property key.
    /// </summary>
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
