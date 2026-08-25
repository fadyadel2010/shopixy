using Elasticsearch.Net;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Promotions;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for syncing promotion data to Elasticsearch.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// CRITICAL: This consumer keeps Elasticsearch in sync with MongoDB promotion data.
/// Without this, promotion-aware ranking would use stale data.
/// 
/// Event Triggers:
/// - PromotionCreatedEvent → Update affected product(s) in ES
/// - PromotionUpdatedEvent → Update affected product(s) in ES
/// - PromotionDeletedEvent → Clear promotion fields from affected product(s) in ES
/// - PromotionExpiredEvent → Clear promotion fields from affected product(s) in ES
/// 
/// Data Flow:
/// 1. Promotion event arrives
/// 2. If scope is "product", fetch the product projection from MongoDB
/// 3. Update Elasticsearch product document with promotion fields
/// 
/// Error Handling:
/// - Transient ES errors: Let MassTransit retry
/// - Permanent ES errors: throw so MassTransit retry/error transport owns recovery
/// </summary>
public class ProductPromotionSearchIndexConsumer :
    IConsumer<PromotionCreatedEvent>,
    IConsumer<PromotionUpdatedEvent>,
    IConsumer<PromotionDeletedEvent>,
    IConsumer<PromotionExpiredEvent>
{
    private readonly IUnifiedSearchIndexService _indexService;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly ILogger<ProductPromotionSearchIndexConsumer> _logger;
    private readonly ApplicationDbContext _dbContext;

    private const string ProductsCollectionName = "products";
    private const string ConsumerName = nameof(ProductPromotionSearchIndexConsumer);

    public ProductPromotionSearchIndexConsumer(
        IUnifiedSearchIndexService indexService,
        IMongoDatabase database,
        ILogger<ProductPromotionSearchIndexConsumer> logger,
        ApplicationDbContext dbContext)
    {
        _indexService = indexService;
        _productCollection = database.GetCollection<ProductDocument>(ProductsCollectionName);
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Handle PromotionCreatedEvent - Update product in ES with promotion data
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionCreatedEvent> context)
    {
        var @event = context.Message;

        if (!@event.TenantId.HasValue)
        {
            _logger.LogWarning(
                "PromotionCreatedEvent skipped for ES (no TenantId) | PromotionId: {PromotionId}",
                @event.PromotionId);
            return;
        }

        await WithFenceAsync(
            @event,
            typeof(PromotionCreatedEvent).FullName ?? nameof(PromotionCreatedEvent),
            @event.PromotionId,
            () => RefreshPromotionSearchFromSqlAuthorityAsync(
                @event.TenantId.Value,
                @event.PromotionId,
                @event.Version,
                allowMissingSqlPromotion: false,
                fallbackScope: @event.Scope,
                fallbackAppliesToId: @event.AppliesToId,
                context.CancellationToken),
            context.CancellationToken);
    }

    /// <summary>
    /// Handle PromotionUpdatedEvent - Update product in ES with new promotion data
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionUpdatedEvent> context)
    {
        var @event = context.Message;

        if (!@event.TenantId.HasValue)
        {
            _logger.LogWarning(
                "PromotionUpdatedEvent skipped for ES (no TenantId) | PromotionId: {PromotionId}",
                @event.PromotionId);
            return;
        }

        await WithFenceAsync(
            @event,
            typeof(PromotionUpdatedEvent).FullName ?? nameof(PromotionUpdatedEvent),
            @event.PromotionId,
            () => RefreshPromotionSearchFromSqlAuthorityAsync(
                @event.TenantId.Value,
                @event.PromotionId,
                @event.Version,
                allowMissingSqlPromotion: false,
                fallbackScope: @event.Scope,
                fallbackAppliesToId: @event.AppliesToId,
                context.CancellationToken),
            context.CancellationToken);
    }

    /// <summary>
    /// Handle PromotionDeletedEvent - Clear promotion data from product in ES
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionDeletedEvent> context)
    {
        var @event = context.Message;

        if (!@event.TenantId.HasValue)
        {
            _logger.LogWarning(
                "PromotionDeletedEvent skipped for ES (no TenantId) | PromotionId: {PromotionId}",
                @event.PromotionId);
            return;
        }

        // For deletes, we need to re-sync all products that had this promotion
        // Since we don't know which products were affected, sync all products with this ActivePromotionId
        await WithFenceAsync(
            @event,
            typeof(PromotionDeletedEvent).FullName ?? nameof(PromotionDeletedEvent),
            @event.PromotionId,
            () => RefreshPromotionSearchFromSqlAuthorityAsync(
                @event.TenantId.Value,
                @event.PromotionId,
                @event.Version,
                allowMissingSqlPromotion: true,
                fallbackScope: @event.Scope,
                fallbackAppliesToId: @event.AppliesToId,
                context.CancellationToken),
            context.CancellationToken);
    }

    /// <summary>
    /// Handle PromotionExpiredEvent - Clear promotion data from product in ES
    /// </summary>
    public async Task Consume(ConsumeContext<PromotionExpiredEvent> context)
    {
        var @event = context.Message;

        if (!@event.TenantId.HasValue)
        {
            _logger.LogWarning(
                "PromotionExpiredEvent skipped for ES (no TenantId) | PromotionId: {PromotionId}",
                @event.PromotionId);
            return;
        }

        // Re-sync all products that had this promotion
        await WithFenceAsync(
            @event,
            typeof(PromotionExpiredEvent).FullName ?? nameof(PromotionExpiredEvent),
            @event.PromotionId,
            () => RefreshPromotionSearchFromSqlAuthorityAsync(
                @event.TenantId.Value,
                @event.PromotionId,
                @event.Version,
                allowMissingSqlPromotion: false,
                fallbackScope: @event.Scope,
                fallbackAppliesToId: @event.AppliesToId,
                context.CancellationToken),
            context.CancellationToken);
    }

    private async Task WithFenceAsync(
        IntegrationEvent @event,
        string eventType,
        Guid entityId,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        ProjectionConsumerEventFence? fence = null;
        try
        {
            fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                ConsumerName,
                eventType,
                entityId,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "Duplicate or active promotion search replay collapsed before Elasticsearch side effects | EventId: {EventId} | EventType: {EventType} | EntityId: {EntityId}",
                    @event.EventId,
                    eventType,
                    entityId);
                return;
            }

            await action();

            if (fence != null)
            {
                await fence.CompleteAsync(@event, ConsumerName, eventType, entityId, cancellationToken);
            }
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    private async Task RefreshPromotionSearchFromSqlAuthorityAsync(
        Guid tenantId,
        Guid promotionId,
        int eventVersion,
        bool allowMissingSqlPromotion,
        string? fallbackScope,
        Guid? fallbackAppliesToId,
        CancellationToken cancellationToken)
    {
        var authority = await LoadSqlPromotionAuthorityAsync(tenantId, promotionId, eventVersion, cancellationToken);
        if (authority.Exists && !authority.VersionAccepted)
        {
            _logger.LogWarning(
                "Promotion search event rejected as stale before Elasticsearch side effects | TenantId: {TenantId} | PromotionId: {PromotionId} | EventVersion: {EventVersion}",
                tenantId,
                promotionId,
                eventVersion);
            return;
        }

        if (!authority.Exists && !allowMissingSqlPromotion)
        {
            _logger.LogWarning(
                "Promotion search event rejected by SQL authority before Elasticsearch side effects | TenantId: {TenantId} | PromotionId: {PromotionId} | EventVersion: {EventVersion}",
                tenantId,
                promotionId,
                eventVersion);
            return;
        }

        var scope = authority.Promotion?.Scope.ToString().ToLowerInvariant() ?? fallbackScope;
        var appliesToId = authority.Promotion?.AppliesToId ?? fallbackAppliesToId;
        if (!string.Equals(scope, "product", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Promotion search event skipped for non-product scope after SQL recomposition | TenantId: {TenantId} | PromotionId: {PromotionId} | Scope: {Scope}",
                tenantId,
                promotionId,
                scope);
            return;
        }

        if (appliesToId.HasValue)
        {
            await RefreshProductPromotionSearchFromSqlAsync(
                tenantId,
                appliesToId.Value,
                promotionId,
                cancellationToken);
            return;
        }

        await SyncAllProductPromotionSearchFromSqlAsync(tenantId, promotionId, cancellationToken);
    }

    private async Task RefreshProductPromotionSearchFromSqlAsync(
        Guid tenantId,
        Guid productId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Syncing product promotion to Elasticsearch from SQL authority | TenantId: {TenantId} | ProductId: {ProductId} | PromotionId: {PromotionId}",
                tenantId, productId, promotionId);

            var product = await LoadProductDocumentAsync(tenantId, productId, cancellationToken);
            if (product == null)
            {
                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {productId} is not available before promotion indexing.");
            }

            var sqlOverlay = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                product,
                tenantId,
                cancellationToken);
            if (sqlOverlay == null || !sqlOverlay.IsActive || !sqlOverlay.IsOnlineVisible || !sqlOverlay.IsSearchable)
            {
                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    tenantId,
                    productId,
                    eventId: null,
                    cancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch promotion delete failed for product {productId}.");
                }

                return;
            }

            var winningPromotion = await LoadWinningSqlProductPromotionAsync(
                tenantId,
                productId,
                cancellationToken);
            PromotionVisibilityProjectionSupport.ApplyProductPromotionOverlay(sqlOverlay, winningPromotion);

            var document = MapToSearchDocument(sqlOverlay);
            var success = await _indexService.IndexProductAsync(document, cancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "Product promotion synced to Elasticsearch | ProductId: {ProductId} | HasActivePromotion: {HasPromotion} | DiscountValue: {DiscountValue}",
                    productId, document.HasActivePromotion, document.PromotionDiscountValue);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to sync product promotion to Elasticsearch | ProductId: {ProductId}",
                    productId);
                throw new SearchIndexSyncException(
                    $"Elasticsearch promotion index failed for product {productId}.");
            }
        }
        catch (ElasticsearchClientException ex) when (IsTransientError(ex))
        {
            _logger.LogWarning(ex,
                "Transient Elasticsearch error syncing product promotion - will retry | ProductId: {ProductId}",
                productId);
            throw; // Let MassTransit retry
        }
        catch (ProjectionNotReadyException)
        {
            throw;
        }
        catch (SearchIndexSyncException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error syncing product promotion to Elasticsearch | ProductId: {ProductId} | TenantId: {TenantId}",
                productId, tenantId);
            throw new SearchIndexSyncException(
                $"Elasticsearch promotion sync failed for product {productId}.", ex);
        }
    }

    /// <summary>
    /// Syncs all products that had a specific promotion (for delete/expire events).
    /// </summary>
    private async Task SyncProductsWithPromotionAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        await SyncAllProductPromotionSearchFromSqlAsync(tenantId, promotionId, cancellationToken);
    }

    private async Task SyncAllProductPromotionSearchFromSqlAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Syncing product promotion search from SQL authority for tenant scope | TenantId: {TenantId} | PromotionId: {PromotionId}",
                tenantId, promotionId);

            var productIds = await _dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product =>
                    product.TenantId == tenantId &&
                    !product.IsDeleted &&
                    product.IsActive &&
                    product.IsOnlineVisible &&
                    product.IsSearchable &&
                    product.PublishStatus == PublishStatus.Published)
                .OrderBy(product => product.Id)
                .Select(product => product.Id)
                .ToListAsync(cancellationToken);

            var syncCount = 0;

            var failedProductIds = new List<Guid>();

            foreach (var productId in productIds)
            {
                try
                {
                    await RefreshProductPromotionSearchFromSqlAsync(
                        tenantId,
                        productId,
                        promotionId,
                        cancellationToken);
                    syncCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to sync product to ES during promotion cleanup | ProductId: {ProductId} | TenantId: {TenantId} | PromotionId: {PromotionId}",
                        productId, tenantId, promotionId);
                    failedProductIds.Add(productId);
                }
            }

            if (failedProductIds.Count > 0)
            {
                const int maxIdsToLog = 50;
                var idsToLog = failedProductIds.Take(maxIdsToLog).Select(id => id.ToString());
                var idsString = string.Join(", ", idsToLog);
                var moreCount = failedProductIds.Count - maxIdsToLog;
                
                if (moreCount > 0)
                {
                    _logger.LogWarning(
                        "Some products failed to sync during promotion cleanup | TenantId: {TenantId} | PromotionId: {PromotionId} | FailedCount: {FailedCount} | FailedProductIds: {FailedProductIds} (and {MoreCount} more)",
                        tenantId, promotionId, failedProductIds.Count, idsString, moreCount);
                }
                else
                {
                    _logger.LogWarning(
                        "Some products failed to sync during promotion cleanup | TenantId: {TenantId} | PromotionId: {PromotionId} | FailedCount: {FailedCount} | FailedProductIds: {FailedProductIds}",
                        tenantId, promotionId, failedProductIds.Count, idsString);
                }

                throw new SearchIndexSyncException(
                    $"Elasticsearch promotion cleanup failed for {failedProductIds.Count} product(s) in tenant {tenantId}.");
            }

            _logger.LogInformation(
                "Completed syncing products after promotion delete/expire | TenantId: {TenantId} | PromotionId: {PromotionId} | SyncedCount: {Count}",
                tenantId, promotionId, syncCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error during bulk product promotion search sync - retrying | TenantId: {TenantId} | PromotionId: {PromotionId}",
                tenantId, promotionId);
            throw;
        }
    }

    private async Task<ProductDocument?> LoadProductDocumentAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId));

        return await _productCollection.Find(filter).FirstOrDefaultAsync(cancellationToken)
            ?? new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId
            };
    }

    private async Task<PromotionSearchAuthority> LoadSqlPromotionAuthorityAsync(
        Guid tenantId,
        Guid promotionId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var promotion = await _dbContext.Promotions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == promotionId,
                cancellationToken);
        if (promotion == null)
        {
            return new PromotionSearchAuthority(null, Exists: false, VersionAccepted: false);
        }

        var validation = ProjectionAuthorityValidator.ValidateSqlVersion(
            ProductEventVersionHelper.ToProductEventVersion(promotion.ProjectionMutationVersion),
            eventVersion,
            requireExactVersion: true);

        return new PromotionSearchAuthority(
            promotion,
            Exists: true,
            VersionAccepted: validation.IsAccepted);
    }

    private async Task<PointOfSale.Core.Entities.Promotion?> LoadWinningSqlProductPromotionAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var promotions = await _dbContext.Promotions
            .AsNoTracking()
            .Where(promotion =>
                promotion.TenantId == tenantId &&
                !promotion.IsDeleted &&
                promotion.IsActive &&
                promotion.Scope == PointOfSale.Core.Enums.PromotionScope.Product &&
                (!promotion.AppliesToId.HasValue || promotion.AppliesToId == productId))
            .ToListAsync(cancellationToken);

        return PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(promotions, nowUtc);
    }

    /// <summary>
    /// Maps MongoDB ProductDocument to Elasticsearch ProductSearchDocument.
    /// SearchText is THE POWER FIELD for multilingual search.
    /// </summary>
    private static ProductSearchDocument MapToSearchDocument(ProductDocument product)
    {
        return ProductSearchDocumentMapper.MapFromProductDocument(product);
    }

    private sealed record PromotionSearchAuthority(
        PointOfSale.Core.Entities.Promotion? Promotion,
        bool Exists,
        bool VersionAccepted);

    private static bool IsTransientError(ElasticsearchClientException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("connection") ||
               message.Contains("timeout") ||
               message.Contains("unavailable") ||
               message.Contains("503") ||
               message.Contains("429");
    }

    private sealed class ProjectionNotReadyException : Exception
    {
        public ProjectionNotReadyException(string message) : base(message)
        {
        }
    }

    private sealed class SearchIndexSyncException : Exception
    {
        public SearchIndexSyncException(string message) : base(message)
        {
        }

        public SearchIndexSyncException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

/// <summary>
/// Consumer definition for ProductPromotionSearchIndexConsumer.
/// </summary>
public class ProductPromotionSearchIndexConsumerDefinition : ConsumerDefinition<ProductPromotionSearchIndexConsumer>
{
    public ProductPromotionSearchIndexConsumerDefinition()
    {
        EndpointName = "product-promotion-search-index";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProductPromotionSearchIndexConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Configure retry policy for transient failures
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
