namespace PointOfSale.Core.Events;

/// <summary>
/// Event published when a tenant's currency is changed.
/// 
/// PHASE 10.4.X: Currency Authority Lockdown
/// 
/// This event is the ONLY mechanism for currency synchronization.
/// Consumers MUST:
/// - Update MongoDB storefront_configs.currency
/// - Invalidate Redis caches
/// - Re-read current Tenant.Currency from SQL before applying visibility updates
/// 
/// ARCHITECTURAL RULES:
/// - Tenant.Currency is the ONLY source of truth (PostgreSQL)
/// - MongoDB is a projection ONLY
/// - Redis cart currency is a derived snapshot
/// - Published on: Tenant creation, Tenant currency update
/// </summary>
public class TenantCurrencyChangedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    /// <summary>
    /// The new ISO 4217 currency code (e.g., "EGP", "USD", "SAR").
    /// </summary>
    public string Currency { get; set; } = string.Empty;
    
    /// <summary>
    /// The previous currency (null if this is initial creation).
    /// </summary>
    public string? PreviousCurrency { get; set; }
    
    /// <summary>
    /// Tenant slug for cache invalidation.
    /// </summary>
    public string? TenantSlug { get; set; }
}

