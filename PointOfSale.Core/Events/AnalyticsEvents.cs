using PointOfSale.Core.Analytics;

namespace PointOfSale.Core.Events;

/// <summary>
/// Integration event published when an analytics event is received and validated.
/// 
/// EVENT PIPELINE:
/// StorefrontAnalyticsController
///    ↓
/// AnalyticsEventValidator
///    ↓
/// EventBus (MassTransit)
///    ↓
/// AnalyticsEventConsumer
///    ├── Redis Store (Hot)
///    └── Mongo Store (Cold)
/// 
/// RULES:
/// - Controller MUST NOT write directly to storage
/// - Consumer handles both Redis and MongoDB writes
/// - Failures in Mongo MUST NOT block Redis write
/// - Consumer MUST be idempotent (by EventId)
/// </summary>
public class AnalyticsEventReceivedEvent : IntegrationEvent
{
    /// <summary>
    /// The canonical analytics event to be stored.
    /// </summary>
    public AnalyticsEvent AnalyticsEvent { get; set; } = null!;

    /// <summary>
    /// Creates the integration event with proper tenant context.
    /// </summary>
    public static AnalyticsEventReceivedEvent Create(AnalyticsEvent analyticsEvent)
    {
        // Guard against empty EventId to prevent duplicate IdempotencyKey/CorrelationId
        var eventId = analyticsEvent.EventId;
        if (eventId == Guid.Empty)
        {
            eventId = Guid.NewGuid();
            analyticsEvent.EventId = eventId;
        }

        return new AnalyticsEventReceivedEvent
        {
            EventId = eventId,
            TenantId = analyticsEvent.TenantId,
            OccurredAtUtc = DateTime.UtcNow,
            AnalyticsEvent = analyticsEvent,
            CorrelationId = eventId.ToString("D"),
            IdempotencyKey = $"analytics:{eventId}"
        };
    }
}
