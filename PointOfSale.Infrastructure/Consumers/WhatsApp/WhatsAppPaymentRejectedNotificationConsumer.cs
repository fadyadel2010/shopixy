using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;

namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

/// <summary>
/// Consumes WhatsAppPaymentRejectedNotificationEvent to notify customer.
/// NON-BLOCKING: Failures do not affect payment processing.
/// </summary>
public class WhatsAppPaymentRejectedNotificationConsumer : IConsumer<WhatsAppPaymentRejectedNotificationEvent>
{
    private readonly IWhatsAppPaymentNotificationService _notificationService;
    private readonly ILogger<WhatsAppPaymentRejectedNotificationConsumer> _logger;

    public WhatsAppPaymentRejectedNotificationConsumer(
        IWhatsAppPaymentNotificationService notificationService,
        ILogger<WhatsAppPaymentRejectedNotificationConsumer> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WhatsAppPaymentRejectedNotificationEvent> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "[WhatsApp Payment] *** CONSUMER INVOKED *** Processing rejection notification | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | CustomerPhone: {CustomerPhone}",
            msg.TenantId, msg.OrderId, msg.OrderNumber, string.IsNullOrWhiteSpace(msg.CustomerPhoneNumber) ? "EMPTY" : "PROVIDED");

        try
        {
            _logger.LogInformation(
                "[WhatsApp Payment] Calling notification service | TenantId: {TenantId} | OrderId: {OrderId}",
                msg.TenantId, msg.OrderId);

            await _notificationService.NotifyCustomerPaymentRejectedAsync(
                msg.TenantId!.Value,
                msg.OrderId,
                msg.PaymentIntentId,
                msg.OrderNumber,
                msg.CustomerPhoneNumber,
                msg.RejectionReason,
                context.CancellationToken);

            _logger.LogInformation(
                "[WhatsApp Payment] Notification service call completed | TenantId: {TenantId} | OrderId: {OrderId}",
                msg.TenantId, msg.OrderId);
        }
        catch (Exception ex)
        {
            // CRITICAL: Catch all exceptions to prevent message redelivery
            // WhatsApp failures should not block payment processing
            _logger.LogError(ex,
                "[WhatsApp Payment] Failed to process rejection notification (non-fatal) | TenantId: {TenantId} | OrderId: {OrderId}",
                msg.TenantId, msg.OrderId);
        }
    }
}
