using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer that invalidates Redis cache on ALL product-related events.
/// 
/// PHASE: Redis Hardening — Full Invalidation Coverage
/// 
/// Responsibilities:
/// - Listen for ProductCreatedEvent, ProductUpdatedEvent, ProductDeletedEvent
/// - Listen for ProductStockChangedEvent, ProductPriceChangedEvent
/// - Listen for ProductPublishStatusChangedEvent
/// - Invalidate tenant's product list cache
/// - Invalidate specific product detail cache entries (id + slug)
/// - Invalidate homepage caches (featured products, new arrivals, best sellers)
/// - Invalidate promotion caches (product-specific promotions)
/// 
/// IMPORTANT:
/// - This consumer does NOT fail on cache errors
/// - Cache invalidation is best-effort
/// - MongoDB sync handles read-model projection updates
/// 
/// Error Handling:
/// - All cache failures are logged but swallowed
/// - Never throw here - cache is an optimization
/// </summary>
public class ProductCacheInvalidationConsumer :
    IConsumer<ProductCreatedEvent>,
    IConsumer<ProductUpdatedEvent>,
    IConsumer<ProductDeletedEvent>,
    IConsumer<ProductStockChangedEvent>,
    IConsumer<ProductPriceChangedEvent>,
    IConsumer<ProductPublishStatusChangedEvent>
{
    private readonly IProductCacheService _cacheService;
    private readonly IRedisJsonCacheService _jsonCacheService;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<ProductCacheInvalidationConsumer> _logger;
    private readonly ApplicationDbContext _dbContext;

    private const string ProductCreatedConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductCreatedEvent);
    private const string ProductUpdatedConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductUpdatedEvent);
    private const string ProductDeletedConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductDeletedEvent);
    private const string ProductPriceConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductPriceChangedEvent);
    private const string ProductStockConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductStockChangedEvent);
    private const string ProductPublicationConsumerName = nameof(ProductCacheInvalidationConsumer) + ":" + nameof(ProductPublishStatusChangedEvent);

    public ProductCacheInvalidationConsumer(
        IProductCacheService cacheService,
        IRedisJsonCacheService jsonCacheService,
        IOptions<RedisSettings> redisSettings,
        ILogger<ProductCacheInvalidationConsumer> logger,
        ApplicationDbContext dbContext)
    {
        _cacheService = cacheService;
        _jsonCacheService = jsonCacheService;
        _redisSettings = redisSettings.Value;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
    {
        await InvalidateWithFenceAsync(
            @event: context.Message,
            eventName: nameof(ProductCreatedEvent),
            eventType: typeof(ProductCreatedEvent).FullName ?? nameof(ProductCreatedEvent),
            consumerName: ProductCreatedConsumerName,
            tenantId: context.Message.TenantId,
            productId: context.Message.ProductId,
            slug: context.Message.Slug,
            correlationId: context.Message.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A",
            invalidateHomepage: true,
            cancellationToken: context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        await InvalidateWithFenceAsync(
            @event: context.Message,
            eventName: nameof(ProductUpdatedEvent),
            eventType: typeof(ProductUpdatedEvent).FullName ?? nameof(ProductUpdatedEvent),
            consumerName: ProductUpdatedConsumerName,
            tenantId: context.Message.TenantId,
            productId: context.Message.ProductId,
            slug: context.Message.Slug,
            correlationId: context.Message.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A",
            invalidateHomepage: true,
            cancellationToken: context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        await InvalidateWithFenceAsync(
            @event: context.Message,
            eventName: nameof(ProductDeletedEvent),
            eventType: typeof(ProductDeletedEvent).FullName ?? nameof(ProductDeletedEvent),
            consumerName: ProductDeletedConsumerName,
            tenantId: context.Message.TenantId,
            productId: context.Message.ProductId,
            slug: null,
            correlationId: context.Message.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A",
            invalidateHomepage: true,
            cancellationToken: context.CancellationToken);
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

    public async Task Consume(ConsumeContext<ProductPublishStatusChangedEvent> context)
    {
        await InvalidateWithFenceAsync(
            @event: context.Message,
            eventName: nameof(ProductPublishStatusChangedEvent),
            eventType: typeof(ProductPublishStatusChangedEvent).FullName ?? nameof(ProductPublishStatusChangedEvent),
            consumerName: ProductPublicationConsumerName,
            tenantId: context.Message.TenantId,
            productId: context.Message.ProductId,
            slug: null,
            correlationId: context.Message.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A",
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
        var instanceName = _redisSettings.InstanceName;

        _logger.LogInformation(
            "Processing cache invalidation | Event: {Event} | EventId: {EventId} | ProductId: {ProductId} | Slug: {Slug} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            eventName,
            eventId,
            productId,
            slug,
            resolvedTenantId,
            correlationId);

        try
        {
            // Core product cache invalidation
            await _cacheService.InvalidateAllProductsAsync(resolvedTenantId, cancellationToken);
            await _cacheService.InvalidateProductAsync(resolvedTenantId, productId, slug, cancellationToken);

            // Promotion cache invalidation (product-specific promotions may reference this product)
            await _jsonCacheService.RemoveAsync(
                CacheKeyBuilder.PromotionsByProductKey(instanceName, resolvedTenantId, productId),
                cancellationToken);

            // Homepage cache invalidation (featured products, new arrivals, best sellers may include this product)
            if (invalidateHomepage)
            {
                await _jsonCacheService.RemoveByPatternAsync(
                    CacheKeyBuilder.HomePatternKey(instanceName, resolvedTenantId),
                    cancellationToken);
            }

            _logger.LogInformation(
                "Cache invalidation completed | Event: {Event} | EventId: {EventId} | ProductId: {ProductId} | Slug: {Slug} | TenantId: {TenantId} | HomepageInvalidated: {HomepageInvalidated}",
                eventName,
                eventId,
                productId,
                slug,
                resolvedTenantId,
                invalidateHomepage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache invalidation failed (non-fatal) | Event: {Event} | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId}",
                eventName,
                eventId,
                productId,
                resolvedTenantId);
        }
    }
}
