using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public interface IProductSqlAuthorityProjectionWriter
{
    Task<ProductSqlAuthorityProjectionWriteResult> ApplyAsync(
        ProductSqlAuthorityProjectionWriteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ProductSqlAuthorityProjectionWriteRequest(
    Guid TenantId,
    Guid ProductId,
    ProductDocument ProjectionSeed,
    Guid EventId,
    DateTime EventOccurredAtUtc,
    int EventVersion);

public enum ProductSqlAuthorityProjectionWriteDisposition
{
    Upserted,
    Tombstoned,
    SkippedStaleUpsert,
    SkippedStaleTombstone
}

public readonly record struct ProductSqlAuthorityProjectionWriteResult(
    ProductSqlAuthorityProjectionWriteDisposition Disposition)
{
    public bool Applied =>
        Disposition is ProductSqlAuthorityProjectionWriteDisposition.Upserted or
        ProductSqlAuthorityProjectionWriteDisposition.Tombstoned;

    public bool Tombstoned =>
        Disposition is ProductSqlAuthorityProjectionWriteDisposition.Tombstoned or
        ProductSqlAuthorityProjectionWriteDisposition.SkippedStaleTombstone;
}

public sealed class ProductSqlAuthorityProjectionWriter : IProductSqlAuthorityProjectionWriter
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IStorefrontProductProjectionMutationBoundary _mutationBoundary;

    public ProductSqlAuthorityProjectionWriter(
        ApplicationDbContext dbContext,
        IStorefrontProductProjectionMutationBoundary mutationBoundary)
    {
        _dbContext = dbContext;
        _mutationBoundary = mutationBoundary;
    }

    public async Task<ProductSqlAuthorityProjectionWriteResult> ApplyAsync(
        ProductSqlAuthorityProjectionWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(request));
        }

        if (request.ProductId == Guid.Empty)
        {
            throw new ArgumentException("ProductId is required.", nameof(request));
        }

        var projectionSeed = request.ProjectionSeed ?? throw new ArgumentNullException(nameof(request.ProjectionSeed));

        if (projectionSeed.TenantId != Guid.Empty && projectionSeed.TenantId != request.TenantId)
        {
            throw new InvalidOperationException("Projection seed TenantId does not match the requested tenant authority.");
        }

        if (projectionSeed.ProductId != Guid.Empty && projectionSeed.ProductId != request.ProductId)
        {
            throw new InvalidOperationException("Projection seed ProductId does not match the requested product authority.");
        }

        projectionSeed.TenantId = request.TenantId;
        projectionSeed.ProductId = request.ProductId;
        if (projectionSeed.CreatedAtUtc == default)
        {
            projectionSeed.CreatedAtUtc = request.EventOccurredAtUtc;
        }

        var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            projectionSeed,
            request.TenantId,
            cancellationToken);

        if (sqlProjection == null || !sqlProjection.IsActive || !sqlProjection.IsOnlineVisible)
        {
            var tombstone = await _mutationBoundary.ApplyAsync(
                new StorefrontProductProjectionMutation(
                    StorefrontProductProjectionMutationKind.ReviewedVisibilityTombstone,
                    request.TenantId,
                    request.ProductId,
                    Projection: null,
                    request.EventOccurredAtUtc,
                    request.EventVersion,
                    request.EventId,
                    ReviewReason: "SQL authority no longer proves storefront product visibility."),
                cancellationToken);

            return new ProductSqlAuthorityProjectionWriteResult(
                tombstone.Applied
                    ? ProductSqlAuthorityProjectionWriteDisposition.Tombstoned
                    : ProductSqlAuthorityProjectionWriteDisposition.SkippedStaleTombstone);
        }

        sqlProjection.LastEventId = request.EventId;
        sqlProjection.LastEventOccurredAtUtc = request.EventOccurredAtUtc;
        sqlProjection.LastEventVersion = request.EventVersion;
        sqlProjection.ReadModelName = ReadModelEvolutionRegistry.StorefrontProduct;
        sqlProjection.ReadModelSchemaVersion = ProductDocument.CurrentReadModelSchemaVersion;
        sqlProjection.ProjectionVersion = ProductDocument.CurrentProjectionVersion;

        var upsert = await _mutationBoundary.ApplyAsync(
            new StorefrontProductProjectionMutation(
                StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert,
                request.TenantId,
                request.ProductId,
                sqlProjection,
                request.EventOccurredAtUtc,
                request.EventVersion,
                request.EventId,
                ReviewReason: "SQL authority recomposed complete storefront ProductDocument."),
            cancellationToken);

        return new ProductSqlAuthorityProjectionWriteResult(
            upsert.Applied
                ? ProductSqlAuthorityProjectionWriteDisposition.Upserted
                : ProductSqlAuthorityProjectionWriteDisposition.SkippedStaleUpsert);
    }
}
