using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces.Bulk;

public interface IBulkIdempotencyService
{
    TimeSpan ClaimLeaseDuration { get; }

    string OuterClaimOperation { get; }

    /// <summary>
    /// Deterministic per-product mutation identity for an item of a bulk
    /// execution. Shared by command building and recovery reconciliation so
    /// both derive the identical storage identity.
    /// </summary>
    string BuildPerProductIdempotencyKey(
        BulkOperationType operation,
        string idempotencyKey,
        Guid productId);

    BulkIdempotencyKeyValidationResult ValidateKey(string? idempotencyKey);

    string BuildFingerprint(
        BulkOperationType operation,
        IReadOnlyList<NormalizedBulkProductItem> normalizedItems,
        IReadOnlyDictionary<string, string?>? relevantOptions = null);

    Task<BulkIdempotencyClaim> TryClaimAsync(
        BulkOperationType operation,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken = default);

    Task<BulkIdempotencyResolution> TryResolveAsync(
        BulkOperationType operation,
        string idempotencyKey,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);

    void Complete(
        ProductRequestIdempotency? record,
        int statusCode,
        object? responsePayload);

    BulkOperationResult? ReadStoredResponse(ProductRequestIdempotency? record);

    string? ReadStoredFailureMessage(ProductRequestIdempotency? record);

    bool StoredResponseMatches(ProductRequestIdempotency? record, BulkOperationResult aggregate);

    bool IsInterruptedClaim(ProductRequestIdempotency? record);

    Task<bool> TryMarkInterruptedAsync(
        ProductRequestIdempotency record,
        CancellationToken cancellationToken = default);

    Task<bool> RenewClaimLeaseAsync(
        ProductRequestIdempotency record,
        CancellationToken cancellationToken = default);

    Task<BulkOperationResult?> TryCompleteAndVerifyAsync(
        ProductRequestIdempotency record,
        BulkOperationResult aggregate,
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeFailureAsync(
        ProductRequestIdempotency record,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovery reconciliation: resolves the durably completed accepted
    /// per-product authoring result for a bulk item's deterministic mutation
    /// identity. Returns null unless tenant, operation, outer-key-derived
    /// storage identity, product identity, completion status, and the stored
    /// serialized result all validate as an accepted committed mutation.
    /// </summary>
    Task<ProductAuthoringResult?> TryResolveCommittedBulkItemMutationAsync(
        BulkOperationType operation,
        string idempotencyKey,
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken = default);
}
