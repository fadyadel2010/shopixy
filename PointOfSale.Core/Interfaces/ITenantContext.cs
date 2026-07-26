namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Provides access to the current tenant context.
/// This abstraction enables tenant isolation at the service layer
/// without requiring explicit TenantId passing through all method signatures.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant ID. May be null in tenant-agnostic contexts
    /// (e.g., system administration, background jobs without tenant scope).
    /// </summary>
    Guid? TenantId { get; }
    
    /// <summary>
    /// Returns true if a tenant context is currently available
    /// </summary>
    bool HasTenant => TenantId.HasValue;
    
    /// <summary>
    /// Gets the tenant ID or throws if no tenant context is available.
    /// Use this in operations that MUST have a tenant context.
    /// </summary>
    Guid RequiredTenantId => TenantId ?? throw new InvalidOperationException("Tenant context is required but not available.");
}
