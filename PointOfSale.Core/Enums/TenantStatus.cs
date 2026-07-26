namespace PointOfSale.Core.Enums;

/// <summary>
/// Tenant lifecycle status as defined in Phase 8.0
/// </summary>
public enum TenantStatus
{
    /// <summary>
    /// Registration complete, bootstrap in progress.
    /// POS and Storefront access blocked.
    /// </summary>
    PendingSetup,
    
    /// <summary>
    /// Fully operational. All features enabled.
    /// </summary>
    Active,
    
    /// <summary>
    /// Frozen due to billing, policy, or admin action.
    /// POS read-only, Storefront blocked, no data mutations.
    /// </summary>
    Suspended,
    
    /// <summary>
    /// Soft-deleted. Data retained for recovery period.
    /// All access blocked.
    /// </summary>
    Deleted
}
