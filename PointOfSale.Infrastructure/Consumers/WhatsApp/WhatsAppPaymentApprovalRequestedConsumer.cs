using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;

namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

/// <summary>
/// Consumes WhatsAppPaymentApprovalRequestedEvent to notify admin.
/// NON-BLOCKING: Failures do not affect payment processing.
/// </summary>
public class WhatsAppPaymentApprovalRequestedConsumer : IConsumer<WhatsAppPaymentApprovalRequestedEvent>
{
    private readonly IWhatsAppPaymentNotificationService _notificationService;
    private readonly ILogger<WhatsAppPaymentApprovalRequestedConsumer> _logger;

    public WhatsAppPaymentApprovalRequestedConsumer(
        IWhatsAppPaymentNotificationService notificationService,
        ILogger<WhatsAppPaymentApprovalRequestedConsumer> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WhatsAppPaymentApprovalRequestedEvent> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "[WhatsApp Payment] Processing approval request | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
            msg.TenantId, msg.OrderId, msg.OrderNumber);

        try
        {
            await _notificationService.NotifyAdminPaymentSubmittedAsync(
                msg.TenantId!.Value,
                msg.OrderId,
                msg.PaymentIntentId,
                msg.OrderNumber,
                msg.Amount,
                msg.Currency,
                msg.PaymentMethod,
                msg.AdminPhoneNumber,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // CRITICAL: Catch all exceptions to prevent message redelivery
            // WhatsApp failures should not block payment processing
            _logger.LogError(ex,
                "[WhatsApp Payment] Failed to process approval request (non-fatal) | TenantId: {TenantId} | OrderId: {OrderId}",
                msg.TenantId, msg.OrderId);
        }
    }
}
