using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Nest;
using Elasticsearch.Net;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ProductCreatedEvent that indexes products to Elasticsearch.
/// 
/// Responsibilities:
/// - Validate incoming event (TenantId is mandatory)
/// - Transform event to ProductSearchDocument
/// - Perform idempotent index operation
/// - Log all actions with structured data
/// 
/// Error Handling Strategy:
/// - Missing TenantId: Log error, acknowledge message (no retry)
/// - Elasticsearch transient errors: Let MassTransit handle retries
/// - Elasticsearch permanent errors: throw so MassTransit retry/error transport owns recovery
/// 
/// IMPORTANT:
/// - This consumer does NOT contain business logic
/// - Elasticsearch is NOT the source of truth
/// - All documents are tenant-isolated by TenantId
/// - Document ID = {TenantId}_{ProductId} ensures idempotent indexing
/// </summary>
public class ProductSearchIndexConsumer : IConsumer<ProductCreatedEvent>
{
    private readonly IUnifiedSearchIndexService _indexService;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly ILogger<ProductSearchIndexConsumer> _logger;
    private readonly ApplicationDbContext _dbContext;
    private const string ProductsCollectionName = "products";
    private const string ConsumerName = nameof(ProductSearchIndexConsumer);

    public ProductSearchIndexConsumer(
        IUnifiedSearchIndexService indexService,
        IMongoDatabase database,
        ILogger<ProductSearchIndexConsumer> logger,
        ApplicationDbContext dbContext)
    {
        _indexService = indexService;
        _productCollection = database.GetCollection<ProductDocument>(ProductsCollectionName);
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
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

        // Start processing log
        _logger.LogInformation(
            "Processing ProductCreatedEvent for Elasticsearch | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ProductId,
            @event.TenantId,
            correlationId);

        // ===========================================
        // CRITICAL: TenantId Validation
        // Events without TenantId MUST NOT be indexed
        // This prevents data corruption in multi-tenant environment
        // ===========================================
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductCreatedEvent rejected for Elasticsearch: TenantId is missing | EventId: {EventId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}.",
                @event.EventId,
                @event.ProductId,
                correlationId);

            // Acknowledge without processing - do NOT throw
            // This prevents infinite retries for permanently invalid messages
            return;
        }

        // Validate ProductId
        if (@event.ProductId == Guid.Empty)
        {
            _logger.LogError(
                "ProductCreatedEvent rejected for Elasticsearch: ProductId is empty | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.TenantId,
                correlationId);
            return;
        }

        ProjectionEventFence? fence = null;
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            typeof(ProductCreatedEvent).FullName ?? nameof(ProductCreatedEvent),
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
                    "ProductCreatedEvent duplicate or active replay collapsed before Elasticsearch side effects | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId);
                return;
            }

            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(x => x.TenantId, @event.TenantId.Value),
                Builders<ProductDocument>.Filter.Eq(x => x.ProductId, @event.ProductId));

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
                    "ProductCreatedEvent MongoDB projection not ready - retrying ES sync | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId);
                throw new ProjectionNotReadyException(
                    $"MongoDB product projection for {@event.ProductId} is not available before ProductCreatedEvent indexing.");
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
                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.EventId,
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
                    "ProductCreatedEvent product not searchable from current SQL authority - deleting any stale ES document | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId);

                var deleted = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    @event.TenantId.Value,
                    @event.ProductId,
                    @event.EventId,
                    context.CancellationToken);
                if (!deleted)
                {
                    throw new SearchIndexSyncException(
                        $"Elasticsearch delete failed for hidden product {@event.ProductId}.");
                }

                await fence.CompleteAsync(@event, fenceKey, context.CancellationToken);
                return;
            }

            var document = ProductSearchDocumentMapper.MapFromProductDocument(product, @event.EventId);

            _logger.LogDebug(
                "Indexing product to Elasticsearch | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | SKU: {SKU}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                @event.SKU ?? "N/A");

            // Perform idempotent index
            var success = await _indexService.IndexProductAsync(document, context.CancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "ProductCreatedEvent successfully indexed to Elasticsearch | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | SKU: {SKU} | CorrelationId: {CorrelationId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    @event.SKU ?? "N/A",
                    correlationId);
                await fence.CompleteAsync(@event, fenceKey, context.CancellationToken);
            }
            else
            {
                // Index service handles logging the specific error
                // We log at warning level since system continues to function
                _logger.LogWarning(
                    "ProductCreatedEvent indexing to Elasticsearch returned false | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}. " +
                    "Product may not be searchable until next sync.",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    correlationId);
                throw new SearchIndexSyncException(
                    $"Elasticsearch index failed for product {@event.ProductId}.");
            }
        }
        catch (ElasticsearchClientException ex) when (IsTransientError(ex))
        {
            // Transient Elasticsearch errors - let MassTransit retry
            _logger.LogWarning(ex,
                "Transient Elasticsearch error processing ProductCreatedEvent - will retry | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);

            throw; // Re-throw to trigger MassTransit retry
        }
        catch (ProjectionNotReadyException)
        {
            throw;
        }
        catch (SearchIndexSyncException)
        {
            throw;
        }
        catch (ElasticsearchClientException ex)
        {
            _logger.LogError(ex,
                "Elasticsearch error processing ProductCreatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}. " +
                "Message will fail so MassTransit retry/error handling can converge the projection.",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error processing ProductCreatedEvent for Elasticsearch | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);
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
    /// Determines if an Elasticsearch error is transient and should trigger a retry.
    /// </summary>
    private static bool IsTransientError(ElasticsearchClientException ex)
    {
        // Network errors, connection issues, timeout errors
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("connection") ||
               message.Contains("timeout") ||
               message.Contains("unavailable") ||
               message.Contains("service_unavailable") ||
               message.Contains("503") ||
               message.Contains("429"); // Too many requests
    }
}
