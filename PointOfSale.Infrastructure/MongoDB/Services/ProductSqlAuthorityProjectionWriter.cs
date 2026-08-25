using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;
using PointOfSale.Infrastructure.Redis.Services;

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
    private readonly IStorefrontProductCacheInvalidator _cacheInvalidator;
    private readonly ILogger<ProductSqlAuthorityProjectionWriter> _logger;

    public ProductSqlAuthorityProjectionWriter(
        ApplicationDbContext dbContext,
        IStorefrontProductProjectionMutationBoundary mutationBoundary)
        : this(
            dbContext,
            mutationBoundary,
            NoopStorefrontProductCacheInvalidator.Instance,
            NullLogger<ProductSqlAuthorityProjectionWriter>.Instance)
    {
    }

    public ProductSqlAuthorityProjectionWriter(
        ApplicationDbContext dbContext,
        IStorefrontProductProjectionMutationBoundary mutationBoundary,
        ILogger<ProductSqlAuthorityProjectionWriter> logger)
        : this(
            dbContext,
            mutationBoundary,
            NoopStorefrontProductCacheInvalidator.Instance,
            logger)
    {
    }

    public ProductSqlAuthorityProjectionWriter(
        ApplicationDbContext dbContext,
        IStorefrontProductProjectionMutationBoundary mutationBoundary,
        IStorefrontProductCacheInvalidator cacheInvalidator,
        ILogger<ProductSqlAuthorityProjectionWriter> logger)
    {
        _dbContext = dbContext;
        _mutationBoundary = mutationBoundary;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
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

        ProductDocument? sqlProjection = null;
        try
        {
            sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                projectionSeed,
                request.TenantId,
                cancellationToken);

            LogProjectionWriterDecision(
                request,
                sqlProjection,
                admissionDecision: "PendingMutationBoundary",
                mutationFenceDecision: "Pending");

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

                var disposition = tombstone.Applied
                    ? ProductSqlAuthorityProjectionWriteDisposition.Tombstoned
                    : ProductSqlAuthorityProjectionWriteDisposition.SkippedStaleTombstone;
                LogProjectionWriterOutcome(
                    request,
                    disposition,
                    tombstone.Applied ? "TOMBSTONE" : "SKIPPED",
                    tombstone.Applied ? "Applied" : "MutationFenceRejected");

                await InvalidateProjectedProductCacheAsync(
                    request,
                    slug: null,
                    cancellationToken);

                return new ProductSqlAuthorityProjectionWriteResult(disposition);
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

            var upsertDisposition = upsert.Applied
                ? ProductSqlAuthorityProjectionWriteDisposition.Upserted
                : ProductSqlAuthorityProjectionWriteDisposition.SkippedStaleUpsert;
            LogProjectionWriterOutcome(
                request,
                upsertDisposition,
                upsert.Applied ? "UPSERT" : "IGNORED",
                upsert.Applied ? "Applied" : "MutationFenceRejected");

            await InvalidateProjectedProductCacheAsync(
                request,
                sqlProjection.Slug,
                cancellationToken);

            return new ProductSqlAuthorityProjectionWriteResult(upsertDisposition);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PROJECTION_EVENT_TRACE | Stage=ProjectionWriter | Outcome=EXCEPTION | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | EventVersion={EventVersion} | OverlayResult={OverlayResult} | AdmissionDecision={AdmissionDecision} | VisibilityDecision={VisibilityDecision} | MutationFenceDecision={MutationFenceDecision} | Timestamp={Timestamp:O}",
                request.ProductId,
                request.TenantId,
                request.EventId,
                request.EventVersion,
                sqlProjection is null ? "Null" : "Projected",
                "Exception",
                ResolveVisibilityDecision(sqlProjection),
                "Exception",
                DateTime.UtcNow);
            throw;
        }
    }

    private Task InvalidateProjectedProductCacheAsync(
        ProductSqlAuthorityProjectionWriteRequest request,
        string? slug,
        CancellationToken cancellationToken) =>
        _cacheInvalidator.InvalidateProductAsync(
            request.TenantId,
            request.ProductId,
            slug,
            invalidateHomepage: true,
            cancellationToken);

    private void LogProjectionWriterDecision(
        ProductSqlAuthorityProjectionWriteRequest request,
        ProductDocument? sqlProjection,
        string admissionDecision,
        string mutationFenceDecision)
    {
        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=ProjectionWriter | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | EventVersion={EventVersion} | OverlayResult={OverlayResult} | AdmissionDecision={AdmissionDecision} | VisibilityDecision={VisibilityDecision} | MutationFenceDecision={MutationFenceDecision} | Timestamp={Timestamp:O}",
            request.ProductId,
            request.TenantId,
            request.EventId,
            request.EventVersion,
            sqlProjection is null ? "Null" : "Projected",
            admissionDecision,
            ResolveVisibilityDecision(sqlProjection),
            mutationFenceDecision,
            DateTime.UtcNow);
    }

    private void LogProjectionWriterOutcome(
        ProductSqlAuthorityProjectionWriteRequest request,
        ProductSqlAuthorityProjectionWriteDisposition disposition,
        string outcome,
        string mutationFenceDecision)
    {
        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=ProjectionWriter | Outcome={Outcome} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | EventVersion={EventVersion} | Disposition={Disposition} | MutationFenceDecision={MutationFenceDecision} | Timestamp={Timestamp:O}",
            outcome,
            request.ProductId,
            request.TenantId,
            request.EventId,
            request.EventVersion,
            disposition,
            mutationFenceDecision,
            DateTime.UtcNow);
    }

    private static string ResolveVisibilityDecision(ProductDocument? sqlProjection)
    {
        if (sqlProjection is null)
        {
            return "RejectedBySqlOverlay";
        }

        return sqlProjection is { IsActive: true, IsOnlineVisible: true }
            ? "Visible"
            : "NotVisible";
    }

    private sealed class NoopStorefrontProductCacheInvalidator : IStorefrontProductCacheInvalidator
    {
        public static readonly NoopStorefrontProductCacheInvalidator Instance = new();

        public Task InvalidateProductAsync(
            Guid tenantId,
            Guid productId,
            string? slug = null,
            bool invalidateHomepage = true,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateTenantAsync(
            Guid tenantId,
            bool invalidateHomepage = true,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
