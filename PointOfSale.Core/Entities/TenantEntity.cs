namespace PointOfSale.Core.Entities;

/// <summary>
/// Abstract base class for entities owned by a tenant.
/// 
/// ARCHITECTURAL RULES (PRODUCTION-GRADE MULTI-TENANCY):
/// 1. ALL business entities MUST inherit from TenantEntity
/// 2. TenantId is NEVER NULL - enforced at database level
/// 3. Global query filters automatically isolate tenant data
/// 4. No exceptions, no fallbacks, no optional tenant behavior
/// 
/// This class ensures Shopify-level tenant isolation.
/// </summary>
public abstract class TenantEntity : BaseEntity
{
    /// <summary>
    /// The tenant that owns this entity.
    /// CRITICAL: This is NOT NULL at database level.
    /// All queries are automatically filtered by this field.
    /// </summary>
    public Guid TenantId { get; set; }
}
