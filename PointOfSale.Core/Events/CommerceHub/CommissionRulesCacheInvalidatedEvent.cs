namespace PointOfSale.Core.Events.CommerceHub;

/// <summary>
/// Event published when commission rules cache should be invalidated across all API instances.
/// This is a non-authoritative operational event used for cross-node cache coordination.
/// </summary>
public sealed class CommissionRulesCacheInvalidatedEvent : IntegrationEvent, INonAuthoritativeIntegrationEvent
{
    /// <summary>
    /// Reason for cache invalidation (e.g., "RuleCreated", "RuleUpdated", "RuleDeleted", "ManualInvalidation")
    /// </summary>
    public string Reason { get; init; } = "ManualInvalidation";

    /// <summary>
    /// Optional ID of the rule that triggered the invalidation
    /// </summary>
    public Guid? RuleId { get; init; }
}
