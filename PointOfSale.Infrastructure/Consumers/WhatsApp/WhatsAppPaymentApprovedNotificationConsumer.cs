using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;

namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

/// <summary>
/// Consumes WhatsAppPaymentApprovedNotificationEvent to notify customer.
/// NON-BLOCKING: Failures do not affect payment processing.
/// </summary>
public class WhatsAppPaymentApprovedNotificationConsumer : IConsumer<WhatsAppPaymentApprovedNotificationEvent>
{
    private readonly IWhatsAppPaymentNotificationService _notificationService;
    private readonly ILogger<WhatsAppPaymentApprovedNotificationConsumer> _logger;

    public WhatsAppPaymentApprovedNotificationConsumer(
        IWhatsAppPaymentNotificationService notificationService,
        ILogger<WhatsAppPaymentApprovedNotificationConsumer> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WhatsAppPaymentApprovedNotificationEvent> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "[WhatsApp Payment] Processing approval notification | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
            msg.TenantId, msg.OrderId, msg.OrderNumber);

        try
        {
            await _notificationService.NotifyCustomerPaymentApprovedAsync(
                msg.TenantId!.Value,
                msg.OrderId,
                msg.PaymentIntentId,
                msg.OrderNumber,
                msg.CustomerPhoneNumber,
                msg.Amount,
                msg.Currency,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // CRITICAL: Catch all exceptions to prevent message redelivery
            // WhatsApp failures should not block payment processing
            _logger.LogError(ex,
                "[WhatsApp Payment] Failed to process approval notification (non-fatal) | TenantId: {TenantId} | OrderId: {OrderId}",
                msg.TenantId, msg.OrderId);
        }
    }
}
