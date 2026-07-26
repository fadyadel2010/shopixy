using PointOfSale.Core.Events;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// Stable identity for a projection consumer's idempotency fence.
/// The tuple must stay durable across deployments because it keys SQL ProcessedEvents.
/// </summary>
public sealed record ProjectionEventFenceKey(
    string ConsumerName,
    string EventType,
    Guid EntityId)
{
    public static ProjectionEventFenceKey For<TConsumer, TEvent>(
        Guid entityId,
        string? consumerName = null)
        where TEvent : IntegrationEvent
    {
        var eventType = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        return new ProjectionEventFenceKey(
            consumerName ?? $"{typeof(TConsumer).Name}:{typeof(TEvent).Name}",
            eventType,
            entityId);
    }
}
