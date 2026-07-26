using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for shipping method events.
/// Projects shipping methods from PostgreSQL to MongoDB for Storefront read access.
/// 
/// ARCHITECTURE:
/// - PostgreSQL is the source of truth (Write Model)
/// - MongoDB is the read model (for Storefront)
/// - Consumer ALWAYS re-fetches from PostgreSQL (never trusts event payload)
/// - Redis cache is invalidated after each projection
/// 
/// Handles:
/// - ShippingMethodUpdatedEvent (create, update, activate, deactivate, reorder)
/// - ShippingMethodDeletedEvent (soft delete)
/// </summary>
public class ShippingMethodProjectionConsumer :
    IConsumer<ShippingMethodUpdatedEvent>,
    IConsumer<ShippingMethodDeletedEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShippingMethodReadRepository _repository;
    private readonly IConnectionMultiplexer _redis;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<ShippingMethodProjectionConsumer> _logger;
    private const string ConsumerName = nameof(ShippingMethodProjectionConsumer);

    public ShippingMethodProjectionConsumer(
        ApplicationDbContext dbContext,
        IShippingMethodReadRepository repository,
        IConnectionMultiplexer redis,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<ShippingMethodProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _redis = redis;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Handles ShippingMethodUpdatedEvent.
    /// Re-fetches from PostgreSQL and projects to MongoDB.
    /// </summary>
    public async Task Consume(ConsumeContext<ShippingMethodUpdatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.MessageId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ShippingMethodUpdatedEvent | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.TenantId,
            @event.ShippingMethodId,
            correlationId);

        // CRITICAL: TenantId validation
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ShippingMethodUpdatedEvent rejected: TenantId is missing | EventId: {EventId} | ShippingMethodId: {ShippingMethodId}",
                @event.EventId,
                @event.ShippingMethodId);
            return; // Acknowledge to prevent retry storm
        }

        if (@event.ShippingMethodId == Guid.Empty)
        {
            _logger.LogError(
                "ShippingMethodUpdatedEvent rejected: ShippingMethodId is empty | EventId: {EventId} | TenantId: {TenantId}",
                @event.EventId,
                @event.TenantId);
            return;
        }

        try
        {
            var key = new ProjectionEventFenceKey(ConsumerName, nameof(ShippingMethodUpdatedEvent), @event.ShippingMethodId);
            await using var fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                key,
                _logger,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "ShippingMethodUpdatedEvent duplicate or active replay collapsed before MongoDB/cache side effects | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
                    @event.EventId,
                    @event.TenantId,
                    @event.ShippingMethodId);
                return;
            }

            // Step 1: Re-fetch from PostgreSQL (Source of Truth)
            var shippingMethod = await _dbContext.ShippingMethods
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    x => x.Id == @event.ShippingMethodId && x.TenantId == @event.TenantId.Value,
                    context.CancellationToken);

            if (shippingMethod == null)
            {
                _logger.LogWarning(
                    "ShippingMethod not found in PostgreSQL | ShippingMethodId: {ShippingMethodId} | TenantId: {TenantId}. Skipping projection.",
                    @event.ShippingMethodId,
                    @event.TenantId);
                await fence.CompleteAsync(@event, key, context.CancellationToken);
                return;
            }

            // Check if soft-deleted - if so, remove from MongoDB
            if (shippingMethod.IsDeleted)
            {
                await TombstoneInMongoAndInvalidateCacheAsync(
                    @event.TenantId.Value,
                    @event.ShippingMethodId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    @event.Version,
                    correlationId,
                    context.CancellationToken);
                await fence.CompleteAsync(@event, key, context.CancellationToken);
                return;
            }

            var tenantCurrency = await ResolveTenantCurrencyAsync(@event.TenantId.Value, context.CancellationToken);

            // Step 2: Map to MongoDB document
            var document = MapToDocument(shippingMethod, tenantCurrency, @event.EventId);

            // Step 3: Upsert to MongoDB
            var success = await _repository.UpsertIfFreshAsync(
                document,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.Version,
                context.CancellationToken);

            if (success)
            {
                _logger.LogInformation(
                    "ShippingMethod projected successfully | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | Name: {Name}",
                    @event.TenantId,
                    @event.ShippingMethodId,
                    shippingMethod.Name);
            }
            else
            {
                _logger.LogWarning(
                    "ShippingMethodUpdatedEvent ignored as stale | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | EventVersion: {EventVersion}",
                    @event.EventId,
                    @event.TenantId,
                    @event.ShippingMethodId,
                    @event.Version);
                await fence.CompleteAsync(@event, key, context.CancellationToken);
                return;
            }

            // Step 4: Invalidate Redis cache
            await InvalidateCacheAsync(@event.TenantId.Value, correlationId);
            await fence.CompleteAsync(@event, key, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to project ShippingMethod | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
                @event.TenantId,
                @event.ShippingMethodId);
            throw; // Re-throw to trigger MassTransit retry
        }
    }

    /// <summary>
    /// Handles ShippingMethodDeletedEvent.
    /// Removes the shipping method from MongoDB.
    /// </summary>
    public async Task Consume(ConsumeContext<ShippingMethodDeletedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.MessageId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ShippingMethodDeletedEvent | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
            @event.EventId,
            @event.TenantId,
            @event.ShippingMethodId);

        // CRITICAL: TenantId validation
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ShippingMethodDeletedEvent rejected: TenantId is missing | EventId: {EventId}",
                @event.EventId);
            return;
        }

        if (@event.ShippingMethodId == Guid.Empty)
        {
            _logger.LogError(
                "ShippingMethodDeletedEvent rejected: ShippingMethodId is empty | EventId: {EventId}",
                @event.EventId);
            return;
        }

        try
        {
            var key = new ProjectionEventFenceKey(ConsumerName, nameof(ShippingMethodDeletedEvent), @event.ShippingMethodId);
            await using var fence = await ProjectionEventFence.TryAcquireAsync(
                _dbContext,
                @event,
                key,
                _logger,
                context.CancellationToken);

            if (fence == null)
            {
                _logger.LogInformation(
                    "ShippingMethodDeletedEvent duplicate or active replay collapsed before MongoDB/cache side effects | EventId: {EventId} | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
                    @event.EventId,
                    @event.TenantId,
                    @event.ShippingMethodId);
                return;
            }

            await TombstoneInMongoAndInvalidateCacheAsync(
                @event.TenantId.Value,
                @event.ShippingMethodId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.Version,
                correlationId,
                context.CancellationToken);
            await fence.CompleteAsync(@event, key, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to delete ShippingMethod from MongoDB | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
                @event.TenantId,
                @event.ShippingMethodId);
            throw;
        }
    }

    private async Task TombstoneInMongoAndInvalidateCacheAsync(
        Guid tenantId,
        Guid shippingMethodId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var applied = await _repository.TombstoneIfFreshAsync(
            tenantId,
            shippingMethodId,
            eventId,
            occurredAtUtc,
            eventVersion,
            cancellationToken);

        if (!applied)
        {
            _logger.LogWarning(
                "ShippingMethod delete/tombstone ignored as stale | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | EventId: {EventId} | EventVersion: {EventVersion}",
                tenantId,
                shippingMethodId,
                eventId,
                eventVersion);
            return;
        }

        _logger.LogInformation(
            "ShippingMethod tombstoned in MongoDB | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | Applied: {Applied}",
            tenantId,
            shippingMethodId,
            applied);

        await InvalidateCacheAsync(tenantId, correlationId);
    }

    private async Task InvalidateCacheAsync(Guid tenantId, string correlationId)
    {
        try
        {
            var db = _redis.GetDatabase();
            // Use the standard cache key pattern from CacheKeyBuilder
            var cacheKey = $"pos:tenant:{tenantId:N}:shipping-methods";
            
            var deleted = await db.KeyDeleteAsync(cacheKey);
            
            _logger.LogInformation(
                "Redis cache invalidated | Key: {Key} | TenantId: {TenantId} | Deleted: {Deleted} | CorrelationId: {CorrelationId}",
                cacheKey,
                tenantId,
                deleted,
                correlationId);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal
            _logger.LogWarning(ex,
                "Failed to invalidate Redis cache (non-fatal) | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);
        }
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        try
        {
            return CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(
                tenantCurrency,
                $"Consumer: ShippingMethodProjectionConsumer, Method: ResolveTenantCurrencyAsync, TenantId: {tenantId}");
        }
        catch (InvalidOperationException)
        {
            _reliabilityTelemetry.RecordCurrencyProjectionMismatch("shipping_methods", "tenant_currency_missing");
            throw;
        }
    }

    private static ShippingMethodDocument MapToDocument(Core.Entities.ShippingMethod entity, string tenantCurrency, Guid eventId)
    {
        return new ShippingMethodDocument
        {
            TenantId = entity.TenantId,
            ShippingMethodId = entity.Id,
            Name = entity.Name,
            NameAr = entity.NameAr,
            Description = entity.Description,
            BaseRate = entity.BaseRate,
            PerKgRate = entity.PerKgRate,
            FreeShippingThreshold = entity.FreeShippingThreshold,
            Currency = tenantCurrency,
            EstimatedDeliveryDaysMin = entity.EstimatedDeliveryDaysMin,
            EstimatedDeliveryDaysMax = entity.EstimatedDeliveryDaysMax,
            IsActive = entity.IsActive,
            SortOrder = entity.SortOrder,
            CarrierName = entity.CarrierName,
            CarrierCode = entity.CarrierCode, // CARRIER-AGNOSTIC: Link to carrier adapter
            TrackingUrlTemplate = entity.TrackingUrlTemplate,
            AvailableCountries = entity.AvailableCountries,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = eventId
        };
    }
}

/// <summary>
/// Consumer definition for ShippingMethodProjectionConsumer.
/// Configures queue name and retry policy.
/// </summary>
public class ShippingMethodProjectionConsumerDefinition : ConsumerDefinition<ShippingMethodProjectionConsumer>
{
    public ShippingMethodProjectionConsumerDefinition()
    {
        EndpointName = "shipping-method-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ShippingMethodProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, 1000));
    }
}
