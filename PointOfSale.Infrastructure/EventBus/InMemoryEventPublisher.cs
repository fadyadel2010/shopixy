using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.EventBus;

/// <summary>
/// In-memory event publisher for development/testing
/// In production, replace with MassTransit + RabbitMQ implementation
/// </summary>
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly ILogger<InMemoryEventPublisher> _logger;
    private static readonly List<IntegrationEvent> _publishedEvents = new();
    
    public InMemoryEventPublisher(ILogger<InMemoryEventPublisher> logger)
    {
        _logger = logger;
    }
    
    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent
    {
        EnsureAuthoritativeTenantScope(@event);

        _logger.LogInformation(
            "Publishing event {EventType} with ID {EventId} at {CreatedAt}",
            @event.EventType,
            @event.Id,
            @event.CreatedAt);
        
        _publishedEvents.Add(@event);
        
        // Log event details for debugging
        _logger.LogDebug("Event details: {@Event}", @event);
        
        return Task.CompletedTask;
    }

    public Task PublishAsync(object @event, CancellationToken cancellationToken = default)
    {
        if (@event is IntegrationEvent integrationEvent)
        {
            return PublishAsync(integrationEvent, cancellationToken);
        }

        _logger.LogInformation(
            "Publishing legacy event {EventType}",
            @event.GetType().Name);

        _logger.LogDebug("Legacy event details: {@Event}", @event);

        return Task.CompletedTask;
    }
    
    public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent
    {
        foreach (var @event in events)
        {
            EnsureAuthoritativeTenantScope(@event);

            _logger.LogInformation(
                "Publishing batch event {EventType} with ID {EventId}",
                @event.EventType,
                @event.Id);
            
            _publishedEvents.Add(@event);
        }
        
        return Task.CompletedTask;
    }

    public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent, ISensitiveIntegrationEvent
    {
        if (IsAuthoritativeEventType(typeof(T)))
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} is authoritative and cannot bypass the transactional outbox via PublishSensitiveAsync.");
        }

        _logger.LogInformation(
            "Publishing sensitive event {EventType} with ID {EventId} at {CreatedAt}",
            @event.EventType,
            @event.Id,
            @event.CreatedAt);
        
        _publishedEvents.Add(@event);
        
        // Log only non-sensitive metadata for debugging (avoid serializing full payload)
        _logger.LogDebug(
            "Sensitive event metadata | EventType: {EventType} | EventId: {EventId} | CreatedAt: {CreatedAt}",
            @event.EventType,
            @event.Id,
            @event.CreatedAt);
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Get all published events (for testing purposes)
    /// </summary>
    public static IReadOnlyList<IntegrationEvent> GetPublishedEvents() => _publishedEvents.AsReadOnly();

    private static void EnsureAuthoritativeTenantScope(IntegrationEvent @event)
    {
        if (!IsAuthoritativeEventType(@event.GetType()))
        {
            return;
        }

        ValidateAuthoritativeEventContract(@event.GetType());

        if (@event.TenantId.HasValue && @event.TenantId.Value != Guid.Empty)
        {
            EnsureAuthoritativeOrderingMetadata(@event);
            EnsureAffectedTenantScope(@event);
            return;
        }

        throw new InvalidOperationException(
            $"Authoritative integration event {@event.GetType().Name} requires a non-empty TenantId.");
    }

    private static void EnsureAuthoritativeOrderingMetadata(IntegrationEvent @event)
    {
        var aggregateId = IntegrationEventAuthority.TryGetAggregateId(@event);
        if (!aggregateId.HasValue || aggregateId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Authoritative integration event {@event.GetType().Name} requires a non-empty aggregate ordering key.");
        }

        if (@event.OccurredAtUtc == default || @event.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"Authoritative integration event {@event.GetType().Name} requires a non-default UTC OccurredAtUtc.");
        }

        if (@event.EventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Authoritative integration event {@event.GetType().Name} requires a positive EventVersion.");
        }
    }

    private static void EnsureAffectedTenantScope(IntegrationEvent @event)
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
                $"Affected-tenant authoritative event {@event.GetType().Name} requires at least one affected tenant.");
        }

        if (!@event.TenantId.HasValue || !affectedTenantIds.Contains(@event.TenantId.Value))
        {
            throw new InvalidOperationException(
                $"Authoritative event {@event.GetType().Name} TenantId must be one of the affected tenant ids.");
        }
    }

    /// <summary>
    /// Determines if an event type is authoritative based on the explicit marker interface
    /// and a narrow legacy-name fallback for older event types that have not been annotated yet.
    /// Authoritative events must go through the transactional outbox and cannot bypass it.
    /// </summary>
    private static bool IsAuthoritativeEventType(Type eventType) =>
        IntegrationEventAuthority.IsAuthoritativeEventType(eventType);

    private static void ValidateAuthoritativeEventContract(Type eventType) =>
        IntegrationEventAuthority.ValidateAuthoritativeEventContract(eventType);
    
    /// <summary>
    /// Clear all published events (for testing purposes)
    /// </summary>
    public static void ClearPublishedEvents() => _publishedEvents.Clear();
}
