using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository interface for shipping method read operations from MongoDB.
/// 
/// ARCHITECTURE:
/// - MongoDB is the READ-ONLY source for Storefront
/// - All queries MUST filter by TenantId
/// - Upserts are idempotent by (TenantId, ShippingMethodId)
/// </summary>
public interface IShippingMethodReadRepository
{
    /// <summary>
    /// Gets all active shipping methods for a tenant, ordered by SortOrder.
    /// </summary>
    Task<List<ShippingMethodDocument>> GetActiveShippingMethodsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a shipping method by ID for a tenant.
    /// </summary>
    Task<ShippingMethodDocument?> GetByIdAsync(
        Guid tenantId,
        Guid shippingMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a shipping method document.
    /// Idempotent by (TenantId, ShippingMethodId).
    /// </summary>
    Task<bool> UpsertAsync(
        ShippingMethodDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a shipping method document only if the event is newer than the
    /// currently projected event metadata.
    /// </summary>
    Task<bool> UpsertIfFreshAsync(
        ShippingMethodDocument document,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a shipping method document.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid shippingMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an inactive tombstone only if the delete event is newer than the
    /// currently projected event metadata.
    /// </summary>
    Task<bool> TombstoneIfFreshAsync(
        Guid tenantId,
        Guid shippingMethodId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken = default);
}
