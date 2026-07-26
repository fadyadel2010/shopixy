using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Notifications;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ShipmentCancelledEvent notifications.
/// Phase 5.3: Tracking & Notifications
/// 
/// Sends "Shipment cancelled" notification to customer.
/// 
/// Payload includes:
/// - Order number
/// - Shipment number
/// - Cancellation reason (if available)
/// - Previous status before cancellation
/// 
/// IMPORTANT:
/// - Never throws exceptions
/// - Never blocks shipment lifecycle
/// - Idempotent via Redis
/// </summary>
public class ShipmentCancelledNotificationConsumer 
    : ShipmentNotificationConsumerBase<ShipmentCancelledEvent>,
      IConsumer<ShipmentCancelledEvent>
{
    public ShipmentCancelledNotificationConsumer(
        INotificationSender notificationSender,
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<ShipmentCancelledNotificationConsumer> logger)
        : base(notificationSender, connectionFactory, settings, logger)
    {
    }

    protected override string NotificationType => NotificationTypes.ShipmentCancelled;

    public async Task Consume(ConsumeContext<ShipmentCancelledEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        Logger.LogInformation(
            "Processing ShipmentCancelledEvent for notification | EventId: {EventId} | " +
            "ShipmentId: {ShipmentId} | OrderId: {OrderId} | TenantId: {TenantId} | " +
            "Reason: {Reason} | PreviousStatus: {PreviousStatus} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ShipmentId,
            @event.OrderId,
            @event.TenantId,
            @event.CancellationReason,
            @event.PreviousStatus,
            correlationId);

        try
        {
            // Validate TenantId
            if (!@event.TenantId.HasValue || @event.TenantId.Value == Guid.Empty)
            {
                Logger.LogWarning(
                    "Notification SKIPPED: Invalid TenantId | EventId: {EventId} | ShipmentId: {ShipmentId}",
                    @event.EventId,
                    @event.ShipmentId);
                return;
            }

            var tenantId = @event.TenantId.Value;

            // Idempotency check
            if (await IsAlreadyProcessedAsync(tenantId, @event.EventId, context.CancellationToken))
            {
                return; // Already processed
            }

            // Build notification message
            var message = new NotificationMessage
            {
                TenantId = tenantId,
                OrderId = @event.OrderId,
                ShipmentId = @event.ShipmentId,
                Type = NotificationType,
                Channel = NotificationSender.Channel,
                Recipient = @event.OrderNumber, // In production, this would be email/phone from customer data
                OrderNumber = @event.OrderNumber,
                ShipmentNumber = @event.ShipmentNumber,
                EventId = @event.EventId,
                CorrelationId = correlationId,
                OccurredAtUtc = @event.OccurredAtUtc,
                Payload = new Dictionary<string, string>
                {
                    ["CancelledAt"] = @event.CancelledAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    ["PreviousStatus"] = @event.PreviousStatus.ToString(),
                    ["Subject"] = "Shipment Cancelled",
                    ["MessageBody"] = $"We're sorry, but shipment {{{@event.ShipmentNumber}}} for order {{{@event.OrderNumber}}} has been cancelled."
                }
            };

            // Add cancellation reason if available
            if (!string.IsNullOrEmpty(@event.CancellationReason))
            {
                message.Payload["CancellationReason"] = @event.CancellationReason;
                message.Payload["MessageBody"] += $" Reason: {{{@event.CancellationReason}}}";
            }

            // Send notification
            var success = await SendNotificationAsync(message, context.CancellationToken);

            // Mark as processed
            await MarkAsProcessedAsync(tenantId, @event.EventId, context.CancellationToken);

            Logger.LogInformation(
                "ShipmentCancelledNotificationConsumer completed | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | Reason: {Reason} | Success: {Success} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ShipmentId,
                @event.CancellationReason,
                success,
                correlationId);
        }
        catch (Exception ex)
        {
            // NEVER throw - notifications are best-effort
            Logger.LogError(ex,
                "Unexpected error in ShipmentCancelledNotificationConsumer | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}. Event will be acknowledged.",
                @event.EventId,
                @event.ShipmentId,
                @event.TenantId,
                correlationId);
        }
    }
}
