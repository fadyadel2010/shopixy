using PointOfSale.Core.DTOs.Bulk;

namespace PointOfSale.Core.Interfaces.Bulk;

public interface IBulkProductLifecycleService
{
    Task<BulkLifecycleOutcome> ExecuteLifecycleAsync(
        BulkOperationType operation,
        BulkProductRequest? request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}

