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
