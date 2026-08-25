using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ProductDeletedEvent that removes products from Elasticsearch.
/// 
/// CRITICAL: This ensures deleted products don't appear in search results.
/// Without this consumer, orphaned documents remain in Elasticsearch.
/// 
/// Error Handling Strategy:
/// - Missing TenantId: Log error, acknowledge message (no retry)
/// - Elasticsearch errors: Log and continue (deletion is best-effort)
/// </summary>
public class ProductDeletedSearchIndexConsumer :
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductArchivedEvent>,
    IConsumer<ProductPublishStatusChangedEvent>
{
    private readonly IUnifiedSearchIndexService _indexService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProductDeletedSearchIndexConsumer> _logger;

    private const string ConsumerName = nameof(ProductDeletedSearchIndexConsumer);

    public ProductDeletedSearchIndexConsumer(
        IUnifiedSearchIndexService indexService,
        ApplicationDbContext dbContext,
        ILogger<ProductDeletedSearchIndexConsumer> logger)
    {
        _indexService = indexService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ProductDeletedEvent> context) =>
        ApplySqlSearchVisibilityAsync(
            context.Message,
            nameof(ProductDeletedEvent),
            context.Message.ProductId,
            context.CorrelationId?.ToString(),
            context.MessageId,
            context.GetRetryAttempt(),
            context.CancellationToken);

    public Task Consume(ConsumeContext<ProductArchivedEvent> context) =>
        ApplySqlSearchVisibilityAsync(
            context.Message,
            nameof(ProductArchivedEvent),
            context.Message.ProductId,
            context.CorrelationId?.ToString(),
            context.MessageId,
            context.GetRetryAttempt(),
            context.CancellationToken);

    public Task Consume(ConsumeContext<ProductPublishStatusChangedEvent> context) =>
        ApplySqlSearchVisibilityAsync(
            context.Message,
            nameof(ProductPublishStatusChangedEvent),
            context.Message.ProductId,
            context.CorrelationId?.ToString(),
            context.MessageId,
            context.GetRetryAttempt(),
            context.CancellationToken);

    private async Task ApplySqlSearchVisibilityAsync(
        IntegrationEvent @event,
        string eventType,
        Guid productId,
        string? contextCorrelationId,
        Guid? messageId,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var correlationId = @event.CorrelationId ?? contextCorrelationId ?? "N/A";

        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=SearchConsumer | ConsumerName={ConsumerName} | MessageId={MessageId} | EventType={EventType} | EventId={EventId} | ProductId={ProductId} | TenantId={TenantId} | EventVersion={EventVersion} | ReceiveTime={ReceiveTime:O} | CorrelationId={CorrelationId} | RetryCount={RetryCount}",
            ConsumerName,
            messageId,
            eventType,
            @event.EventId,
            productId,
            @event.TenantId,
            @event.EventVersion,
            DateTime.UtcNow,
            correlationId,
            retryCount);

        _logger.LogInformation(
            "Processing product lifecycle event for Elasticsearch | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            eventType, @event.EventId, productId, @event.TenantId, correlationId);

        // Validate TenantId
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "{EventType} rejected: TenantId is missing | EventId: {EventId} | ProductId: {ProductId}",
                eventType, @event.EventId, productId);
            return;
        }

        if (productId == Guid.Empty)
        {
            _logger.LogError(
                "{EventType} rejected: ProductId is empty | EventId: {EventId}",
                eventType, @event.EventId);
            return;
        }

        var tenantId = @event.TenantId.Value;
        var fenceKey = new ProjectionEventFenceKey(
            ConsumerName,
            @event.GetType().FullName ?? eventType,
            productId);
        ProjectionEventFence? fence = null;

        try
        {
            fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                fenceKey,
                _logger,
                cancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "Duplicate or active product lifecycle search replay collapsed | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                    eventType,
                    @event.EventId,
                    productId,
                    tenantId);
                return;
            }

            var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = productId,
                    CreatedAtUtc = @event.OccurredAtUtc
                },
                tenantId,
                cancellationToken);

            bool success;
            if (sqlProjection is { IsActive: true, IsOnlineVisible: true, IsSearchable: true })
            {
                var document = ProductSearchDocumentMapper.MapFromProductDocument(
                    sqlProjection,
                    @event.EventId);
                success = await _indexService.IndexProductAsync(document, cancellationToken);

                _logger.LogInformation(
                    "Product lifecycle search event recomposed visible product from SQL authority | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | Success: {Success}",
                    eventType,
                    @event.EventId,
                    productId,
                    tenantId,
                    success);
            }
            else
            {
                success = await ProductSearchTombstoneWriter.WriteAsync(
                    _indexService,
                    _dbContext,
                    tenantId,
                    productId,
                    @event.EventId,
                    cancellationToken);

                _logger.LogInformation(
                    "Product lifecycle search event deleted/tombstoned Elasticsearch document from SQL authority | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | Success: {Success}",
                    eventType,
                    @event.EventId,
                    productId,
                    tenantId,
                    success);
            }

            if (!success)
            {
                throw new SearchIndexSyncException(
                    $"Elasticsearch lifecycle search sync failed for product {productId}.");
            }

            await fence.CompleteAsync(@event, fenceKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying product lifecycle search visibility | EventType: {EventType} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                eventType,
                @event.EventId,
                productId,
                @event.TenantId);
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

    private sealed class SearchIndexSyncException : Exception
    {
        public SearchIndexSyncException(string message) : base(message)
        {
        }
    }
}
