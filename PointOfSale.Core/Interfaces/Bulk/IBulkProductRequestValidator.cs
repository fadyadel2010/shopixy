using PointOfSale.Core.DTOs.Bulk;

namespace PointOfSale.Core.Interfaces.Bulk;

/// <summary>
/// Validates and normalizes a bulk product request (action-agnostic),
/// enforcing the raw contract, malformed/missing ID and version handling,
/// the 50-item ceiling, and duplicate-conflict normalization.
/// </summary>
public interface IBulkProductRequestValidator
{
    Task<BulkRequestValidationResult> ValidateAsync(
        BulkProductRequest? request,
        CancellationToken cancellationToken = default);
}
