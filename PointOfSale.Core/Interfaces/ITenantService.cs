namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for tenant validation and management operations.
/// </summary>
public interface ITenantService
{
    /// <summary>
    /// Checks if a tenant exists and is active (Status = Active).
    /// </summary>
    /// <param name="tenantId">The tenant ID to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tenant exists and is active, false otherwise</returns>
    Task<bool> IsTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
