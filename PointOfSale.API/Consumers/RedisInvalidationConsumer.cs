using MassTransit;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.Services.MarketplaceFooter;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.API.Consumers;

public sealed class RedisInvalidationConsumer :
    IConsumer<HomepageSectionsPublishedEvent>,
    IConsumer<MarketplaceCmsPagePublishedEvent>,
    IConsumer<MarketplaceCmsPageDeletedEvent>,
    IConsumer<MarketplaceFooterUpdatedEvent>,
    IConsumer<MarketplaceEvents.ProductUpdatedEvent>,
    IConsumer<MarketplaceEvents.OrderCreatedEvent>,
    IConsumer<MarketplaceEvents.PaymentCompletedEvent>,
    IConsumer<MarketplaceEvents.OrderDeliveredEvent>,
    IConsumer<MarketplaceEvents.RefundProcessedEvent>,
    IConsumer<MarketplaceEvents.ReviewApprovedEvent>,
    IConsumer<MarketplaceEvents.ReviewUpdatedEvent>,
    IConsumer<MarketplaceEvents.ReviewRejectedEvent>,
    IConsumer<MarketplaceEvents.ReviewDeletedEvent>
{
    private readonly IMarketplaceCmsPageCacheService _marketplaceCmsPageCacheService;
    private readonly IMarketplaceFooterCacheService _marketplaceFooterCacheService;
    private readonly IRedisJsonCacheService _cacheService;
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<RedisInvalidationConsumer> _logger;

    private const string HomepageConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(HomepageSectionsPublishedEvent);
    private const string MarketplaceCmsPublishedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceCmsPagePublishedEvent);
    private const string MarketplaceCmsDeletedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceCmsPageDeletedEvent);
    private const string MarketplaceFooterConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceFooterUpdatedEvent);
    private const string MarketplaceProductConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.ProductUpdatedEvent);
    private const string MarketplaceOrderCreatedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.OrderCreatedEvent);
    private const string MarketplacePaymentCompletedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.PaymentCompletedEvent);
    private const string MarketplaceOrderDeliveredConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.OrderDeliveredEvent);
    private const string MarketplaceRefundProcessedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.RefundProcessedEvent);
    private const string MarketplaceReviewApprovedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.ReviewApprovedEvent);
    private const string MarketplaceReviewUpdatedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.ReviewUpdatedEvent);
    private const string MarketplaceReviewRejectedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.ReviewRejectedEvent);
    private const string MarketplaceReviewDeletedConsumerName = nameof(RedisInvalidationConsumer) + ":" + nameof(MarketplaceEvents.ReviewDeletedEvent);

    public RedisInvalidationConsumer(
        IMarketplaceCmsPageCacheService marketplaceCmsPageCacheService,
        IMarketplaceFooterCacheService marketplaceFooterCacheService,
        IRedisJsonCacheService cacheService,
        ApplicationDbContext dbContext,
        IOptions<RedisSettings> redisSettings,
        ILogger<RedisInvalidationConsumer> logger)
    {
        _marketplaceCmsPageCacheService = marketplaceCmsPageCacheService;
        _marketplaceFooterCacheService = marketplaceFooterCacheService;
        _cacheService = cacheService;
        _dbContext = dbContext;
        _redisSettings = redisSettings.Value;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<HomepageSectionsPublishedEvent> context)
    {
        await RunIntegrationInvalidationWithFenceAsync(
            context.Message,
            HomepageConsumerName,
            context.Message.TenantId ?? context.Message.EventId,
            async ct =>
            {
                await _cacheService.RemoveByPatternAsync(
                    $"{_redisSettings.InstanceName}:marketplace:homepage:{(context.Message.TenantId?.ToString("N") ?? "global")}:*",
                    ct);
            },
            context.CancellationToken);

        _logger.LogInformation(
            "Redis caches invalidated for homepage publish | TenantId: {TenantId} | EventId: {EventId}",
            context.Message.TenantId,
            context.Message.EventId);
    }

    public Task Consume(ConsumeContext<MarketplaceCmsPagePublishedEvent> context)
        => RunIntegrationInvalidationWithFenceAsync(
            context.Message,
            MarketplaceCmsPublishedConsumerName,
            context.Message.PageGroupId,
            InvalidateMarketplaceCmsCachesAsync,
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceCmsPageDeletedEvent> context)
        => RunIntegrationInvalidationWithFenceAsync(
            context.Message,
            MarketplaceCmsDeletedConsumerName,
            context.Message.PageGroupId,
            InvalidateMarketplaceCmsCachesAsync,
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceFooterUpdatedEvent> context)
        => RunIntegrationInvalidationWithFenceAsync(
            context.Message,
            MarketplaceFooterConsumerName,
            context.Message.EventId,
            _marketplaceFooterCacheService.InvalidateAsync,
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.ProductUpdatedEvent> context)
    {
        if (!context.Message.TenantId.HasValue)
        {
            return Task.CompletedTask;
        }

        return RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceProductConsumerName,
            context.Message.EntityId,
            ct => _cacheService.RemoveByPatternAsync(
                CacheKeyBuilder.MarketplaceProductsPatternKey(_redisSettings.InstanceName, context.Message.TenantId.Value),
                ct),
            context.CancellationToken);
    }

    public Task Consume(ConsumeContext<MarketplaceEvents.OrderCreatedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceOrderCreatedConsumerName,
            context.Message.EntityId,
            ct => InvalidateMarketplaceOrderCachesAsync(context.Message.TenantId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.PaymentCompletedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplacePaymentCompletedConsumerName,
            context.Message.EntityId,
            ct => InvalidateMarketplaceOrderCachesAsync(context.Message.TenantId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.OrderDeliveredEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceOrderDeliveredConsumerName,
            context.Message.EntityId,
            ct => InvalidateMarketplaceOrderCachesAsync(context.Message.TenantId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.RefundProcessedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceRefundProcessedConsumerName,
            context.Message.EntityId,
            ct => InvalidateMarketplaceOrderCachesAsync(context.Message.TenantId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.ReviewApprovedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceReviewApprovedConsumerName,
            context.Message.ProductId,
            ct => InvalidateMarketplaceReviewCachesAsync(context.Message.ProductId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.ReviewUpdatedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceReviewUpdatedConsumerName,
            context.Message.ProductId,
            ct => InvalidateMarketplaceReviewCachesAsync(context.Message.ProductId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.ReviewRejectedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceReviewRejectedConsumerName,
            context.Message.ProductId,
            ct => InvalidateMarketplaceReviewCachesAsync(context.Message.ProductId, ct),
            context.CancellationToken);

    public Task Consume(ConsumeContext<MarketplaceEvents.ReviewDeletedEvent> context)
        => RunMarketplaceInvalidationWithFenceAsync(
            context.Message,
            MarketplaceReviewDeletedConsumerName,
            context.Message.ProductId,
            ct => InvalidateMarketplaceReviewCachesAsync(context.Message.ProductId, ct),
            context.CancellationToken);

    private async Task RunIntegrationInvalidationWithFenceAsync(
        IntegrationEvent @event,
        string consumerName,
        Guid entityId,
        Func<CancellationToken, Task> invalidateAsync,
        CancellationToken cancellationToken)
    {
        var eventType = @event.GetType().FullName ?? @event.GetType().Name;
        var key = new ProjectionEventFenceKey(consumerName, eventType, entityId);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            key,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Duplicate or active Redis invalidation collapsed before cache side effects | EventType: {EventType} | EventId: {EventId} | Consumer: {ConsumerName} | TenantId: {TenantId}",
                eventType,
                @event.EventId,
                consumerName,
                @event.TenantId);
            return;
        }

        await invalidateAsync(cancellationToken);
        await fence.CompleteAsync(@event, key, cancellationToken);
    }

    private async Task RunMarketplaceInvalidationWithFenceAsync(
        IMarketplaceDomainEvent @event,
        string consumerName,
        Guid entityId,
        Func<CancellationToken, Task> invalidateAsync,
        CancellationToken cancellationToken)
    {
        var eventType = @event.GetType().FullName ?? @event.GetType().Name;
        var key = new ProjectionEventFenceKey(consumerName, eventType, entityId);

        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event.EventId,
            @event.TenantId,
            key,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Duplicate or active marketplace Redis invalidation collapsed before cache side effects | EventType: {EventType} | EventId: {EventId} | Consumer: {ConsumerName} | TenantId: {TenantId}",
                eventType,
                @event.EventId,
                consumerName,
                @event.TenantId);
            return;
        }

        await invalidateAsync(cancellationToken);
        await fence.CompleteAsync(
            @event.EventId,
            @event.TenantId,
            @event.CorrelationId,
            key,
            cancellationToken);
    }

    private async Task InvalidateMarketplaceCmsCachesAsync(CancellationToken cancellationToken)
    {
        await _marketplaceCmsPageCacheService.InvalidatePublishedPagesAsync(cancellationToken);
        await _marketplaceFooterCacheService.InvalidateAsync(cancellationToken);
    }

    private async Task InvalidateMarketplaceOrderCachesAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            return;
        }

        await _cacheService.RemoveByPatternAsync(
            CacheKeyBuilder.MarketplaceOrdersPatternKey(_redisSettings.InstanceName, tenantId.Value),
            cancellationToken);
    }

    private async Task InvalidateMarketplaceReviewCachesAsync(Guid productId, CancellationToken cancellationToken)
    {
        await _cacheService.RemoveAsync(
            CacheKeyBuilder.MarketplaceProductRatingSummaryKey(_redisSettings.InstanceName, productId),
            cancellationToken);

        await _cacheService.RemoveByPatternAsync(
            CacheKeyBuilder.MarketplaceProductReviewsPatternKey(_redisSettings.InstanceName, productId),
            cancellationToken);
    }
}
