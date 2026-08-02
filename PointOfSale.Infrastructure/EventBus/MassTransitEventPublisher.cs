using MassTransit;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.EventBus;

public class MassTransitEventPublisher : IEventPublisher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IBus _bus;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitEventPublisher> _logger;
    private readonly ITenantContext _tenantContext;

    public MassTransitEventPublisher(
        ApplicationDbContext dbContext,
        IBus bus,
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitEventPublisher> logger,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _bus = bus;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        await PublishIntegrationEventAsync(@event, typeof(T), cancellationToken);
    }

    public async Task PublishAsync(object @event, CancellationToken cancellationToken = default)
    {
        if (@event is IntegrationEvent integrationEvent)
        {
            await PublishIntegrationEventAsync(integrationEvent, @event.GetType(), cancellationToken);
            return;
        }

        var eventType = @event.GetType();
        var eventId = eventType.GetProperty("EventId")?.GetValue(@event)?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Publishing Legacy Event | ID: {EventId} | Type: {EventType}",
            eventId,
            eventType.Name);

        EnsureTransactionalPublishBoundary(eventType);

        var shouldFlushOutbox = CanFlushOutboxImmediately();
        var outboxSnapshot = _dbContext.ChangeTracker.Entries()
            .Where(e => IsOutboxInfrastructureEntity(e.Entity))
            .Select(e => e.Entity)
            .ToHashSet();

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);
        }
        catch
        {
            var outboxEntries = _dbContext.ChangeTracker.Entries()
                .Where(e => IsOutboxInfrastructureEntity(e.Entity) && !outboxSnapshot.Contains(e.Entity))
                .ToList();

            foreach (var entry in outboxEntries)
            {
                entry.State = EntityState.Detached;
            }

            throw;
        }

        if (shouldFlushOutbox)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Legacy Event Published Successfully | ID: {EventId} | Type: {EventType}",
            eventId,
            eventType.Name);
    }

    private async Task PublishIntegrationEventAsync(
        IntegrationEvent @event,
        Type eventType,
        CancellationToken cancellationToken)
    {
        // Enrich event with tenant context
        EnrichEventWithTenantContext(@event);
        
        // Enrich event metadata
        if (string.IsNullOrEmpty(@event.CorrelationId))
        {
            @event.CorrelationId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString();
        }

        if (string.IsNullOrEmpty(@event.IdempotencyKey))
        {
            // Fallback to EventId if no business key provided
            @event.IdempotencyKey = @event.EventId.ToString();
        }

        _logger.LogInformation(
            "Publishing Event | ID: {EventId} | Type: {EventType} | Ver: {Version} | CorrId: {CorrelationId} | IdempKey: {IdempotencyKey} | TenantId: {TenantId}", 
            @event.EventId, 
            eventType.Name, 
            @event.Version, 
            @event.CorrelationId,
            @event.IdempotencyKey,
            @event.TenantId);

        await EnrichAuthoritativeOrderingMetadataAsync(@event, eventType, cancellationToken);
        EnsureAuthoritativeTenantScope(@event, eventType);
        EnsureTransactionalPublishBoundary(eventType);

        var shouldFlushOutbox = CanFlushOutboxImmediately();
        
        // Snapshot existing outbox entities before publish to avoid detaching unrelated messages on failure
        var outboxSnapshot = _dbContext.ChangeTracker.Entries()
            .Where(e => IsOutboxInfrastructureEntity(e.Entity))
            .Select(e => e.Entity)
            .ToHashSet();
        
        try
        {
            await _publishEndpoint.Publish(@event, eventType, cancellationToken);
        }
        catch
        {
            // Clean up only NEW outbox entities created by this failed publish attempt
            // Preserve pre-existing outbox messages that were queued before this operation
            var outboxEntries = _dbContext.ChangeTracker.Entries()
                .Where(e => IsOutboxInfrastructureEntity(e.Entity) && !outboxSnapshot.Contains(e.Entity))
                .ToList();

            foreach (var entry in outboxEntries)
            {
                entry.State = EntityState.Detached;
            }

            throw;
        }

        // A number of legacy producers publish after their domain SaveChanges call.
        // When there are no pending domain mutations left in the DbContext, flush the
        // outbox entry immediately so the publish still remains outbox-backed.
        if (shouldFlushOutbox)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        
        _logger.LogInformation("Event Published Successfully | ID: {EventId}", @event.EventId);
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        var shouldFlushOutbox = CanFlushOutboxImmediately();

        // Snapshot existing outbox entities BEFORE the batch to ensure full rollback on any failure
        // This prevents partial delivery where earlier successful publishes remain tracked after a later failure
        var initialOutboxSnapshot = _dbContext.ChangeTracker.Entries()
            .Where(e => IsOutboxInfrastructureEntity(e.Entity))
            .Select(e => e.Entity)
            .ToHashSet();

        foreach (var @event in events)
        {
            try
            {
                EnrichEventWithTenantContext(@event);

                if (string.IsNullOrEmpty(@event.CorrelationId))
                {
                    @event.CorrelationId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString();
                }

                if (string.IsNullOrEmpty(@event.IdempotencyKey))
                {
                    @event.IdempotencyKey = @event.EventId.ToString();
                }

                var eventType = @event.GetType();
                await EnrichAuthoritativeOrderingMetadataAsync(@event, eventType, cancellationToken);
                EnsureTransactionalPublishBoundary(eventType);
                EnsureAuthoritativeTenantScope(@event, eventType);

                await _publishEndpoint.Publish(@event, cancellationToken);
            }
            catch
            {
                // Clean up ALL NEW outbox entities created during this batch (not just the failed one)
                // This ensures the entire batch is rolled back, preventing partial delivery
                var outboxEntries = _dbContext.ChangeTracker.Entries()
                    .Where(e => IsOutboxInfrastructureEntity(e.Entity) && !initialOutboxSnapshot.Contains(e.Entity))
                    .ToList();

                foreach (var entry in outboxEntries)
                {
                    entry.State = EntityState.Detached;
                }

                throw;
            }
        }

        if (shouldFlushOutbox)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent, ISensitiveIntegrationEvent
    {
        // SECURITY: Use runtime type to detect authoritative events (prevents bypass via derived types)
        if (IsAuthoritativeEventType(@event.GetType()))
        {
            throw new InvalidOperationException(
                $"{@event.GetType().Name} is authoritative and cannot bypass the transactional outbox via PublishSensitiveAsync.");
        }

        // Enrich event with tenant context
        EnrichEventWithTenantContext(@event);
        
        // Enrich event metadata
        if (string.IsNullOrEmpty(@event.CorrelationId))
        {
            @event.CorrelationId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString();
        }

        if (string.IsNullOrEmpty(@event.IdempotencyKey))
        {
            @event.IdempotencyKey = @event.EventId.ToString();
        }

        _logger.LogInformation(
            "Publishing Sensitive Event (Direct Bus Publish / Outbox Bypass) | ID: {EventId} | Type: {EventType} | Ver: {Version} | CorrId: {CorrelationId} | TenantId: {TenantId}", 
            @event.EventId, 
            typeof(T).Name, 
            @event.Version, 
            @event.CorrelationId,
            @event.TenantId);

        // Snapshot existing outbox entities before publish to avoid detaching unrelated messages on failure
        var outboxSnapshot = _dbContext.ChangeTracker.Entries()
            .Where(e => IsOutboxInfrastructureEntity(e.Entity))
            .Select(e => e.Entity)
            .ToHashSet();

        try
        {
            // Publish via IBus to bypass the bus outbox interception that wraps
            // IPublishEndpoint/ISendEndpointProvider in the current DI scope.
            await _bus.Publish(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to publish sensitive event | ID: {EventId} | Type: {EventType} | CorrId: {CorrelationId}", 
                @event.EventId, 
                typeof(T).Name, 
                @event.CorrelationId);

            // Clean up only NEW outbox entities created by this failed publish attempt
            // Preserve pre-existing outbox messages that were queued before this operation
            var outboxEntries = _dbContext.ChangeTracker.Entries()
                .Where(e => IsOutboxInfrastructureEntity(e.Entity) && !outboxSnapshot.Contains(e.Entity))
                .ToList();

            foreach (var entry in outboxEntries)
            {
                entry.State = EntityState.Detached;
            }

            throw;
        }

        _logger.LogInformation("Sensitive Event Published Successfully (No Outbox) | ID: {EventId}", @event.EventId);
    }
    
    /// <summary>
    /// Enriches the event with tenant context from the current request scope.
    /// If TenantId is already set on the event, it will not be overwritten.
    /// </summary>
    private void EnrichEventWithTenantContext<T>(T @event) where T : IntegrationEvent
    {
        // Only set TenantId if not already explicitly set and tenant context is available
        if (!@event.TenantId.HasValue && _tenantContext.HasTenant)
        {
            @event.TenantId = _tenantContext.TenantId;
        }
        
        // Log warning if publishing without tenant context (helps identify gaps during migration)
        if (!@event.TenantId.HasValue)
        {
            _logger.LogWarning(
                "Publishing event without TenantId | EventId: {EventId} | EventType: {EventType}. " +
                "This may indicate a gap in tenant context propagation.",
                @event.EventId,
                typeof(T).Name);
        }
    }

    private bool CanFlushOutboxImmediately()
    {
        if (_dbContext.Database.CurrentTransaction != null)
        {
            return false;
        }

        return !HasPendingBusinessMutation();
    }

    private void EnsureTransactionalPublishBoundary(Type eventType)
    {
        if (!IsAuthoritativeEventType(eventType))
        {
            return;
        }

        if (_dbContext.Database.CurrentTransaction != null || HasPendingBusinessMutation())
        {
            return;
        }

        throw new InvalidOperationException(
            "Authoritative integration events must be published before the source mutation is committed so the MassTransit outbox row is atomic with the business state. " +
            $"EventType={eventType.Name}");
    }

    private async Task EnrichAuthoritativeOrderingMetadataAsync(
        IntegrationEvent @event,
        Type eventType,
        CancellationToken cancellationToken)
    {
        if (!IsAuthoritativeEventType(eventType))
        {
            return;
        }

        ValidateAuthoritativeEventContract(eventType);

        var aggregateId = IntegrationEventAuthority.TryGetAggregateId(@event);
        if (!aggregateId.HasValue || aggregateId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Authoritative integration events must expose a non-empty aggregate ordering key. " +
                $"EventType={eventType.Name}; EventId={@event.EventId}");
        }

        var commerceHubOrderVersion = await ResolveCommerceHubOrderProjectionVersionAsync(
            @event,
            eventType,
            aggregateId.Value,
            cancellationToken);

        if (commerceHubOrderVersion.HasValue)
        {
            ApplyResolvedAuthoritativeVersion(@event, eventType, commerceHubOrderVersion.Value, "commercehub-order-aggregate");
            return;
        }

        var trackedVersion = ResolveTrackedProjectionVersion(aggregateId.Value);
        if (trackedVersion.HasValue)
        {
            ApplyResolvedAuthoritativeVersion(@event, eventType, trackedVersion.Value, "tracked");
            return;
        }

        var persistedVersion = await ResolvePersistedProjectionVersionAsync(
            eventType,
            aggregateId.Value,
            cancellationToken);

        if (persistedVersion.HasValue)
        {
            ApplyResolvedAuthoritativeVersion(@event, eventType, persistedVersion.Value, "persisted");
        }
    }

    private static void ApplyResolvedAuthoritativeVersion(
        IntegrationEvent @event,
        Type eventType,
        int resolvedVersion,
        string source)
    {
        if (@event.EventVersion <= 1)
        {
            @event.EventVersion = resolvedVersion;
            return;
        }

        if (@event.EventVersion != resolvedVersion)
        {
            throw new InvalidOperationException(
                "Authoritative integration events must use the source aggregate mutation version for deterministic stale-event rejection. " +
                $"EventType={eventType.Name}; EventId={@event.EventId}; EventVersion={@event.EventVersion}; ResolvedVersion={resolvedVersion}; Source={source}");
        }
    }

    private int? ResolveTrackedProjectionVersion(Guid aggregateId)
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<IProjectionVersioned>())
        {
            if (entry.State is EntityState.Detached)
            {
                continue;
            }

            var idProperty = entry.Metadata.FindProperty("Id");
            if (idProperty == null)
            {
                continue;
            }

            if (entry.Property("Id").CurrentValue is not Guid entityId || entityId != aggregateId)
            {
                continue;
            }

            var sourceVersion = ResolvePendingProjectionVersion(entry);

            return ProductEventVersionHelper.ToProductEventVersion(sourceVersion);
        }

        return null;
    }

    private async Task<int?> ResolveCommerceHubOrderProjectionVersionAsync(
        IntegrationEvent @event,
        Type eventType,
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        if (!IsCommerceHubOrderEvent(eventType))
        {
            return null;
        }

        if (@event is CommerceHubSubOrderStatusUpdatedEvent subOrderEvent &&
            subOrderEvent.MarketplaceOrderId != marketplaceOrderId)
        {
            throw new InvalidOperationException(
                "CommerceHub sub-order events must scope their authoritative aggregate to MarketplaceOrderId. " +
                $"EventId={@event.EventId}; AggregateId={marketplaceOrderId}; MarketplaceOrderId={subOrderEvent.MarketplaceOrderId}");
        }

        long? resolvedVersion = ResolveTrackedCommerceHubOrderProjectionMutationVersion(marketplaceOrderId);
        var persistedVersion = await ResolvePersistedCommerceHubOrderProjectionMutationVersionAsync(
            marketplaceOrderId,
            cancellationToken);

        if (persistedVersion.HasValue)
        {
            resolvedVersion = resolvedVersion.HasValue
                ? Math.Max(resolvedVersion.Value, persistedVersion.Value)
                : persistedVersion.Value;
        }

        return resolvedVersion.HasValue
            ? ProductEventVersionHelper.ToProductEventVersion(resolvedVersion.Value)
            : null;
    }

    private long? ResolveTrackedCommerceHubOrderProjectionMutationVersion(Guid marketplaceOrderId)
    {
        long? maxVersion = null;

        foreach (var entry in _dbContext.ChangeTracker.Entries<CommerceHubMarketplaceOrder>())
        {
            if (entry.State is EntityState.Detached)
            {
                continue;
            }

            if (entry.Entity.Id != marketplaceOrderId)
            {
                continue;
            }

            maxVersion = MaxNullable(maxVersion, ResolvePendingProjectionVersion(entry));
        }

        foreach (var entry in _dbContext.ChangeTracker.Entries<CommerceHubSubOrder>())
        {
            if (entry.State is EntityState.Detached)
            {
                continue;
            }

            var subOrderMarketplaceOrderId = entry.Entity.MarketplaceOrderId != Guid.Empty
                ? entry.Entity.MarketplaceOrderId
                : entry.Entity.MarketplaceOrder?.Id ?? Guid.Empty;

            if (subOrderMarketplaceOrderId != marketplaceOrderId)
            {
                continue;
            }

            maxVersion = MaxNullable(maxVersion, ResolvePendingProjectionVersion(entry));
        }

        return maxVersion;
    }

    private async Task<long?> ResolvePersistedCommerceHubOrderProjectionMutationVersionAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        var orderVersion = await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(order => order.Id == marketplaceOrderId)
            .Select(order => (long?)order.ProjectionMutationVersion)
            .FirstOrDefaultAsync(cancellationToken);

        var subOrderVersion = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(subOrder => subOrder.MarketplaceOrderId == marketplaceOrderId)
            .Select(subOrder => (long?)subOrder.ProjectionMutationVersion)
            .MaxAsync(cancellationToken);

        if (!orderVersion.HasValue)
        {
            return subOrderVersion;
        }

        return subOrderVersion.HasValue
            ? Math.Max(orderVersion.Value, subOrderVersion.Value)
            : orderVersion.Value;
    }

    private static bool IsCommerceHubOrderEvent(Type eventType)
    {
        var name = eventType.Name;
        return name.StartsWith("CommerceHub", StringComparison.OrdinalIgnoreCase) &&
               name.Contains("Order", StringComparison.OrdinalIgnoreCase);
    }

    private static long MaxNullable(long? left, long right) =>
        left.HasValue ? Math.Max(left.Value, right) : right;

    private async Task<int?> ResolvePersistedProjectionVersionAsync(
        Type eventType,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var name = eventType.Name;

        if (name.StartsWith("CommerceHub", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("Order", StringComparison.OrdinalIgnoreCase))
        {
            var version = await ResolvePersistedCommerceHubOrderProjectionMutationVersionAsync(
                aggregateId,
                cancellationToken);

            return version.HasValue
                ? ProductEventVersionHelper.ToProductEventVersion(version.Value)
                : null;
        }
        
        // Order events
        if (name.Contains("Order", StringComparison.OrdinalIgnoreCase))
        {
            var version = await _dbContext.Orders
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(order => order.Id == aggregateId)
                .Select(order => order.ProjectionMutationVersion)
                .FirstOrDefaultAsync(cancellationToken);

            return ProductEventVersionHelper.ToProductEventVersion(version);
        }

        // Product events (exclude Variant events)
        if (name.Contains("Product", StringComparison.OrdinalIgnoreCase) && 
            !name.Contains("Variant", StringComparison.OrdinalIgnoreCase))
        {
            var version = await _dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.Id == aggregateId)
                .Select(product => product.ProjectionMutationVersion)
                .FirstOrDefaultAsync(cancellationToken);

            return ProductEventVersionHelper.ToProductEventVersion(version);
        }

        // Variant events (ProductVariant) - check BEFORE Stock to handle ProductVariantStockChangedEvent correctly
        if (name.Contains("Variant", StringComparison.OrdinalIgnoreCase))
        {
            var version = await _dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(variant => variant.Id == aggregateId)
                .Select(variant => variant.ProjectionMutationVersion)
                .FirstOrDefaultAsync(cancellationToken);

            return ProductEventVersionHelper.ToProductEventVersion(version);
        }

        // Storefront promotion events
        if (name.Contains("Promotion", StringComparison.OrdinalIgnoreCase))
        {
            var version = await _dbContext.Promotions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(promotion => promotion.Id == aggregateId)
                .Select(promotion => promotion.ProjectionMutationVersion)
                .FirstOrDefaultAsync(cancellationToken);

            return ProductEventVersionHelper.ToProductEventVersion(version);
        }

        // Stock events (ProductVariantStock)
        if (name.Contains("Stock", StringComparison.OrdinalIgnoreCase))
        {
            var version = await _dbContext.ProductVariantStocks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(stock => stock.Id == aggregateId)
                .Select(stock => stock.ProjectionMutationVersion)
                .FirstOrDefaultAsync(cancellationToken);

            return ProductEventVersionHelper.ToProductEventVersion(version);
        }

        return null;
    }

    private static long ResolvePendingProjectionVersion(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var entity = (IProjectionVersioned)entry.Entity;

        return entry.State switch
        {
            EntityState.Added => entity.ProjectionMutationVersion > 0
                ? entity.ProjectionMutationVersion
                : 1,
            EntityState.Modified or EntityState.Deleted => IsProjectionVersionManuallyAdvanced(entry)
                ? entity.ProjectionMutationVersion
                : entity.ProjectionMutationVersion + 1,
            _ => entity.ProjectionMutationVersion
        };
    }

    private static bool IsProjectionVersionManuallyAdvanced(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var property = entry.Property(nameof(IProjectionVersioned.ProjectionMutationVersion));
        return property.IsModified;
    }

    private static void EnsureAuthoritativeTenantScope(IntegrationEvent @event, Type eventType)
    {
        if (!IsAuthoritativeEventType(eventType))
        {
            return;
        }

        ValidateAuthoritativeEventContract(eventType);

        if (@event.TenantId.HasValue && @event.TenantId.Value != Guid.Empty)
        {
            EnsureAuthoritativeOrderingMetadata(@event, eventType);
            EnsureAffectedTenantScope(@event, eventType);
            return;
        }

        throw new InvalidOperationException(
            "Authoritative integration events must carry a non-empty TenantId before publishing. " +
            $"EventType={eventType.Name}; EventId={@event.EventId}");
    }

    private static void EnsureAuthoritativeOrderingMetadata(IntegrationEvent @event, Type eventType)
    {
        if (@event.OccurredAtUtc == default || @event.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                "Authoritative integration events must carry a non-default UTC OccurredAtUtc for deterministic ordering. " +
                $"EventType={eventType.Name}; EventId={@event.EventId}; OccurredAtUtc={@event.OccurredAtUtc:o}; Kind={@event.OccurredAtUtc.Kind}");
        }

        if (@event.EventVersion <= 0)
        {
            throw new InvalidOperationException(
                "Authoritative integration events must carry a positive EventVersion for stale-event rejection. " +
                $"EventType={eventType.Name}; EventId={@event.EventId}; EventVersion={@event.EventVersion}");
        }
    }

    private static void EnsureAffectedTenantScope(IntegrationEvent @event, Type eventType)
    {
        if (@event is not IAffectedTenantScopedIntegrationEvent affectedTenantEvent)
        {
            return;
        }

        var affectedTenantIds = affectedTenantEvent.GetAffectedTenantIds()
            .Where(tenantId => tenantId != Guid.Empty)
            .Distinct()
            .ToList();

        if (affectedTenantIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Affected-tenant authoritative events must carry at least one affected tenant. " +
                $"EventType={eventType.Name}; EventId={@event.EventId}");
        }

        if (!@event.TenantId.HasValue || !affectedTenantIds.Contains(@event.TenantId.Value))
        {
            throw new InvalidOperationException(
                "Authoritative event TenantId must be one of the affected tenant ids. " +
                $"EventType={eventType.Name}; EventId={@event.EventId}; TenantId={@event.TenantId}; AffectedTenantCount={affectedTenantIds.Count}");
        }
    }

    private bool HasPendingBusinessMutation()
    {
        return _dbContext.ChangeTracker.Entries()
            .Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                !IsOutboxInfrastructureEntity(entry.Entity));
    }

    private static bool IsAuthoritativeEventType(Type eventType) =>
        IntegrationEventAuthority.IsAuthoritativeEventType(eventType);

    private static void ValidateAuthoritativeEventContract(Type eventType) =>
        IntegrationEventAuthority.ValidateAuthoritativeEventContract(eventType);

    private static bool IsOutboxInfrastructureEntity(object entity)
    {
        return entity is OutboxMessage or AccountingOutboxMessage or MarketplaceEventOutboxMessage or StockSyncOutboxMessage ||
               entity.GetType().FullName?.StartsWith("MassTransit.EntityFrameworkCoreIntegration.Outbox", StringComparison.Ordinal) == true;
    }
}
