using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Notifications;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Base class for shipment notification consumers.
/// Phase 5.3: Tracking & Notifications
/// 
/// Provides common functionality for all notification consumers:
/// - Idempotency via Redis (tenant:{tenantId}:notification:{eventId})
/// - Best-effort notification sending (never break flow)
/// - Structured logging for observability
/// - Graceful failure handling
/// 
/// Error Handling Rules:
/// - Notification sender fails → Log warning, ACK event
/// - Redis unavailable → Skip idempotency, still send notification
/// - Missing recipient → Log warning, ACK event
/// - Invalid payload → Log error, ACK event
/// 
/// IMPORTANT:
/// - NEVER throw exceptions from these consumers
/// - NEVER retry infinitely
/// - NEVER block shipment lifecycle
/// - Notifications are best-effort, NOT guaranteed delivery
/// </summary>
public abstract class ShipmentNotificationConsumerBase<TEvent>
    where TEvent : class
{
    protected readonly INotificationSender NotificationSender;
    protected readonly RedisConnectionFactory ConnectionFactory;
    protected readonly RedisSettings Settings;
    protected readonly ILogger Logger;

    // Idempotency check TTL (24 hours)
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    protected ShipmentNotificationConsumerBase(
        INotificationSender notificationSender,
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger logger)
    {
        NotificationSender = notificationSender;
        ConnectionFactory = connectionFactory;
        Settings = settings.Value;
        Logger = logger;
    }

    /// <summary>
    /// Gets the notification type for this consumer.
    /// </summary>
    protected abstract string NotificationType { get; }

    /// <summary>
    /// Checks if this event has already been processed (idempotency).
    /// Returns true if already processed, false if new.
    /// 
    /// IMPORTANT: If Redis is unavailable, returns false (proceed with send)
    /// </summary>
    protected async Task<bool> IsAlreadyProcessedAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken)
    {
        var database = ConnectionFactory.GetDatabase();
        if (database == null)
        {
            Logger.LogDebug(
                "Idempotency check SKIPPED (Redis unavailable) | EventId: {EventId} | TenantId: {TenantId}",
                eventId,
                tenantId);
            return false; // Proceed with notification if Redis unavailable
        }

        var key = CacheKeyBuilder.NotificationIdempotencyKey(Settings.InstanceName, tenantId, eventId);

        try
        {
            var exists = await database.KeyExistsAsync(key);
            if (exists)
            {
                Logger.LogInformation(
                    "Notification SKIPPED (idempotent) | Type: {Type} | EventId: {EventId} | TenantId: {TenantId}",
                    NotificationType,
                    eventId,
                    tenantId);
            }
            return exists;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Idempotency check failed | EventId: {EventId} | TenantId: {TenantId}. Proceeding with notification.",
                eventId,
                tenantId);
            return false; // Proceed with notification on error
        }
    }

    /// <summary>
    /// Marks the event as processed for idempotency tracking.
    /// </summary>
    protected async Task MarkAsProcessedAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken)
    {
        var database = ConnectionFactory.GetDatabase();
        if (database == null)
        {
            Logger.LogDebug(
                "Idempotency mark SKIPPED (Redis unavailable) | EventId: {EventId} | TenantId: {TenantId}",
                eventId,
                tenantId);
            return;
        }

        var key = CacheKeyBuilder.NotificationIdempotencyKey(Settings.InstanceName, tenantId, eventId);

        try
        {
            await database.StringSetAsync(key, "1", IdempotencyTtl);
            Logger.LogDebug(
                "Idempotency key SET | Key: {Key} | EventId: {EventId} | TenantId: {TenantId}",
                key,
                eventId,
                tenantId);
        }
        catch (Exception ex)
        {
            // Log but don't fail - idempotency is best-effort
            Logger.LogWarning(ex,
                "Failed to set idempotency key | Key: {Key} | EventId: {EventId} | TenantId: {TenantId}",
                key,
                eventId,
                tenantId);
        }
    }

    /// <summary>
    /// Sends a notification message using the configured sender.
    /// </summary>
    protected async Task<bool> SendNotificationAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var success = await NotificationSender.SendAsync(message, cancellationToken);

            Logger.LogInformation(
                "Notification attempt | Type: {Type} | Channel: {Channel} | " +
                "Success: {Success} | TenantId: {TenantId} | OrderId: {OrderId} | " +
                "ShipmentId: {ShipmentId} | EventId: {EventId} | Recipient: {Recipient} | CorrelationId: {CorrelationId}",
                message.Type,
                message.Channel,
                success,
                message.TenantId,
                message.OrderId,
                message.ShipmentId,
                message.EventId,
                message.Recipient,
                message.CorrelationId);

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Notification send failed | Type: {Type} | TenantId: {TenantId} | " +
                "OrderId: {OrderId} | ShipmentId: {ShipmentId} | EventId: {EventId} | CorrelationId: {CorrelationId}",
                message.Type,
                message.TenantId,
                message.OrderId,
                message.ShipmentId,
                message.EventId,
                message.CorrelationId);
            return false;
        }
    }
}
