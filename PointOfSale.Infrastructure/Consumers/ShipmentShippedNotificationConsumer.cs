using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Notifications;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ShipmentShippedEvent notifications.
/// Phase 5.3: Tracking & Notifications
/// 
/// Sends "Tracking number available" notification to customer.
/// 
/// Payload includes:
/// - Order number
/// - Shipment number
/// - Tracking number
/// - Carrier name
/// - Tracking URL (if available)
/// - Estimated delivery date (if available)
/// 
/// IMPORTANT:
/// - Never throws exceptions
/// - Never blocks shipment lifecycle
/// - Idempotent via Redis
/// </summary>
public class ShipmentShippedNotificationConsumer 
    : ShipmentNotificationConsumerBase<ShipmentShippedEvent>,
      IConsumer<ShipmentShippedEvent>
{
    public ShipmentShippedNotificationConsumer(
        INotificationSender notificationSender,
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<ShipmentShippedNotificationConsumer> logger)
        : base(notificationSender, connectionFactory, settings, logger)
    {
    }

    protected override string NotificationType => NotificationTypes.ShipmentShipped;

    public async Task Consume(ConsumeContext<ShipmentShippedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        Logger.LogInformation(
            "Processing ShipmentShippedEvent for notification | EventId: {EventId} | " +
            "ShipmentId: {ShipmentId} | OrderId: {OrderId} | TenantId: {TenantId} | " +
            "TrackingNumber: {TrackingNumber} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ShipmentId,
            @event.OrderId,
            @event.TenantId,
            @event.TrackingNumber,
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
                    ["TrackingNumber"] = @event.TrackingNumber,
                    ["ShippedAt"] = @event.ShippedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    ["Subject"] = "Your Order Has Shipped!",
                    ["MessageBody"] = $"Great news! Your order {{{@event.OrderNumber}}} has been shipped. Tracking number: {{{@event.TrackingNumber}}}"
                }
            };

            // Add optional fields
            if (!string.IsNullOrEmpty(@event.CarrierName))
            {
                message.Payload["CarrierName"] = @event.CarrierName;
            }

            if (!string.IsNullOrEmpty(@event.TrackingUrl))
            {
                message.Payload["TrackingUrl"] = @event.TrackingUrl;
            }

            if (@event.EstimatedDeliveryDate.HasValue)
            {
                message.Payload["EstimatedDeliveryDate"] = @event.EstimatedDeliveryDate.Value.ToString("yyyy-MM-dd");
            }

            // Send notification
            var success = await SendNotificationAsync(message, context.CancellationToken);

            // Mark as processed
            await MarkAsProcessedAsync(tenantId, @event.EventId, context.CancellationToken);

            Logger.LogInformation(
                "ShipmentShippedNotificationConsumer completed | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | Success: {Success} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ShipmentId,
                @event.TrackingNumber,
                success,
                correlationId);
        }
        catch (Exception ex)
        {
            // NEVER throw - notifications are best-effort
            Logger.LogError(ex,
                "Unexpected error in ShipmentShippedNotificationConsumer | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}. Event will be acknowledged.",
                @event.EventId,
                @event.ShipmentId,
                @event.TenantId,
                correlationId);
        }
    }
}
