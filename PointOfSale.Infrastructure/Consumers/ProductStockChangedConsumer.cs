using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumer for ProductStockChangedEvent.
/// Updates the StockQuantity field in MongoDB ProductDocument.
/// 
/// This enables the storefront to:
/// - Display "In Stock" / "Out of Stock" status
/// - Disable "Add to Cart" for out-of-stock products
/// - Show stock quantity when available
/// 
/// Architecture Notes:
/// - MongoDB is NOT the source of truth (PostgreSQL ProductVariantStocks is)
/// - This is a read-only projection for storefront display
/// - Writes are idempotent by (TenantId, ProductId)
/// </summary>
public class ProductStockChangedConsumer :
    IConsumer<ProductStockChangedEvent>,
    IConsumer<ProductVariantStockChangedEvent>
{
    private readonly IMongoCollection<ProductDocument> _collection;
    private readonly ILogger<ProductStockChangedConsumer> _logger;
    private readonly ApplicationDbContext? _dbContext;

    public ProductStockChangedConsumer(
        IMongoDatabase database,
        ILogger<ProductStockChangedConsumer> logger,
        ApplicationDbContext? dbContext = null)
    {
        _collection = database.GetCollection<ProductDocument>("products");
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProductStockChangedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "[ProductStock] Processing stock change | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | StockQuantity: {StockQuantity} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ProductId,
            @event.TenantId,
            @event.StockQuantity,
            correlationId);

        // Validate TenantId
        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[ProductStock] Event rejected - missing TenantId | EventId: {EventId} | ProductId: {ProductId}",
                @event.EventId, @event.ProductId);
            return;
        }

        // Validate ProductId
        if (@event.ProductId == Guid.Empty)
        {
            _logger.LogError(
                "[ProductStock] Event rejected - empty ProductId | EventId: {EventId} | TenantId: {TenantId}",
                @event.EventId, @event.TenantId);
            return;
        }

        try
        {
            var filter = BuildFreshStockEventFilter(
                @event.TenantId.Value,
                @event.ProductId,
                @event.OccurredAtUtc,
                @event.EventVersion);

            var update = Builders<ProductDocument>.Update
                .Set(x => x.StockQuantity, @event.StockQuantity)
                .Set(x => x.TrackStock, @event.TrackStock)
                .Set(x => x.AllowBackorder, @event.AllowBackorder)
                .Set(x => x.SyncedAtUtc, DateTime.UtcNow)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion)
                .Set(x => x.LastStockEventId, @event.EventId)
                .Set(x => x.LastStockUpdatedAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastStockEventVersion, @event.EventVersion);

            var result = await _collection.UpdateOneAsync(
                filter, 
                update, 
                cancellationToken: context.CancellationToken);

            if (result.MatchedCount > 0)
            {
                _logger.LogInformation(
                    "[ProductStock] Stock updated | ProductId: {ProductId} | StockQuantity: {StockQuantity} | TrackStock: {TrackStock} | AllowBackorder: {AllowBackorder}",
                    @event.ProductId, @event.StockQuantity, @event.TrackStock, @event.AllowBackorder);
            }
            else
            {
                var existsFilter = Builders<ProductDocument>.Filter.And(
                    Builders<ProductDocument>.Filter.Eq(x => x.TenantId, @event.TenantId.Value),
                    Builders<ProductDocument>.Filter.Eq(x => x.ProductId, @event.ProductId));
                    
                var exists = await _collection.Find(existsFilter).AnyAsync(context.CancellationToken);
                if (exists)
                {
                    _logger.LogInformation(
                        "[ProductStock] Event rejected - stale event | EventId: {EventId} | ProductId: {ProductId} | EventUtc: {EventUtc}",
                        @event.EventId, @event.ProductId, @event.OccurredAtUtc);
                }
                else
                {
                    _logger.LogWarning(
                        "[ProductStock] Product not found in MongoDB | ProductId: {ProductId} | TenantId: {TenantId}",
                        @event.ProductId, @event.TenantId);
                }
            }
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "[ProductStock] MongoDB error | EventId: {EventId} | ProductId: {ProductId}",
                @event.EventId, @event.ProductId);
            throw; // Let MassTransit retry
        }
    }

    public async Task Consume(ConsumeContext<ProductVariantStockChangedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "[ProductStock:VariantAuthority] Processing variant stock change | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | TotalStock: {TotalStock} | StockUpdatedAtUtc: {StockUpdatedAtUtc} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ProductId,
            @event.VariantId,
            @event.TenantId,
            @event.TotalStockQuantity,
            @event.StockUpdatedAtUtc,
            correlationId);

        if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[ProductStock:VariantAuthority] Event rejected - missing TenantId | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId}",
                @event.EventId, @event.ProductId, @event.VariantId);
            return;
        }

        if (@event.ProductId == Guid.Empty || @event.VariantId == Guid.Empty)
        {
            _logger.LogError(
                "[ProductStock:VariantAuthority] Event rejected - empty ProductId or VariantId | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId}",
                @event.EventId, @event.ProductId, @event.VariantId);
            return;
        }

        ProjectionEventFence? fence = null;
        var fenceKey = ProjectionEventFenceKey.For<ProductStockChangedConsumer, ProductVariantStockChangedEvent>(
            @event.VariantId);
        try
        {
            if (_dbContext != null)
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
                        "[ProductStock:VariantAuthority] Duplicate or active replay collapsed before product stock side effects | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                        @event.EventId,
                        @event.ProductId,
                        @event.VariantId,
                        @event.TenantId);
                    return;
                }
            }

            await SynchronizeProductLevelStockFromVariantProjectionAsync(
                @event.TenantId.Value,
                @event.ProductId,
                @event.VariantId,
                @event.EventId,
                @event.StockUpdatedAtUtc,
                @event.EventVersion,
                context.CancellationToken);

            if (fence != null)
            {
                await fence.CompleteAsync(
                    @event,
                    fenceKey,
                    context.CancellationToken);
            }
        }
        catch (ProjectionNotReadyException)
        {
            throw;
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "[ProductStock:VariantAuthority] MongoDB error | EventId: {EventId} | ProductId: {ProductId} | VariantId: {VariantId}",
                @event.EventId, @event.ProductId, @event.VariantId);
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

    private async Task SynchronizeProductLevelStockFromVariantProjectionAsync(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        Guid eventId,
        DateTime stockUpdatedAtUtc,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        stockUpdatedAtUtc = StockProjectionTimestamp.NormalizeUtc(stockUpdatedAtUtc);

        var productFilter = Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Eq(x => x.ProductId, productId));

        var product = await _collection.Find(productFilter).FirstOrDefaultAsync(cancellationToken);
        if (product == null)
        {
            throw new ProjectionNotReadyException(
                $"Product projection {productId} is not ready for variant stock event {eventId}.");
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
            throw new ProjectionNotReadyException(
                $"Product projection {productId} variant {variantId} has not applied stock event {eventId}.");
        }

        var aggregateStock = Math.Max(
            0,
            (int)product.Variants
                .Where(variant => variant.IsActive)
                .Sum(variant => variant.TotalStock));

        var filter = BuildFreshStockEventFilter(tenantId, productId, stockUpdatedAtUtc, eventVersion);
        var update = Builders<ProductDocument>.Update
            .Set(x => x.StockQuantity, aggregateStock)
            .Set(x => x.SyncedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastStockEventId, eventId)
            .Set(x => x.LastStockUpdatedAtUtc, stockUpdatedAtUtc)
            .Set(x => x.LastStockEventVersion, eventVersion);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0)
        {
            _logger.LogInformation(
                "[ProductStock:VariantAuthority] Product-level stock update skipped as stale or duplicate | ProductId: {ProductId} | TenantId: {TenantId} | EventId: {EventId} | StockUpdatedAtUtc: {StockUpdatedAtUtc} | EventVersion: {EventVersion}",
                productId, tenantId, eventId, stockUpdatedAtUtc, eventVersion);
        }
    }

    private static FilterDefinition<ProductDocument> BuildFreshStockEventFilter(
        Guid tenantId,
        Guid productId,
        DateTime stockUpdatedAtUtc,
        int eventVersion)
    {
        var filter = Builders<ProductDocument>.Filter;
        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.ProductId, productId),
            filter.Or(
                filter.Exists(x => x.LastStockUpdatedAtUtc, false),
                filter.Eq(x => x.LastStockUpdatedAtUtc, null),
                filter.Lt(x => x.LastStockUpdatedAtUtc, stockUpdatedAtUtc),
                filter.And(
                    filter.Eq(x => x.LastStockUpdatedAtUtc, stockUpdatedAtUtc),
                    filter.Lte(x => x.LastStockEventVersion, eventVersion))));
    }

    private sealed class ProjectionNotReadyException : Exception
    {
        public ProjectionNotReadyException(string message) : base(message)
        {
        }
    }
}

/// <summary>
/// Consumer definition for ProductStockChangedConsumer.
/// Configures endpoint name and retry policy.
/// </summary>
public class ProductStockChangedConsumerDefinition : ConsumerDefinition<ProductStockChangedConsumer>
{
    public ProductStockChangedConsumerDefinition()
    {
        EndpointName = "product-stock-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProductStockChangedConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
