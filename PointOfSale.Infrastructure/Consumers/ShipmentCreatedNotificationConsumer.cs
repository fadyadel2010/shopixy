using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Notifications;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ShipmentCreatedEvent notifications.
/// Phase 5.3: Tracking & Notifications
/// 
/// Sends "Order confirmed & shipment created" notification to customer.
/// 
/// Payload includes:
/// - Order number
/// - Shipment number
/// - Shipping method
/// - Carrier (if available)
/// - Estimated delivery date (if available)
/// - Shipping address
/// 
/// IMPORTANT:
/// - Never throws exceptions
/// - Never blocks shipment lifecycle
/// - Idempotent via Redis
/// </summary>
public class ShipmentCreatedNotificationConsumer 
    : ShipmentNotificationConsumerBase<ShipmentCreatedEvent>,
      IConsumer<ShipmentCreatedEvent>
{
    public ShipmentCreatedNotificationConsumer(
        INotificationSender notificationSender,
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<ShipmentCreatedNotificationConsumer> logger)
        : base(notificationSender, connectionFactory, settings, logger)
    {
    }

    protected override string NotificationType => NotificationTypes.ShipmentCreated;

    public async Task Consume(ConsumeContext<ShipmentCreatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        Logger.LogInformation(
            "Processing ShipmentCreatedEvent for notification | EventId: {EventId} | " +
            "ShipmentId: {ShipmentId} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ShipmentId,
            @event.OrderId,
            @event.TenantId,
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
                Recipient = @event.RecipientName, // In production, this would be email/phone from customer data
                OrderNumber = @event.OrderNumber,
                ShipmentNumber = @event.ShipmentNumber,
                EventId = @event.EventId,
                CorrelationId = correlationId,
                OccurredAtUtc = @event.OccurredAtUtc,
                Payload = new Dictionary<string, string>
                {
                    ["ShippingMethod"] = @event.ShippingMethodName,
                    ["ShippingAddress"] = @event.ShippingAddress,
                    ["ShippingCost"] = $"{@event.ShippingCost} {@event.Currency}",
                    ["Subject"] = "Order Confirmed & Shipment Created",
                    ["MessageBody"] = $"Your order {{{@event.OrderNumber}}} has been confirmed. Shipment {{{@event.ShipmentNumber}}} has been created."
                }
            };

            // Add optional fields
            if (!string.IsNullOrEmpty(@event.CarrierName))
            {
                message.Payload["CarrierName"] = @event.CarrierName;
            }

            if (@event.EstimatedDeliveryDate.HasValue)
            {
                message.Payload["EstimatedDeliveryDate"] = @event.EstimatedDeliveryDate.Value.ToString("yyyy-MM-dd");
            }

            // Send notification
            var success = await SendNotificationAsync(message, context.CancellationToken);

            // Mark as processed (regardless of send success - we don't want to retry endlessly)
            await MarkAsProcessedAsync(tenantId, @event.EventId, context.CancellationToken);

            Logger.LogInformation(
                "ShipmentCreatedNotificationConsumer completed | EventId: {EventId} | " +
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
                "Unexpected error in ShipmentCreatedNotificationConsumer | EventId: {EventId} | " +
                "ShipmentId: {ShipmentId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}. Event will be acknowledged.",
                @event.EventId,
                @event.ShipmentId,
                @event.TenantId,
                correlationId);
        }
    }
}
