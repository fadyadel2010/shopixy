using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Tenant currency changes to MongoDB storefront_configs.
/// 
/// PHASE 10.4.X: Currency Authority Lockdown
/// 
/// This consumer is the ONLY mechanism for updating currency in MongoDB.
/// 
/// ARCHITECTURAL RULES:
/// - Consumes TenantCurrencyChangedEvent
/// - Updates storefront_configs.currency in MongoDB
/// - Invalidates Redis store config cache
/// - Re-reads Tenant.Currency from SQL before mutation; event payload is replay evidence only
/// - Idempotent (can safely reprocess same event)
/// - Order-agnostic (only cares about latest currency)
/// </summary>
public class TenantCurrencyProjectionConsumer : IConsumer<TenantCurrencyChangedEvent>
{
    private readonly IMongoCollection<StorefrontConfigDocument> _collection;
    private readonly ApplicationDbContext _dbContext;
    private readonly IStoreConfigCacheService _cacheService;
    private readonly ILogger<TenantCurrencyProjectionConsumer> _logger;

    public TenantCurrencyProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ApplicationDbContext dbContext,
        IStoreConfigCacheService cacheService,
        ILogger<TenantCurrencyProjectionConsumer> logger)
    {
        _collection = mongoDatabase.GetCollection<StorefrontConfigDocument>("storefront_configs");
        _dbContext = dbContext;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TenantCurrencyChangedEvent> context)
    {
        var @event = context.Message;
        
        // Validate TenantId (nullable in base class)
        if (!@event.TenantId.HasValue || @event.TenantId == Guid.Empty)
        {
            _logger.LogError(
                "TenantCurrencyChangedEvent has invalid TenantId - REJECTING | CorrelationId: {CorrelationId}",
                @event.CorrelationId);
            throw new InvalidOperationException("TenantId is required for currency projection");
        }
        
        var tenantId = @event.TenantId.Value;
        
        _logger.LogInformation(
            "Processing TenantCurrencyChangedEvent | TenantId: {TenantId} | Currency: {Currency} | Previous: {Previous} | CorrelationId: {CorrelationId}",
            tenantId, @event.Currency, @event.PreviousCurrency ?? "(none)", @event.CorrelationId);

        try
        {
            await using var fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                new ProjectionEventFenceKey(
                    nameof(TenantCurrencyProjectionConsumer),
                    nameof(TenantCurrencyChangedEvent),
                    tenantId),
                _logger,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "TenantCurrencyChangedEvent collapsed by SQL event fence | TenantId: {TenantId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    tenantId,
                    @event.EventId,
                    @event.EventVersion);
                return;
            }

            var sqlCurrency = await ResolveCurrentSqlTenantCurrencyAsync(tenantId, context.CancellationToken);

            // 2. Update MongoDB - only the currency field (atomic update).
            // The payload currency is deliberately ignored here; delayed or replayed events
            // can only trigger convergence to current SQL tenant authority.
            var occurredAtUtc = @event.OccurredAtUtc.Kind == DateTimeKind.Utc
                ? @event.OccurredAtUtc
                : DateTime.SpecifyKind(@event.OccurredAtUtc, DateTimeKind.Utc);
            var filter = BuildFreshCurrencyEventFilter(tenantId, @event.EventId, occurredAtUtc, @event.EventVersion);
            var update = Builders<StorefrontConfigDocument>.Update
                .Set(x => x.Currency, sqlCurrency)
                .Set(x => x.LastCurrencyEventId, @event.EventId)
                .Set(x => x.LastCurrencyEventOccurredAtUtc, occurredAtUtc)
                .Set(x => x.LastCurrencyEventVersion, @event.EventVersion)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(
                filter, 
                update, 
                new UpdateOptions { IsUpsert = false }, // Don't create if doesn't exist
                context.CancellationToken);

            if (result.MatchedCount == 0)
            {
                var tenantConfigExists = await _collection
                    .Find(Builders<StorefrontConfigDocument>.Filter.Eq(x => x.TenantId, tenantId))
                    .AnyAsync(context.CancellationToken);

                if (tenantConfigExists)
                {
                    _logger.LogInformation(
                        "TenantCurrencyChangedEvent ignored as stale or duplicate | TenantId: {TenantId} | Currency: {Currency} | EventId: {EventId} | EventVersion: {EventVersion} | CorrelationId: {CorrelationId}",
                        tenantId, sqlCurrency, @event.EventId, @event.EventVersion, @event.CorrelationId);
                    await fence.CompleteAsync(@event, new ProjectionEventFenceKey(
                        nameof(TenantCurrencyProjectionConsumer),
                        nameof(TenantCurrencyChangedEvent),
                        tenantId), context.CancellationToken);
                    return;
                }

                // StorefrontConfig document doesn't exist yet - this is OK during initial tenant creation
                // The StorefrontConfigProjectionConsumer will create it with the correct currency
                _logger.LogWarning(
                    "StorefrontConfig not found for tenant - will be created by StoreConfig projection | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, @event.CorrelationId);
            }
            else if (result.ModifiedCount == 0)
            {
                _logger.LogInformation(
                    "TenantCurrencyChangedEvent ignored as stale or duplicate | TenantId: {TenantId} | Currency: {Currency} | EventId: {EventId} | EventVersion: {EventVersion} | CorrelationId: {CorrelationId}",
                    tenantId, sqlCurrency, @event.EventId, @event.EventVersion, @event.CorrelationId);
                await fence.CompleteAsync(@event, new ProjectionEventFenceKey(
                    nameof(TenantCurrencyProjectionConsumer),
                    nameof(TenantCurrencyChangedEvent),
                    tenantId), context.CancellationToken);
                return;
            }
            else
            {
                _logger.LogInformation(
                    "MongoDB currency updated | TenantId: {TenantId} | Currency: {Currency} | ModifiedCount: {ModifiedCount} | CorrelationId: {CorrelationId}",
                    tenantId, sqlCurrency, result.ModifiedCount, @event.CorrelationId);
            }

            // 3. Invalidate Redis cache using the proper cache service (correct key format)
            await InvalidateRedisCacheAsync(tenantId, @event.CorrelationId);
            await fence.CompleteAsync(@event, new ProjectionEventFenceKey(
                nameof(TenantCurrencyProjectionConsumer),
                nameof(TenantCurrencyChangedEvent),
                tenantId), context.CancellationToken);

            _logger.LogInformation(
                "TenantCurrencyChangedEvent processed successfully | TenantId: {TenantId} | Currency: {Currency} | CorrelationId: {CorrelationId}",
                tenantId, sqlCurrency, @event.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to project currency change | TenantId: {TenantId} | Currency: {Currency} | CorrelationId: {CorrelationId}",
                tenantId, @event.Currency, @event.CorrelationId);
            throw; // Retry via MassTransit
        }
    }

    private async Task<string> ResolveCurrentSqlTenantCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(
            tenantCurrency,
            $"Consumer: TenantCurrencyProjectionConsumer, Method: ResolveCurrentSqlTenantCurrencyAsync, TenantId: {tenantId}",
            validateIso: true);
    }

    private async Task InvalidateRedisCacheAsync(Guid tenantId, string? correlationId)
    {
        try
        {
            // Use IStoreConfigCacheService for correct cache key format
            // Format: {instanceName}:tenant:{tenantId:N}:store:config
            var invalidated = await _cacheService.InvalidateStoreConfigAsync(tenantId);
            
            if (invalidated)
            {
                _logger.LogInformation(
                    "Redis cache invalidated for currency change | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);
            }
            else
            {
                _logger.LogWarning(
                    "Redis cache invalidation returned false | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    tenantId, correlationId);
            }
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal - log warning but don't fail the projection
            _logger.LogWarning(ex,
                "Failed to invalidate Redis cache (non-fatal) | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId, correlationId);
        }
    }

    private static FilterDefinition<StorefrontConfigDocument> BuildFreshCurrencyEventFilter(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<StorefrontConfigDocument>.Filter;

        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Ne(x => x.LastCurrencyEventId, eventId),
            filter.Or(
                filter.Exists(x => x.LastCurrencyEventOccurredAtUtc, false),
                filter.Eq(x => x.LastCurrencyEventOccurredAtUtc, null),
                filter.Lt(x => x.LastCurrencyEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(x => x.LastCurrencyEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(x => x.LastCurrencyEventVersion, eventVersion))));
    }
}

/// <summary>
/// Consumer definition with retry configuration.
/// </summary>
public class TenantCurrencyProjectionConsumerDefinition : ConsumerDefinition<TenantCurrencyProjectionConsumer>
{
    public TenantCurrencyProjectionConsumerDefinition()
    {
        EndpointName = "tenant-currency-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator, 
        IConsumerConfigurator<TenantCurrencyProjectionConsumer> consumerConfigurator)
    {
        // Retry on transient failures
        endpointConfigurator.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
    }
}

