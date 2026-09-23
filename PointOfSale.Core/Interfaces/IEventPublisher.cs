using PointOfSale.Core.Events;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Interface for publishing integration events to the message bus
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish an event to the message bus
    /// </summary>
    /// <typeparam name="T">Type of the event</typeparam>
    /// <param name="event">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent;

    /// <summary>
    /// Publish a legacy event type through the guaranteed outbox-backed publisher.
    /// Prefer the generic IntegrationEvent overload for new events.
    /// </summary>
    /// <param name="event">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync(object @event, CancellationToken cancellationToken = default)
    {
        if (@event is null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        if (@event is IntegrationEvent integrationEvent)
        {
            return PublishAsync<IntegrationEvent>(integrationEvent, cancellationToken);
        }

        throw new NotSupportedException(
            $"Publisher {GetType().Name} does not support legacy event type '{@event.GetType().Name}'.");
    }
    
    /// <summary>
    /// Publish multiple events in a batch
    /// </summary>
    /// <typeparam name="T">Type of the events</typeparam>
    /// <param name="events">The events to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent;

    /// <summary>
    /// Publish a batch containing different integration or legacy event types.
    /// </summary>
    Task PublishBatchAsync(
        IEnumerable<object> events,
        CancellationToken cancellationToken = default)
    {
        return PublishBatchFallbackAsync(events, cancellationToken);

        async Task PublishBatchFallbackAsync(
            IEnumerable<object> pendingEvents,
            CancellationToken ct)
        {
            foreach (var pendingEvent in pendingEvents)
            {
                await PublishAsync(pendingEvent, ct);
            }
        }
    }

    /// <summary>
    /// Publish a sensitive event directly to the message bus, bypassing the outbox.
    /// Use this for events containing sensitive data (e.g., payment proof, PII) that should not be persisted.
    /// WARNING: This bypasses transactional guarantees - only use for non-critical, sensitive data.
    /// </summary>
    /// <typeparam name="T">Type of the event (must implement ISensitiveIntegrationEvent marker)</typeparam>
    /// <param name="event">The sensitive event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent, ISensitiveIntegrationEvent;
}
