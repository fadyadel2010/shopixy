namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

/// <summary>
/// Raised when a bulk execution discovers it no longer owns its outer
/// idempotency claim lease, e.g. because an abandoned claim was recovered by
/// another caller. The current execution must stop without finalizing the
/// claim; the new owner is responsible for it.
/// </summary>
public sealed class BulkIdempotencyLeaseLostException(string message) : Exception(message);
