using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Notifications;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ShipmentDeliveredEvent notifications.
/// Phase 5.3: Tracking & Notifications
/// 
/// Sends "Order delivered" notification to customer.
/// 
/// Payload includes:
/// - Order number
/// - Shipment number
/// - Delivery date/time
/// - Carrier name (if available)
/// - Tracking number (if available)
/// 
/// IMPORTANT:
/// - Never throws exceptions
/// - Never blocks shipment lifecycle
/// - Idempotent via Redis
/// </summary>
public class ShipmentDeliveredNotificationConsumer 
    : ShipmentNotificationConsumerBase<ShipmentDeliveredEvent>,
      IConsumer<ShipmentDeliveredEvent>
{
    public ShipmentDeliveredNotificationConsumer(
        INotificationSender notificationSender,
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<ShipmentDeliveredNotificationConsumer> logger)
        : base(notificationSender, connectionFactory, settings, logger)
    {
    }

    protected override string NotificationType => NotificationTypes.ShipmentDelivered;

    public async Task Consume(ConsumeContext<ShipmentDeliveredEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        Logger.LogInformation(
            "Processing ShipmentDeliveredEvent for notification | EventId: {EventId} | " +
            "ShipmentId: {ShipmentId} | OrderId: {OrderId} | TenantId: {TenantId} | " +
            "DeliveredAt: {DeliveredAt} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ShipmentId,
            @event.OrderId,
            @event.TenantId,
            @event.DeliveredAt,
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
                    ["DeliveredAt"] = @event.DeliveredAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    ["Subject"] = "Your Order Has Been Delivered!",
                    ["MessageBody"] = $"Your order {{{@event.OrderNumber}}} has been delivered. Thank you for shopping with us!"
                }
            };

            // Add optional fields
            if (!string.IsNullOrEmpty(@event.CarrierName))
            {
                message.Payload["CarrierName"] = @event.CarrierName;
            }

            if (!string.IsNullOrEmpty(@event.TrackingNumber))
            {
                message.Payload["TrackingNumber"] = @event.TrackingNumber;
            }

            if (!string.IsNullOrEmpty(@event.Notes))
            {
                message.Payload["DeliveryNotes"] = @event.Notes;
            }

            // Send notification
            var success = await SendNotificationAsync(message, context.CancellationToken);

            // Mark as processed
            await MarkAsProcessedAsync(tenantId, @event.EventId, context.CancellationToken);

            Logger.LogInformation(
                "ShipmentDeliveredNotificationConsumer completed | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | Success: {Success} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ShipmentId,
                success,
                correlationId);
        }
        catch (Exception ex)
        {
            // NEVER throw - notifications are best-effort
            Logger.LogError(ex,
                "Unexpected error in ShipmentDeliveredNotificationConsumer | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}. Event will be acknowledged.",
                @event.EventId,
                @event.ShipmentId,
                @event.TenantId,
                correlationId);
        }
    }
}
