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

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ProductUpdatedEvent that updates products in Elasticsearch.
/// 
/// CRITICAL: This ensures Elasticsearch stays in sync when products are modified.
/// Without this consumer, search results become stale after any product update.
/// 
/// PHASE: Shopify-Grade Promotion-Aware Search
/// This consumer reads MongoDB projection candidates, then reapplies SQL authority before indexing.
/// PostgreSQL remains business truth for searchable product visibility and fields.
/// 
/// Error Handling Strategy:
/// - Missing TenantId: Log error, acknowledge message (no retry)
/// - Elasticsearch transient errors: Let MassTransit handle retries
/// - Elasticsearch permanent errors: throw so MassTransit retry/error transport owns recovery
/// </summary>
public class ProductUpdatedSearchIndexConsumer : IConsumer<ProductUpdatedEvent>
{
    private readonly IUnifiedSearchIndexService _indexService;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly ILogger<ProductUpdatedSearchIndexConsumer> _logger;
    private readonly ApplicationDbContext _dbContext;

    private const string ProductsCollectionName = "products";
    private const string ConsumerName = nameof(ProductUpdatedSearchIndexConsumer);

    public ProductUpdatedSearchIndexConsumer(
        IUnifiedSearchIndexService indexService,
        IMongoDatabase database,
        ILogger<ProductUpdatedSearchIndexConsumer> logger,
        ApplicationDbContext dbContext)
    {
        _indexService = indexService;
        _productCollection = database.GetCollection<ProductDocument>(ProductsCollectionName);
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=SearchConsumer | ConsumerName={ConsumerName} | MessageId={MessageId} | EventId={EventId} | ProductId={ProductId} | TenantId={TenantId} | EventVersion={EventVersion} | ReceiveTime={ReceiveTime:O} | CorrelationId={CorrelationId} | RetryCount={RetryCount}",
            ConsumerName,
            context.MessageId,
            @event.EventId,
            @event.ProductId,
            @event.TenantId,
            @event.EventVersion,
            DateTime.UtcNow,
            correlationId,
            context.GetRetryAttempt());

        _logger.LogInformation(
            "Processing ProductUpdatedEvent for Elasticsearch | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | ChangedFields: {ChangedFields}",
            @event.EventId, @event.ProductId, @event.TenantId, 
            string.Join(",", @event.ChangedFields ?? Array.Empty<string>()));

        // Validate TenantId
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductUpdatedEvent rejected: TenantId is missing | EventId: {EventId} | ProductId: {ProductId}",
                @event.EventId, @event.ProductId);
            return;
        }

        if (@event.ProductId == Guid.Empty)
        {
            _logger.LogError(
                "ProductUpdatedEvent rejected: ProductId is empty | EventId: {EventId}",
                @event.EventId);
            return;
        }

        ProjectionEventFence? fence = null;
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            typeof(ProductUpdatedEvent).FullName ?? nameof(ProductUpdatedEvent),
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
                    "ProductUpdatedEvent duplicate or active replay collapsed before Elasticsearch side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId);
                return;
            }

            // Fetch current product projection from MongoDB for ES hydration.
            // This includes promotion fields that we must preserve
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, @event.ProductId)
            );

            var product = await _productCollection.Find(filter).FirstOrDefaultAsync(context.CancellationToken);

            if (product == null)
            {
                _logger.LogWarning(
                    "PROJECTION_EVENT_TRACE | Stage=SearchConsumer | Outcome=ProjectionMissing | ProjectionFound={ProjectionFound} | ProjectionMissing={ProjectionMissing} | ProjectionNotReadyException={ProjectionNotReadyException} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | RetryCount={RetryCount} | Timestamp={Timestamp:O}",
                    false,
                    true,
                    true,
                    @event.ProductId,
                    @event.TenantId,
                    @event.EventId,
                    context.GetRetryAttempt(),
                    DateTime.UtcNow);
                _logger.LogWarning(
                    "Product not found in MongoDB - throwing ProjectionNotReadyException, will retry ES sync | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.ProductId, @event.TenantId);
                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {@event.ProductId} is not available before ProductUpdatedEvent indexing.");
            }

            _logger.LogInformation(
                "PROJECTION_EVENT_TRACE | Stage=SearchConsumer | Outcome=ProjectionFound | ProjectionFound={ProjectionFound} | ProjectionMissing={ProjectionMissing} | ProjectionNotReadyException={ProjectionNotReadyException} | ProductId={ProductId} | TenantId={TenantId} | EventId={EventId} | RetryCount={RetryCount} | Timestamp={Timestamp:O}",
                true,
                false,
                false,
                @event.ProductId,
                @event.TenantId,
                @event.EventId,
                context.GetRetryAttempt(),
                DateTime.UtcNow);

            product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                product,
                @event.TenantId.Value,
                context.CancellationToken);

            if (product == null)
            {
                var deleted = await _indexService.DeleteProductAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    context.CancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for missing SQL product {@event.ProductId}.");
                }

                await fence.CompleteAsync(@event, fenceKey, context.CancellationToken);
                return;
            }

            if (!product.IsActive || !product.IsOnlineVisible || !product.IsSearchable)
            {
                _logger.LogInformation(
                    "Product search visibility removed from current authority - deleting from Elasticsearch | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.ProductId,
                    @event.TenantId);

                var deleted = await _indexService.DeleteProductAsync(
                    @event.TenantId.Value,
                    @event.ProductId,
                    context.CancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for hidden product {@event.ProductId}.");
                }

                await fence.CompleteAsync(@event, fenceKey, context.CancellationToken);
                return;
            }

            // Build ES document from the SQL-recomposed projection.
            var document = MapToSearchDocument(product, @event.EventId);

            var success = await _indexService.IndexProductAsync(document, context.CancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "ProductUpdatedEvent indexed to Elasticsearch | ProductId: {ProductId} | TenantId: {TenantId} | HasActivePromotion: {HasPromotion}",
                    @event.ProductId, @event.TenantId, document.HasActivePromotion);
                await fence.CompleteAsync(@event, fenceKey, context.CancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "ProductUpdatedEvent indexing returned false | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.ProductId, @event.TenantId);
                throw new SearchIndexSyncException(
                    $"Elasticsearch index failed for product {@event.ProductId}.");
            }
        }
        catch (ElasticsearchClientException ex) when (IsTransientError(ex))
        {
            _logger.LogWarning(ex,
                "Transient Elasticsearch error - will retry | ProductId: {ProductId}",
                @event.ProductId);
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
                "Error processing ProductUpdatedEvent for Elasticsearch | ProductId: {ProductId}",
                @event.ProductId);
            throw;
        }
        finally
        {
            if (fence != null)
            {
                await fence.DisposeAsync();
            }
        }
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
    }

    /// <summary>
    /// Maps the SQL-recomposed ProductDocument to an Elasticsearch ProductSearchDocument.
    /// SearchText is THE POWER FIELD for multilingual search.
    /// </summary>
    private static ProductSearchDocument MapToSearchDocument(ProductDocument product, Guid eventId)
    {
        return ProductSearchDocumentMapper.MapFromProductDocument(product, eventId);
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

