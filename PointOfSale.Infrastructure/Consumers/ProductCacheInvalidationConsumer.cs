using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer that invalidates Redis cache for product stock/price events.
/// 
/// PHASE: Redis Hardening — Full Invalidation Coverage
/// 
/// Responsibilities:
/// - Listen for ProductStockChangedEvent, ProductPriceChangedEvent
/// - Invalidate tenant's product list cache
/// - Invalidate specific product detail cache entries (id + slug)
/// - Invalidate homepage caches (featured products, new arrivals, best sellers)
/// - Delegate all Storefront product Redis invalidation to its single owner
/// 
/// IMPORTANT:
/// - Required invalidation failures are rethrown so the event can retry
/// - Create/update/delete/publication invalidation is owned by the SQL-authority
///   projection writer after its Mongo mutation boundary returns
/// </summary>
public class ProductCacheInvalidationConsumer :
    IConsumer<ProductStockChangedEvent>,
    IConsumer<ProductPriceChangedEvent>
{
    private readonly IStorefrontProductCacheInvalidator _cacheInvalidator;
    private readonly ILogger<ProductCacheInvalidationConsumer> _logger;
    private readonly ApplicationDbContext _dbContext;

    private const string ProductPriceConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductPriceChangedEvent);
    private const string ProductStockConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductStockChangedEvent);

    public ProductCacheInvalidationConsumer(
        IStorefrontProductCacheInvalidator cacheInvalidator,
        ILogger<ProductCacheInvalidationConsumer> logger,
        ApplicationDbContext dbContext)
    {
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProductStockChangedEvent> context)
    {
        var message = context.Message;
        await InvalidateWithFenceAsync(
            @event: message,
            eventName: nameof(ProductStockChangedEvent),
            eventType: typeof(ProductStockChangedEvent).FullName ?? nameof(ProductStockChangedEvent),
            consumerName: ProductStockConsumerName,
            tenantId: message.TenantId,
            productId: message.ProductId,
            slug: null,
            correlationId: message.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A",
            invalidateHomepage: true,
            cancellationToken: context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductPriceChangedEvent> context)
    {
        var message = context.Message;
        await InvalidateWithFenceAsync(
            @event: message,
            eventName: nameof(ProductPriceChangedEvent),
            eventType: typeof(ProductPriceChangedEvent).FullName ?? nameof(ProductPriceChangedEvent),
            consumerName: ProductPriceConsumerName,
            tenantId: message.TenantId,
            productId: message.ProductId,
            slug: null,
            correlationId: message.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A",
            invalidateHomepage: true,
            cancellationToken: context.CancellationToken);
    }

    private async Task InvalidateWithFenceAsync(
        IntegrationEvent @event,
        string eventName,
        string eventType,
        string consumerName,
        Guid? tenantId,
        Guid productId,
        string? slug,
        string correlationId,
        bool invalidateHomepage,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            await InvalidateAsync(
                eventName,
                @event.EventId,
                tenantId,
                productId,
                slug,
                correlationId,
                invalidateHomepage,
                cancellationToken);
            return;
        }

        await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            consumerName,
            eventType,
            productId,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Duplicate or active product cache invalidation collapsed before Redis side effects | Event: {Event} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                eventName,
                @event.EventId,
                productId,
                tenantId.Value);
            return;
        }

        var authoritativeSlug = await ResolveCurrentSqlProductSlugAsync(
            tenantId.Value,
            productId,
            cancellationToken);

        await InvalidateAsync(
            eventName,
            @event.EventId,
            tenantId,
            productId,
            authoritativeSlug ?? slug,
            correlationId,
            invalidateHomepage,
            cancellationToken);

        await fence.CompleteAsync(
            @event,
            consumerName,
            eventType,
            productId,
            cancellationToken);
    }

    private async Task<string?> ResolveCurrentSqlProductSlugAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && product.Id == productId)
            .Select(product => product.Slug)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task InvalidateAsync(
        string eventName,
        Guid eventId,
        Guid? tenantId,
        Guid productId,
        string? slug,
        string correlationId,
        bool invalidateHomepage,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            _logger.LogDebug(
                "Cache invalidation skipped: No TenantId | Event: {Event} | EventId: {EventId} | ProductId: {ProductId}",
                eventName,
                eventId,
                productId);
            return;
        }

        var resolvedTenantId = tenantId.Value;
        _logger.LogInformation(
            "Processing cache invalidation | Event: {Event} | EventId: {EventId} | ProductId: {ProductId} | Slug: {Slug} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            eventName,
            eventId,
            productId,
            slug,
            resolvedTenantId,
            correlationId);

        await _cacheInvalidator.InvalidateProductAsync(
            resolvedTenantId,
            productId,
            slug,
            invalidateHomepage,
            cancellationToken);

        _logger.LogInformation(
            "Cache invalidation completed | Event: {Event} | EventId: {EventId} | ProductId: {ProductId} | Slug: {Slug} | TenantId: {TenantId} | HomepageInvalidated: {HomepageInvalidated}",
            eventName,
            eventId,
            productId,
            slug,
            resolvedTenantId,
            invalidateHomepage);
    }
}
