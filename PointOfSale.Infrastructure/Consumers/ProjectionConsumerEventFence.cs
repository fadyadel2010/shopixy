using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Compatibility facade for consumers that have not yet migrated to ProjectionEventFence.
/// New consumers should use PointOfSale.Infrastructure.Reliability.ProjectionEventFence directly.
/// </summary>
internal sealed class ProjectionConsumerEventFence : IAsyncDisposable
{
    private readonly ProjectionEventFence _inner;
    private readonly ProjectionEventFenceKey _key;

    private ProjectionConsumerEventFence(ProjectionEventFence inner, ProjectionEventFenceKey key)
    {
        _inner = inner;
        _key = key;
    }

    public static async Task<ProjectionConsumerEventFence?> TryAcquireAsync(
        ApplicationDbContext dbContext,
        IntegrationEvent @event,
        string consumerName,
        string eventType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var key = new ProjectionEventFenceKey(consumerName, eventType, entityId);
        var inner = await ProjectionEventFence.TryAcquireAsync(
            dbContext,
            @event,
            key,
            NullLogger.Instance,
            cancellationToken);

        return inner == null ? null : new ProjectionConsumerEventFence(inner, key);
    }

    public Task CompleteAsync(
        IntegrationEvent @event,
        string consumerName,
        string eventType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var key = consumerName == _key.ConsumerName &&
                  eventType == _key.EventType &&
                  entityId == _key.EntityId
            ? _key
            : new ProjectionEventFenceKey(consumerName, eventType, entityId);

        return _inner.CompleteAsync(@event, key, cancellationToken);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
