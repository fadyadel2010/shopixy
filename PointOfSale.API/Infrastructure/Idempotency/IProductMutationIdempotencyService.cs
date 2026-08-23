using PointOfSale.Core.Entities;

namespace PointOfSale.API.Infrastructure.Idempotency;

public interface IProductMutationIdempotencyService
{
    Task<ProductMutationIdempotencyClaim> TryClaimAsync(
        ProductMutationIdempotencyRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductRequestIdempotency?> TryResolveAsync(
        Guid tenantId,
        string key,
        string operation,
        CancellationToken cancellationToken = default);

    void Complete(
        ProductRequestIdempotency? record,
        int statusCode,
        object? responsePayload,
        Guid? productId = null,
        Guid? productVariantId = null);

    string ScopeKey(string scope, string idempotencyKey);

    Task<ProductMutationIdempotencyRecoveryResult> TryRecoverAbandonedClaimAsync(
        ProductMutationIdempotencyRequest request,
        TimeSpan leaseDuration,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryRenewClaimLeaseAsync(
        ProductRequestIdempotency record,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryWriteIncompleteClaimStateAsync(
        ProductRequestIdempotency record,
        string responseJson,
        CancellationToken cancellationToken = default);

    Task<ProductMutationIdempotencyCompletionResult> TryCompleteDurablyAsync(
        ProductRequestIdempotency record,
        int statusCode,
        object? responsePayload,
        DateTime? completedAtUtc = null,
        CancellationToken cancellationToken = default);
}

public sealed record ProductMutationIdempotencyRequest(
    Guid TenantId,
    string Key,
    string Operation,
    Guid ProductId,
    Guid? ProductVariantId,
    string RequestFingerprint);

public sealed record ProductMutationIdempotencyClaim(
    ProductRequestIdempotency? Record,
    ProductMutationIdempotencyReplay? Replay);

public sealed record ProductMutationIdempotencyReplay(
    ProductMutationIdempotencyReplayKind Kind,
    ProductRequestIdempotency? Record);

public enum ProductMutationIdempotencyReplayKind
{
    Completed,
    Conflict,
    InProgress
}

public enum ProductMutationIdempotencyRecoveryKind
{
    Recovered,
    NotRecoverable
}

public sealed record ProductMutationIdempotencyRecoveryResult(
    ProductMutationIdempotencyRecoveryKind Kind,
    ProductRequestIdempotency? Record);

public enum ProductMutationIdempotencyCompletionKind
{
    PersistedNow,
    AlreadyPersisted,
    LostOwnership
}

public sealed record ProductMutationIdempotencyCompletionResult(
    ProductMutationIdempotencyCompletionKind Kind,
    ProductRequestIdempotency? Record);
