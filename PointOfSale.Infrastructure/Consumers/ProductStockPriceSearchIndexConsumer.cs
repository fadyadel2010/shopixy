using Elasticsearch.Net;
using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ProductStockChangedEvent and ProductPriceChangedEvent
/// that syncs stock and price changes to Elasticsearch.
/// 
/// PHASE: Elasticsearch Search &amp; Discovery System — Event Coverage Gap Fix
/// 
/// PROBLEM:
/// Stock and price changes update MongoDB but NOT Elasticsearch. This means:
/// - "In Stock" filter returns stale results
/// - Price range queries miss updated prices
/// - Sort by price shows outdated values
/// 
/// SOLUTION:
/// Read the full product projection from MongoDB, rebuild the ES document,
/// and re-index. This preserves promotion fields and all other data.
/// 
/// ARCHITECTURE RULES:
/// - MongoDB supplies projection candidates; PostgreSQL authority is reapplied before ES writes
/// - Events are triggers, not data sources
/// - Full re-index per product (not partial update) for consistency
/// - Idempotent by document ID ({TenantId}_{ProductId})
/// 
/// Error Handling:
/// - Missing TenantId: Log error, acknowledge (no retry)
/// - Transient ES errors: Let MassTransit retry
/// - Permanent ES errors: throw so MassTransit retry/error transport owns recovery
/// </summary>
public class ProductStockPriceSearchIndexConsumer :
    IConsumer<ProductStockChangedEvent>,
    IConsumer<ProductVariantStockChangedEvent>,
    IConsumer<ProductPriceChangedEvent>
{
    private readonly IUnifiedSearchIndexService _indexService;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly ILogger<ProductStockPriceSearchIndexConsumer> _logger;
    private readonly ApplicationDbContext _dbContext;

    private const string ProductsCollectionName = "products";
    private const string ProductStockConsumerName = nameof(ProductStockPriceSearchIndexConsumer) + ":" + nameof(ProductStockChangedEvent);
    private const string VariantStockConsumerName = nameof(ProductStockPriceSearchIndexConsumer) + ":" + nameof(ProductVariantStockChangedEvent);
    private const string ProductPriceConsumerName = nameof(ProductStockPriceSearchIndexConsumer) + ":" + nameof(ProductPriceChangedEvent);

    public ProductStockPriceSearchIndexConsumer(
        IUnifiedSearchIndexService indexService,
        IMongoDatabase database,
        ILogger<ProductStockPriceSearchIndexConsumer> logger,
        ApplicationDbContext dbContext)
    {
        _indexService = indexService;
        _productCollection = database.GetCollection<ProductDocument>(ProductsCollectionName);
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Handle ProductVariantStockChangedEvent - variant stock is the authoritative stock stream.
    /// Waits for the Mongo product variant projection to apply the event, then re-indexes the product.
    /// </summary>
    public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation(
            "[ES-VariantStock] Processing variant stock change | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | StockUpdatedAtUtc: {StockUpdatedAtUtc}",
            @event.EventId, @event.ProductId, @event.VariantId, @event.TenantId, @event.StockUpdatedAtUtc);

        if (!ValidateEvent(@event.TenantId, @event.ProductId, @event.EventId, "ProductVariantStockChangedEvent"))
        {
            return;
        }

        if (@event.VariantId == Guid.Empty)
        {
            _logger.LogError(
                "[ES-VariantStock] Event rejected - VariantId empty | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                @event.EventId, @event.ProductId, @event.TenantId);
            return;
        }

        ProjectionEventFence? fence = null;
        var fenceKey = new ProjectionEventFenceKey(
            VariantStockConsumerName,
            typeof(ProductVariantStockChangedEvent).FullName ?? nameof(ProductVariantStockChangedEvent),
            @event.VariantId);
        try
        {
            fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                fenceKey,
                _logger,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[ES-VariantStock] Duplicate or active replay collapsed before search side effects | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.VariantId,
                    @event.TenantId);
                return;
            }

            var applied = await ReindexProductFromMongoAfterVariantStockAsync(
                @event.TenantId!.Value,
                @event.ProductId,
                @event.VariantId,
                @event.EventId,
                @event.StockUpdatedAtUtc,
                @event.EventVersion,
                context.CancellationToken);

            if (applied && fence != null)
            {
                await fence.CompleteAsync(
                    @event,
                    fenceKey,
                    context.CancellationToken);
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

    /// <summary>
    /// Handle ProductStockChangedEvent — sync stock changes to Elasticsearch.
    /// Updates IsInStock and StockQuantity fields in ES.
    /// </summary>
    public async Task Consume(ConsumeContext<ProductStockChangedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation(
            "[ES-Stock] Processing stock change | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | StockQuantity: {StockQuantity}",
            @event.EventId, @event.ProductId, @event.TenantId, @event.StockQuantity);

        if (!ValidateEvent(@event.TenantId, @event.ProductId, @event.EventId, "ProductStockChangedEvent"))
        {
            return;
        }

        ProjectionEventFence? fence = null;
        var fenceKey = new ProjectionEventFenceKey(
            ProductStockConsumerName,
            typeof(ProductStockChangedEvent).FullName ?? nameof(ProductStockChangedEvent),
            @event.ProductId);
        try
        {
            fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                fenceKey,
                _logger,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[ES-Stock] Duplicate or active replay collapsed before search side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId);
                return;
            }

            await ReindexProductFromMongoAsync(
                @event.TenantId!.Value,
                @event.ProductId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.Version,
                "ProductStockChangedEvent",
                context.CancellationToken);

            await fence.CompleteAsync(
                @event,
                fenceKey,
                context.CancellationToken);
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Handle ProductPriceChangedEvent — sync price changes to Elasticsearch.
    /// Updates Price field in ES (critical for price range filters and sorting).
    /// </summary>
    public async Task Consume(ConsumeContext<ProductPriceChangedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation(
            "[ES-Price] Processing price change | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | OldPrice: {OldPrice} | NewPrice: {NewPrice}",
            @event.EventId, @event.ProductId, @event.TenantId, @event.OldPrice, @event.NewPrice);

        if (!ValidateEvent(@event.TenantId, @event.ProductId, @event.EventId, "ProductPriceChangedEvent"))
        {
            return;
        }

        ProjectionEventFence? fence = null;
        var fenceKey = new ProjectionEventFenceKey(
            ProductPriceConsumerName,
            typeof(ProductPriceChangedEvent).FullName ?? nameof(ProductPriceChangedEvent),
            @event.ProductId);
        try
        {
            fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                fenceKey,
                _logger,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "[ES-Price] Duplicate or active replay collapsed before search side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId);
                return;
            }

            await ReindexProductFromMongoAsync(
                @event.TenantId!.Value,
                @event.ProductId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.Version,
                "ProductPriceChangedEvent",
                context.CancellationToken);

            if (fence != null)
            {
                await fence.CompleteAsync(
                    @event,
                    fenceKey,
                    context.CancellationToken);
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

    /// <summary>
    /// Reads the current product state from MongoDB and re-indexes to Elasticsearch.
    /// This ensures all fields (including promotion data) are preserved.
    /// </summary>
    private async Task ReindexProductFromMongoAsync(
        Guid tenantId,
        Guid productId,
        Guid eventId,
        DateTime eventOccurredAtUtc,
        int eventVersion,
        string eventName,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId));

            var product = await _productCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);

            if (product == null)
            {
                _logger.LogWarning(
                    "[ES-{EventName}] Product not found in MongoDB — throwing ProjectionNotReadyException, will retry ES sync | ProductId: {ProductId} | TenantId: {TenantId}",
                    eventName, productId, tenantId);
                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {productId} is not available before {eventName} indexing.");
            }

            if (IsMongoProjectionOlderThanEvent(product, eventOccurredAtUtc, eventVersion))
            {
                _logger.LogWarning(
                    "[ES-{EventName}] MongoDB product projection has not caught up — throwing ProjectionNotReadyException, will retry ES sync | ProductId: {ProductId} | TenantId: {TenantId} | EventUtc: {EventUtc} | EventVersion: {EventVersion} | MongoUtc: {MongoUtc} | MongoVersion: {MongoVersion}",
                    eventName,
                    productId,
                    tenantId,
                    eventOccurredAtUtc,
                    eventVersion,
                    product.LastEventOccurredAtUtc,
                    product.LastEventVersion);

                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {productId} is older than {eventName}.");
            }

            product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                product,
                tenantId,
                cancellationToken);

            if (product == null)
            {
                _logger.LogWarning(
                    "[ES-{EventName}] SQL product authority not found - deleting stale search document | ProductId: {ProductId} | TenantId: {TenantId}",
                    eventName,
                    productId,
                    tenantId);

                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    tenantId,
                    productId,
                    eventId,
                    cancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for missing SQL product {productId}.");
                }

                return;
            }

            // Delete hidden products only after SQL authority has had a chance to
            // override stale Mongo visibility from replayed projection timing.
            if (!product.IsActive || !product.IsOnlineVisible || !product.IsSearchable)
            {
                _logger.LogInformation(
                    "[ES-{EventName}] SQL product authority is not searchable - deleting stale ES document | ProductId: {ProductId} | TenantId: {TenantId}",
                    eventName, productId, tenantId);

                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    tenantId,
                    productId,
                    eventId,
                    cancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for hidden product {productId}.");
                }

                return;
            }

            var document = ProductSearchDocumentMapper.MapFromProductDocument(product, eventId);
            var success = await _indexService.IndexProductAsync(document, cancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "[ES-{EventName}] Product re-indexed | ProductId: {ProductId} | TenantId: {TenantId} | IsInStock: {IsInStock} | Price: {Price}",
                    eventName, productId, tenantId, document.IsInStock, document.Price);
            }
            else
            {
                _logger.LogWarning(
                    "[ES-{EventName}] Product re-index failed | ProductId: {ProductId} | TenantId: {TenantId}",
                    eventName, productId, tenantId);
                throw new SearchIndexSyncException(
                    $"Elasticsearch index failed for product {productId}.");
            }
        }
        catch (ElasticsearchClientException ex) when (IsTransientError(ex))
        {
            _logger.LogWarning(ex,
                "[ES-{EventName}] Transient error — will retry | ProductId: {ProductId}",
                eventName, productId);
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
                "[ES-{EventName}] Error re-indexing product | ProductId: {ProductId} | TenantId: {TenantId}",
                eventName, productId, tenantId);
            throw new SearchIndexSyncException(
                $"Elasticsearch sync failed for product {productId} after {eventName}.", ex);
        }
    }

    private async Task<bool> ReindexProductFromMongoAfterVariantStockAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        Guid eventId,
        DateTime stockUpdatedAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(stockUpdatedAtUtc);

            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId));

            var product = await _productCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);

            if (product == null)
            {
                _logger.LogWarning(
                    "[ES-VariantStock] Product not found in MongoDB - will retry ES sync | ProductId: {ProductId} | TenantId: {TenantId}",
                    productId, tenantId);
                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {productId} is not available before variant stock indexing.");
            }

            if (product.Variants == null || !product.Variants.Any())
            {
                _logger.LogWarning(
                    "[ES-VariantStock] Product has no variants in MongoDB projection - will retry ES sync | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                    productId, variantId, tenantId);
                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {productId} has no variants before variant {variantId} stock indexing.");
            }

            var changedVariant = product.Variants.FirstOrDefault(variant => variant.VariantId == variantId);
            var changedVariantStockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(
                changedVariant?.LastStockUpdatedAtUtc ?? changedVariant?.StockUpdatedAtUtc);
            var changedVariantStockEventVersion = changedVariant?.LastStockEventVersion > 0
                ? changedVariant.LastStockEventVersion
                : 0;

            if (changedVariantStockUpdatedAtUtc == null ||
                changedVariantStockUpdatedAtUtc.Value < stockUpdatedAtUtc ||
                (changedVariantStockUpdatedAtUtc.Value == stockUpdatedAtUtc &&
                 changedVariantStockEventVersion < eventVersion))
            {
                _logger.LogWarning(
                    "[ES-VariantStock] MongoDB variant stock projection has not caught up - will retry ES sync | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | EventVersion: {EventVersion} | StockUpdatedAtUtc: {StockUpdatedAtUtc} | MongoStockUpdatedAtUtc: {MongoStockUpdatedAtUtc} | MongoStockEventVersion: {MongoStockEventVersion}",
                    productId,
                    variantId,
                    tenantId,
                    eventVersion,
                    stockUpdatedAtUtc,
                    changedVariantStockUpdatedAtUtc,
                    changedVariantStockEventVersion);

                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {productId} variant {variantId} is older than ProductVariantStockChangedEvent.");
            }

            product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                product,
                tenantId,
                cancellationToken);

            if (product == null)
            {
                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    tenantId,
                    productId,
                    eventId,
                    cancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for missing SQL product {productId}.");
                }

                return true;
            }

            if (!product.IsActive || !product.IsOnlineVisible || !product.IsSearchable)
            {
                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    tenantId,
                    productId,
                    eventId,
                    cancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for hidden product {productId}.");
                }

                return true;
            }

            var document = ProductSearchDocumentMapper.MapFromProductDocument(product, eventId);
            var success = await _indexService.IndexProductAsync(document, cancellationToken);

            if (!success)
            {
                throw new SearchIndexSyncException(
                    $"Elasticsearch index failed for product {productId}.");
            }

            return true;
        }
        catch (ElasticsearchClientException ex) when (IsTransientError(ex))
        {
            _logger.LogWarning(ex,
                "[ES-VariantStock] Transient error - will retry | ProductId: {ProductId}",
                productId);
            throw;
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
                "[ES-VariantStock] Error re-indexing product | ProductId: {ProductId} | TenantId: {TenantId}",
                productId, tenantId);
            throw new SearchIndexSyncException(
                $"Elasticsearch sync failed for product {productId} after variant stock event.", ex);
        }
    }

    private static bool IsMongoProjectionOlderThanEvent(
        ProductDocument product,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        if (product.LastEventVersion <= 0 ||
            !product.LastEventOccurredAtUtc.HasValue)
        {
            return true;
        }

        return product.LastEventVersion < eventVersion ||
               (product.LastEventVersion == eventVersion &&
                product.LastEventOccurredAtUtc.Value < eventOccurredAtUtc);
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

    private bool ValidateEvent(Guid? tenantId, Guid productId, Guid eventId, string eventName)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[ES-{EventName}] Event rejected — TenantId missing | EventId: {EventId} | ProductId: {ProductId}",
                eventName, eventId, productId);
            return false;
        }

        if (productId == Guid.Empty)
        {
            _logger.LogError(
                "[ES-{EventName}] Event rejected — ProductId empty | EventId: {EventId} | TenantId: {TenantId}",
                eventName, eventId, tenantId);
            return false;
        }

        return true;
    }

    private static bool IsTransientError(ElasticsearchClientException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("connection") ||
               message.Contains("timeout") ||
               message.Contains("unavailable") ||
               message.Contains("503") ||
               message.Contains("429");
    }
}

/// <summary>
/// Consumer definition for ProductStockPriceSearchIndexConsumer.
/// Configures endpoint and retry policy.
/// </summary>
public class ProductStockPriceSearchIndexConsumerDefinition : ConsumerDefinition<ProductStockPriceSearchIndexConsumer>
{
    public ProductStockPriceSearchIndexConsumerDefinition()
    {
        EndpointName = "product-stock-price-search-index";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProductStockPriceSearchIndexConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
