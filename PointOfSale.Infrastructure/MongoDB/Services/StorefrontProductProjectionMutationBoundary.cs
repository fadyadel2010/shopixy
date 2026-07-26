using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public interface IStorefrontProductProjectionMutationBoundary
{
    Task<StorefrontProductProjectionMutationResult> ApplyAsync(
        StorefrontProductProjectionMutation mutation,
        CancellationToken cancellationToken = default);
}

public enum StorefrontProductProjectionMutationKind
{
    ReviewedFullDocumentUpsert,
    ReviewedVisibilityTombstone
}

public sealed record StorefrontProductProjectionMutation(
    StorefrontProductProjectionMutationKind Kind,
    Guid TenantId,
    Guid ProductId,
    ProductDocument? Projection,
    DateTime EventOccurredAtUtc,
    int EventVersion,
    Guid EventId,
    string ReviewReason);

public readonly record struct StorefrontProductProjectionMutationResult(bool Applied);

public sealed class StorefrontProductProjectionMutationBoundary : IStorefrontProductProjectionMutationBoundary
{
    private readonly IProductReadRepository _repository;

    public StorefrontProductProjectionMutationBoundary(IProductReadRepository repository)
    {
        _repository = repository;
    }

    public async Task<StorefrontProductProjectionMutationResult> ApplyAsync(
        StorefrontProductProjectionMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonMutationAuthority(mutation);

        var applied = mutation.Kind switch
        {
            StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert =>
                await ApplyReviewedFullDocumentUpsertAsync(mutation, cancellationToken),
            StorefrontProductProjectionMutationKind.ReviewedVisibilityTombstone =>
                await _repository.TombstoneIfFreshAsync(
                    mutation.TenantId,
                    mutation.ProductId,
                    mutation.EventOccurredAtUtc,
                    mutation.EventVersion,
                    mutation.EventId,
                    cancellationToken),
            _ => throw new InvalidOperationException("STOREFRONT_PRODUCT_MUTATION_KIND_NOT_REVIEWED")
        };

        return new StorefrontProductProjectionMutationResult(applied);
    }

    private async Task<bool> ApplyReviewedFullDocumentUpsertAsync(
        StorefrontProductProjectionMutation mutation,
        CancellationToken cancellationToken)
    {
        var projection = mutation.Projection ??
            throw new InvalidOperationException("STOREFRONT_PRODUCT_UPSERT_REQUIRES_SQL_PROJECTION");

        if (projection.TenantId != mutation.TenantId ||
            projection.ProductId != mutation.ProductId)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_UPSERT_AUTHORITY_KEY_MISMATCH");
        }

        if (!projection.IsActive || !projection.IsOnlineVisible)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_UPSERT_REQUIRES_VISIBLE_SQL_PROJECTION");
        }

        return await _repository.UpsertAsync(projection, cancellationToken);
    }

    private static void ValidateCommonMutationAuthority(StorefrontProductProjectionMutation mutation)
    {
        if (mutation.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_MUTATION_TENANT_REQUIRED");
        }

        if (mutation.ProductId == Guid.Empty)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_MUTATION_PRODUCT_REQUIRED");
        }

        if (mutation.EventId == Guid.Empty)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_MUTATION_EVENT_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(mutation.ReviewReason))
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_MUTATION_REVIEW_REASON_REQUIRED");
        }
    }
}
