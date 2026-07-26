namespace PointOfSale.Core.Interfaces;

/// <summary>
/// PRODUCTION-GRADE MULTI-TENANCY
/// 
/// Marker interface for entities that are owned by a tenant.
/// Entities implementing this interface will have tenant isolation
/// enforced via EF Core global query filters.
/// 
/// CRITICAL RULES:
/// 1. TenantId is NEVER NULL - enforced at database level
/// 2. All queries are automatically filtered by TenantId
/// 3. No fallbacks, no optional tenant behavior
/// 4. Missing tenant context throws SecurityException
/// </summary>
public interface ITenantOwned
{
    /// <summary>
    /// The tenant that owns this entity.
    /// CRITICAL: This is NOT NULL at database level.
    /// All business data MUST be owned by exactly one tenant.
    /// </summary>
    Guid TenantId { get; set; }
}
