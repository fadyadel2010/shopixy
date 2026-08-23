using PointOfSale.Core.DTOs.Bulk;

namespace PointOfSale.Core.Interfaces.Bulk;

/// <summary>
/// Reusable application preparation service for bulk product operations.
/// Orchestrates validation, tenant-scoped set-based loading, and per-item
/// preparation context production without mutating or persisting anything.
/// </summary>
public interface IBulkPreparationService
{
    /// <summary>
    /// Validates, loads, and prepares the request. Never mutates products
    /// and never claims/persists an idempotency record.
    /// </summary>
    Task<BulkPreparationOutcome> PrepareAsync(
        BulkProductRequest? request,
        CancellationToken cancellationToken = default);
}
